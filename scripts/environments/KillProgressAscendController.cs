using System;
using Godot;
using Kuros.Core;
using Kuros.Systems.Cutscene;

namespace Kuros.Environments
{
	/// <summary>
	/// F_begin「上升井道」的关卡侧进度载体（挂在房间根节点上），一件事两面用途：
	///   1) **击杀统计**：静态事件 <see cref="GameActor.DeathFinalized"/> + "enemies" 组过滤。
	///      常态免伤的本体/磁铁臂走不到 FinalizeDeath，天然不计入；生成的敌人挂在世界节点下（不在本房间子树里），
	///      所以只能按组统计、不能按子树。
	///   2) **上升视觉**：进度 0..1 → AnimationPlayer.SpeedScale（越接近满进度越快）。**只调速、不当刹车**：
	///      进度满只是交还速度控制，上升循环继续跑到到站过场接手；**停住**发生在到站过场开始那一刻
	///      （AnimationPlayer.Pause，保持姿态）。不靠 SpeedScale 归零/渐减来刹车：否则残留会给随后在同一播放器上
	///      播的到站动画（up_to_top）留下"时间不走 / 速度非 1"的假播放，把过场卡死。
	///   3) **进度是公开数据**：消费者（Boss 的大招阈值、按进度的形态升级…）自己读
	///      <see cref="Progress"/> / <see cref="ProgressChanged"/> 并**各自定义阈值**——与血量阈值敌人
	///      （UltimateHealthThresholds 放在敌人自己的控制器里）同构。本控制器不替消费者定义阈值，
	///      也不广播阈值事件（轮询式判断天然幂等：晚生成、一次跨多个阈值都不会漏/重）。
	/// 到站（进度满）：**交还上升速度控制**（循环继续跑）+ 武装 ArrivalTrigger（触发区覆盖平台 = 立刻播；
	/// 挪到出口 = 走过去才播；可再用 ArrivalTriggerDelay 延后几秒）；到站过场一开始**暂停上升循环**并释放速度接管
	/// ——停在这里而不是进度满那一刻，因为两者之间可能隔着一段延迟，电梯在那段时间里应该还在往上走。
	///
	/// 时序铁律：loop 动画真正挂上（CurrentAnimation == LoopAnimationName）之前**一个字节都不写 SpeedScale**——
	/// 过场的 `up` 是 WaitForCompletion 阻塞式，写成 0 会把整段过场冻死（后续 Boss/滑槽都不会出场）。
	/// </summary>
	[GlobalClass]
	public partial class KillProgressAscendController : Node2D
	{
		/// <summary>组名：Boss 侧与其它消费者用 Find/GetFirstNodeInGroup 找本控制器。
		/// 注意不要再加进 player/enemies 组——CameraFollow 的局部顿帧会冻结组内节点。</summary>
		public const string ProgressGroup = "kill_progress";

		[ExportCategory("Kill Progress")]
		/// <summary>满进度所需的击杀数（进度 = 击杀数 / 本值，钳到 0..1）。</summary>
		[Export(PropertyHint.Range, "1,500,1")] public int TotalKillsForFull { get; set; } = 20;

		[ExportCategory("Ascend Visual")]
		[Export] public NodePath AnimationPlayerPath { get; set; } = new("AnimationPlayer");
		/// <summary>"持续上升"的循环动画名（F_begin 是 up_loop）。</summary>
		[Export] public string LoopAnimationName { get; set; } = "up_loop";
		[Export(PropertyHint.Range, "0,10,0.05")] public float MinSpeedScale { get; set; } = 1f;
		[Export(PropertyHint.Range, "0,10,0.05")] public float MaxSpeedScale { get; set; } = 2f;
		/// <summary>速度平滑（每秒逼近系数，0 = 立即跳变）。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float SpeedScaleLerpSpeed { get; set; } = 2.5f;
		[Export] public bool DriveAscendVisual { get; set; } = true;

		[ExportCategory("Arrival")]
		/// <summary>进度满时武装的到站触发器（调它的 Arm()，路径相对本节点）。触发区覆盖平台 → 立即播；放到出口 → 玩家走过去才播。</summary>
		[Export] public NodePath ArrivalTriggerPath { get; set; } = new();
		/// <summary>到站触发器的**武装延迟**（秒）：进度满后先等这么久再 Arm（"堵塞时间"）。
		/// 典型用法：满进度后的收尾动作要占用几秒（如敌人/机械臂按 FinishReturnDuration 退场），
		/// 把本值设成同一时长 → 过场在它们退干净之后才触发。0 = 立刻武装（旧行为）。</summary>
		[Export(PropertyHint.Range, "0,30,0.1")] public float ArrivalTriggerDelay { get; set; } = 0f;

		[ExportCategory("Debug")]
		[Export] public bool EnableDebugLogs { get; set; } = false;

		[Signal] public delegate void AscendArrivedEventHandler();

		public int KillCount { get; private set; }
		/// <summary>0..1，连续。消费者（Boss 阈值等）自行取用。</summary>
		public float Progress { get; private set; }
		/// <summary>是否已接管 loop 动画的速度控制。</summary>
		public bool VisualDriveActive { get; private set; }
		/// <summary>进度是否已满（到站）。</summary>
		public bool HasArrived { get; private set; }

		/// <summary>(killCount, progress)</summary>
		public event Action<int, float>? KillCountChanged;
		public event Action<float>? ProgressChanged;

		private AnimationPlayer? _animPlayer;
		private float _speedScaleBeforeDrive = 1f;
		private CutsceneManager? _cutsceneManager;
		private bool _controlReleased;
		// 到站触发器的延迟武装（ArrivalTriggerDelay）
		private float _arrivalArmRemaining;
		private bool _arrivalTriggerArmed;

		public override void _EnterTree()
		{
			AddToGroup(ProgressGroup);
		}

		public override void _ExitTree()
		{
			// 静态事件必须成对退订（GameMemoryService 只退订了实例事件，别照抄它那段）
			GameActor.DeathFinalized -= OnActorDeathFinalized;
			UnsubscribeCutsceneManager();
		}

		public override void _Ready()
		{
			_animPlayer = AnimationPlayerPath.IsEmpty ? null : GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
			if (DriveAscendVisual && _animPlayer == null)
				GD.PushWarning($"{Name}: 未找到 AnimationPlayer（{AnimationPlayerPath}），上升视觉不会被驱动");

			GameActor.DeathFinalized += OnActorDeathFinalized;
		}

		public override void _PhysicsProcess(double delta)
		{
			TickArrivalArm((float)delta);
			if (!DriveAscendVisual || _controlReleased) return;
			if (_animPlayer == null || !GodotObject.IsInstanceValid(_animPlayer)) return;

			if (!VisualDriveActive)
			{
				// 只记数、不碰 SpeedScale：过场可能正在播 up/opening（阻塞式，写 0 会卡死过场）
				if (!IsLoopAnimationActive()) return;

				VisualDriveActive = true;
				_speedScaleBeforeDrive = _animPlayer.SpeedScale; // 以过场设定的值为起点，避免速度跳变
				if (EnableDebugLogs)
					GD.Print($"{Name}: 接管 {LoopAnimationName} 速度控制（起点 {_speedScaleBeforeDrive:F2}）");
			}

			if (!IsLoopAnimationActive())
			{
				// 循环被换掉/停掉（例如过场切了别的动画）→ 归还控制权
				_animPlayer.SpeedScale = _speedScaleBeforeDrive;
				VisualDriveActive = false;
				return;
			}

			// 到站（进度满）：**只交还速度控制，不暂停**——上升循环继续以接管前的速度跑，
			// 直到到站过场用它自己的动画（up_to_top）把循环顶掉；"停住"发生在过场开始那一刻
			// （OnCutsceneStarted 里 Pause），不是进度满那一刻（两者之间可能还隔着 ArrivalTriggerDelay）。
			// 速度**还原**成接管前的值、不渐减到 0：否则会把一个接近 0 / 非 1 的速度残留给随后在
			// 同一个播放器上播出的到站动画（up_to_top 会几乎不动或跑成两倍速）。
			if (HasArrived)
			{
				_animPlayer.SpeedScale = _speedScaleBeforeDrive;
				VisualDriveActive = false;
				_controlReleased = true;
				if (EnableDebugLogs)
					GD.Print($"{Name}: 进度已满 → 交还速度控制（还原为 {_speedScaleBeforeDrive:F2}，上升循环继续跑到过场接手）");
				return;
			}

			float target = Mathf.Lerp(MinSpeedScale, MaxSpeedScale, Progress);
			float current = _animPlayer.SpeedScale;
			_animPlayer.SpeedScale = SpeedScaleLerpSpeed <= 0f
				? target
				: Mathf.Lerp(current, target, Mathf.Clamp(SpeedScaleLerpSpeed * (float)delta, 0f, 1f));
		}

		/// <summary>当前挂的是不是那支后台循环——**不看 IsPlaying()**（SpeedScale = 0 时各版本语义不一致）。</summary>
		private bool IsLoopAnimationActive()
			=> _animPlayer != null && GodotObject.IsInstanceValid(_animPlayer)
			   && _animPlayer.CurrentAnimation == LoopAnimationName;

		// ── 击杀统计 ──────────────────────────────────────────────────────

		private void OnActorDeathFinalized(GameActor actor)
		{
			if (actor == null || !GodotObject.IsInstanceValid(actor)) return;
			if (!actor.IsInGroup("enemies")) return;
			AddKill(1);
		}

		/// <summary>记一次击杀（正常由 DeathFinalized 驱动；无头测试/调试/控制台清场可直接调）。</summary>
		public void AddKill(int amount = 1)
		{
			if (amount <= 0) return;

			KillCount += amount;
			float prev = Progress;
			Progress = TotalKillsForFull > 0
				? Mathf.Clamp((float)KillCount / TotalKillsForFull, 0f, 1f)
				: 0f;

			KillCountChanged?.Invoke(KillCount, Progress);
			if (!Mathf.IsEqualApprox(prev, Progress))
				ProgressChanged?.Invoke(Progress);

			CheckArrival();

			if (EnableDebugLogs)
				GD.Print($"{Name}: kill={KillCount} progress={Progress:F3}");
		}

		// ── 到站 ──────────────────────────────────────────────────────────

		private void CheckArrival()
		{
			if (HasArrived || Progress < 1f) return;

			HasArrived = true;
			EmitSignal(SignalName.AscendArrived);

			// 先订阅再武装：Arm() 可能立刻触发过场（玩家已在区内），信号在那次调用里同步发出
			SubscribeCutsceneManager();

			_arrivalArmRemaining = Mathf.Max(0f, ArrivalTriggerDelay);
			if (_arrivalArmRemaining <= 0f)
			{
				ArmArrivalTrigger();
			}
			else if (EnableDebugLogs)
			{
				GD.Print($"{Name}: 进度已满 → 交还上升速度控制，{_arrivalArmRemaining:F1}s 后武装到站触发器");
			}
		}

		/// <summary>延迟武装倒计时（每帧跑，不受上升驱动/速度接管影响的早退影响）。</summary>
		private void TickArrivalArm(float delta)
		{
			if (!HasArrived || _arrivalTriggerArmed) return;

			_arrivalArmRemaining -= delta;
			if (_arrivalArmRemaining > 0f) return;

			ArmArrivalTrigger();
		}

		/// <summary>武装到站触发器（幂等）。触发区覆盖平台 = 立刻播；放到出口 = 玩家走过去才播。</summary>
		private void ArmArrivalTrigger()
		{
			if (_arrivalTriggerArmed) return;
			_arrivalTriggerArmed = true;

			if (ArrivalTriggerPath.IsEmpty)
			{
				GD.PushWarning($"{Name}: 未配置到站触发器（ArrivalTriggerPath），进度满后不会播到站过场");
				return;
			}

			GetNodeOrNull<CutsceneTrigger>(ArrivalTriggerPath)?.Arm();
			if (EnableDebugLogs) GD.Print($"{Name}: 到站触发器已武装");
		}

		private void SubscribeCutsceneManager()
		{
			if (_cutsceneManager != null && GodotObject.IsInstanceValid(_cutsceneManager)) return;

			_cutsceneManager = GetTree()?.GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
			if (_cutsceneManager != null)
				_cutsceneManager.CutsceneStarted += OnCutsceneStarted;
		}

		private void UnsubscribeCutsceneManager()
		{
			if (_cutsceneManager != null && GodotObject.IsInstanceValid(_cutsceneManager))
				_cutsceneManager.CutsceneStarted -= OnCutsceneStarted;
			_cutsceneManager = null;
		}

		/// <summary>到站过场开始：**暂停上升循环**（保持电梯当前姿态），之后该播放器交给过场自由使用。
		/// 判据是"循环仍是当前动画"而不是"控制器还在驱动"——进度满时已经交还了速度控制，但那时**不该停**：
		/// 停的时机是过场开始（进度满与过场之间可能隔着 ArrivalTriggerDelay）。
		/// 反过来，过场若已经切了自己的动画（up / opening / up_to_top），这里就一个字节都不碰——
		/// Pause 会把过场正在播的动画冻住。
		/// 用 Pause 而不是把 SpeedScale 压到 0：后者会让"在同一播放器上播新动画"变成时间不走的假播放
		/// （IsPlaying 恒真 → WaitForCompletion 永久挂起，up_to_top 那类到站动画会卡死过场）。</summary>
		private void OnCutsceneStarted(string sequenceId)
		{
			if (_animPlayer == null || !GodotObject.IsInstanceValid(_animPlayer)) return;
			if (!IsLoopAnimationActive()) return;

			_animPlayer.SpeedScale = _speedScaleBeforeDrive;
			_animPlayer.Pause();
			VisualDriveActive = false;
			_controlReleased = true;

			if (EnableDebugLogs) GD.Print($"{Name}: 到站过场开始（{sequenceId}）→ 暂停上升循环（保持姿态）");
		}

		// ── 工具 ──────────────────────────────────────────────────────────

		public void ResetProgress()
		{
			KillCount = 0;
			Progress = 0f;
			HasArrived = false;
			_controlReleased = false;
			_arrivalTriggerArmed = false;
			_arrivalArmRemaining = 0f;
			if (_animPlayer != null && GodotObject.IsInstanceValid(_animPlayer))
				_animPlayer.SpeedScale = _speedScaleBeforeDrive;
		}

		/// <summary>按组查找（Boss 侧、UI 等都走这个入口）。</summary>
		public static KillProgressAscendController? Find(Node context)
			=> context?.GetTree()?.GetFirstNodeInGroup(ProgressGroup) as KillProgressAscendController;
	}
}
