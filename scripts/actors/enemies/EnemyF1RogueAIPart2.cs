using Godot;
using Kuros.Actors.Enemies.Animation;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Environments;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// rogueAI 二阶段本体（Enemy_F1_rogueAI_part2 专属脚本）：在 <see cref="EnemyF1RogueAI"/> 之上把
	/// 生命周期从"进度满收工"改为**部件驱动的血量**——
	///   · 订阅 <see cref="GameActor.DeathFinalized"/>，按组过滤本次战斗的部件（罐子 / 两条机械臂），
	///     每碎一件本体血量 -= <see cref="DamagePerPart"/>；归零走正常战斗死亡流程（Dying/Dead 状态、
	///     BossHUD 解绑、FinalizeDeath）。形态门：up 免伤；血量跌破 <see cref="DownHealthThreshold"/>
	///     先进 DownTransition 过渡（播 up_to_down、锁移动/出招、期间仍免伤）并启用挂载护甲；
	///     过渡结束入 down 后，**护甲未破前本体依旧免伤**，击破护甲才放行直接伤害——
	///     部件联动链三种阶段下都有效（直扣血量、不走免伤门）。
	///   · <see cref="EnemyF1RogueAI"/> 的 progress 收工链被强制关闭：F_begin 的 stage_top 段删除后
	///     进度恒 ≥1（stage_up 已满不清零），保留收工链会出场即退场；且部件清完那一帧会与 HP 归零
	///     双结束链同时开跑。生命周期唯一 owner = 血量。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIPart2 : EnemyF1RogueAI
	{
		[ExportCategory("Part Link 部件联动")]
		/// <summary>部件标记组：本次战斗的罐子/机械臂由生成方打入此组——滑槽机械用
		/// <c>SlideRailMount.CarriageGroupToAdd</c>、过场直生用 <c>EffectConfig.GroupToAdd</c>、
		/// 手工摆放用 tscn 的 groups。部件死亡即扣本体血量。</summary>
		[Export] public string PartGroup { get; set; } = "rogueai_part2_part";
		/// <summary>每碎一件部件扣的血量（血量口径 = 场景 MaxHealth，建议配成部件总数）。</summary>
		[Export(PropertyHint.Range, "0,100,1")] public int DamagePerPart { get; set; } = 1;

		[ExportCategory("Armor 护甲")]
		/// <summary>down 阶段护甲（rogueAI_bumper）：场景里用导出引用指定，可随骨骼挂点重排重新绑定；
		/// 为空 = 视为已破（down 直接可伤）并警告。</summary>
		[Export] public RogueAIBumper? Armor { get; private set; }

		[ExportCategory("Form up/down 形态")]
		/// <summary>down 形态血量阈值：血量 ≤ 本值时**先进入 DownTransition 过渡**（播 up_to_down、
		/// 锁移动/出招、期间仍免伤），过渡结束进入 down（放行直接伤害、动画切 ldle_down）；
		/// **0（默认）= 恒 up**（恒免伤 = 旧行为）。up 形态免疫直接伤害，但部件联动链照常生效。</summary>
		[Export(PropertyHint.Range, "0,10000,1")] public int DownHealthThreshold { get; set; }
		/// <summary>进入 down 形态时 z_index 相对当前值降低的量（N；0 = 不变）——让倒地后的本体压到
		/// 场景层下方。只在 up → down 那一次生效。</summary>
		[Export(PropertyHint.Range, "0,100,1")] public int DownZIndexDecrease { get; set; }

		[ExportCategory("Debug 排查")]
		/// <summary>调试：每按一次 <see cref="DebugDamageKey"/> 对本体直扣的伤害（**绕过免伤门**——便于在 up 形态
		/// 排查 down 转换 / 血条 / 死亡流程）；0 = 关闭。仅调试用。</summary>
		[Export(PropertyHint.Range, "0,1000,1")] public int DebugDamagePerPress { get; set; }
		/// <summary>调试按键。</summary>
		[Export] public Key DebugDamageKey { get; set; } = Key.K;
		/// <summary>调试：下砸攻击运行期间每物理帧打印本体坐标/路径/实例 ID，用于核对冲击波生成点。</summary>
		[Export] public bool DebugLogSlamPosition { get; set; } = false;

		/// <summary>过渡状态节点名（场景 StateMachine 下的常驻子状态）。</summary>
		private const string DownTransitionStateName = "DownTransition";

		private enum FormPhase { Up, Transitioning, Down }
		private FormPhase _formPhase = FormPhase.Up;

		private EnemyF1RogueAISpineAnimationController? _animController;
		private CollisionShape2D? _bodyShape;
		/// <summary>运行期间强制**关闭**本体碰撞的攻击（Overload / OverloadPro / DownSlam / DownSlamPro）：
		/// 这些招式会带动本体位移/下砸，碰撞开着会把玩家顶开或卡住；结束后按形态恢复
		/// （down = 开启、up = 关闭）。见 <see cref="IsBodyCollisionSuppressed"/>。</summary>
		private readonly System.Collections.Generic.List<EnemyAttackTemplate> _collisionSuppressAttacks = new();
		private RogueAIBumper? _armor;
		/// <summary>护甲已破（或场景没配护甲 = 直接放行），down 阶段唯一的可伤条件。</summary>
		private bool _armorBroken;

		public override void _Ready()
		{
			base._Ready();
			// 生命周期由部件血量驱动：关掉进度收工链（stage_top 已删、进度恒满，保留它 = 出场即退场）
			RetreatOnProgressFull = false;
			_animController = GetNodeOrNull<EnemyF1RogueAISpineAnimationController>("AnimationController");

			// 本体碰撞：up 阶段关闭（滑槽上不是实体）；进入 down 时开启（倒地后成为实体障碍）。
			// 例外：Overload / DownSlam 系攻击运行期间一律关闭（招式带本体位移/下砸，会把玩家顶开），见 _PhysicsProcess。
			_bodyShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (_bodyShape != null)
				_bodyShape.Disabled = true;
			CacheCollisionSuppressAttacks();

			// 护甲：场景里通过导出引用绑定（不写死路径）；up 阶段隐藏+不可命中。
			// 场景没配 = 视为已破，避免 down 无法击杀的软锁；同时明确警告。
			_armor = Armor;
			_armorBroken = _armor == null;
			if (_armor == null)
				GD.PushWarning($"[{Name}] 未绑定护甲引用（Armor）：down 阶段将直接可伤");
			_armor?.SetArmorActive(false);

			GameActor.DeathFinalized += OnPartDeathFinalized;
		}

		/// <summary>血量豁免楼层难度缩放：本体的"血量"= 部件计数（每碎一件 -1），必须与部件数严格相等——
		/// 被放大（如 stage_6 Floor 4 → ×1.48 → 场景值 4 变 6）后部件全碎也清不了血条，本体不死。
		/// 对外伤害照常按难度缩放（它仍是真刀真枪与玩家对打的敌人）。
		/// 在 <c>base._Ready()</c> 抄写 BaseMaxHealth/CurrentHealth 之前回滚，见 SampleEnemy.ApplyStageScaling 时序。</summary>
		protected override void ApplyStageScaling()
		{
			int rawMaxHealth = MaxHealth;
			base.ApplyStageScaling();   // 照常写入对外伤害系数（顺带把血量乘了系数）
			MaxHealth = rawMaxHealth;   // 血量回滚为场景原始值（部件计数口径）
		}

		public override void _ExitTree()
		{
			GameActor.DeathFinalized -= OnPartDeathFinalized;
			base._ExitTree();
		}

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			if (DebugLogSlamPosition)
				LogSlamPosition();

			// 本体碰撞守恒：攻击中（Overload/OverloadPro/DownSlam/DownSlamPro）一律关闭；
			// 其余按形态——down 开启（倒地成为实体障碍）、up 关闭。每帧比对目标态而不是单向置位：
			// 攻击结束/被打断后自然回到形态对应的状态（幂等，deferred 生效）。
			if (_bodyShape != null && GodotObject.IsInstanceValid(_bodyShape))
			{
				bool wantDisabled = _formPhase != FormPhase.Down || IsBodyCollisionSuppressed();
				if (_bodyShape.Disabled != wantDisabled)
					_bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, wantDisabled);
			}

			// up → down 单向：跌破阈值先进过渡状态（up_to_down + 锁移动/出招），
			// 过渡结束由状态回调 EnterDownForm；期间仍免伤，部件联动链照常生效。
			if (_formPhase != FormPhase.Up || IsDeadOrDying) return;
			if (DownHealthThreshold <= 0 || CurrentHealth > DownHealthThreshold) return;

			_formPhase = FormPhase.Transitioning;
			// up_to_down 触发即启用护甲：过渡 0.8s 里就能开始拆甲（本体此时仍免伤）
			if (!_armorBroken && _armor != null && GodotObject.IsInstanceValid(_armor))
				_armor.SetArmorActive(true);

			var machine = StateMachine;
			if (machine == null || !machine.HasState(DownTransitionStateName)
				|| !machine.ChangeState(DownTransitionStateName))
			{
				// 没有过渡状态（单测）/ 被当前状态拒绝：直接落 down，避免永久免疫卡死
				EnterDownForm();
			}
		}

		/// <summary>缓存"运行中关闭本体碰撞"的攻击模板（控制器导出的攻击名下查找，不写死节点名）。</summary>
		private void CacheCollisionSuppressAttacks()
		{
			var controller = StateMachine?.GetNodeOrNull<EnemyF1RogueAIAttackController>("Attack/AttackController");
			if (controller == null) return;

			AddCollisionSuppressAttack(controller, controller.UltimateAttackName);
			AddCollisionSuppressAttack(controller, controller.UltimateProAttackName);
			if (controller is EnemyF1RogueAIPart2AttackController part2Controller)
			{
				AddCollisionSuppressAttack(controller, part2Controller.ProximityAttackName);
				AddCollisionSuppressAttack(controller, part2Controller.ProximityProAttackName);
			}
		}

		private void AddCollisionSuppressAttack(Node owner, string attackName)
		{
			if (string.IsNullOrEmpty(attackName)) return;
			var attack = owner.GetNodeOrNull<EnemyAttackTemplate>(attackName);
			if (attack != null && !_collisionSuppressAttacks.Contains(attack))
				_collisionSuppressAttacks.Add(attack);
		}

		/// <summary>上述攻击是否正在运行（Warmup / Active / Recovery 任一段）——运行中本体碰撞一律关闭。</summary>
		private bool IsBodyCollisionSuppressed()
		{
			foreach (var attack in _collisionSuppressAttacks)
			{
				if (GodotObject.IsInstanceValid(attack) && attack.IsRunning) return true;
			}
			return false;
		}

		/// <summary>调试：近身下砸在跑时打印本体根节点坐标/路径/ID（与 Shockwave 的 attacker 对照）。</summary>
		private void LogSlamPosition()
		{
			var controller = StateMachine?.GetNodeOrNull<EnemyAttackController>("Attack/AttackController");
			if (controller == null) return;

			foreach (var child in controller.GetChildren())
			{
				if (child is EnemyF1RogueAIDownSlamAttack { IsRunning: true } slam)
				{
					GD.Print($"[Part2Pos] attack={slam.Name} pos={GlobalPosition}"
						+ $" path={GetPath()} id={GetInstanceId()}"
						+ $" warm={slam.IsWarmup} active={slam.IsActive}"
						+ $" t_ms={Time.GetTicksMsec()}");
				}
			}
		}

		/// <summary>进入 down 形态（过渡结束时由 DownTransition 状态回调；无过渡状态时直接兜底）。幂等。</summary>
		public void EnterDownForm()
		{
			if (_formPhase == FormPhase.Down) return;
			_formPhase = FormPhase.Down;
			if (_animController != null && GodotObject.IsInstanceValid(_animController))
				_animController.DownForm = true;
			if (DownZIndexDecrease != 0)
				ZIndex -= DownZIndexDecrease;

			// 倒地后开启本体碰撞（物理回调中改形状状态需要 deferred）
			if (_bodyShape != null && GodotObject.IsInstanceValid(_bodyShape))
				_bodyShape.SetDeferred(CollisionShape2D.PropertyName.Disabled, false);
		}

		/// <summary>只有 down 形态**且护甲已破**才放行直接伤害（仍不吃 ActorEffect）；up / 过渡 /
		/// 护甲未破期间一律免疫。部件联动链（<see cref="OnPartDeathFinalized"/> 直扣血量）不走这道门。</summary>
		public override bool CanBeAffected(ActorEffect? effect)
			=> effect == null && _formPhase == FormPhase.Down && _armorBroken;

		/// <summary>调试排查键：按一次直扣 <see cref="DebugDamagePerPress"/>（绕过免伤门；忽略长按重复）。</summary>
		public override void _UnhandledKeyInput(InputEvent @event)
		{
			if (DebugDamagePerPress <= 0) return;
			if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
			if (key.Keycode != DebugDamageKey) return;
			if (IsDeadOrDying) return;

			CurrentHealth = Mathf.Max(0, CurrentHealth - DebugDamagePerPress);
			NotifyHealthChanged();
			GD.Print($"[Debug] {Name} 排查伤害 -{DebugDamagePerPress} → HP {CurrentHealth}/{MaxHealth}");
			if (CurrentHealth <= 0)
				Die();
		}

		/// <summary>部件阵亡 → 本体掉血（免伤核心的专用通道：直扣血量、不走 TakeDamage）。</summary>
		private void OnPartDeathFinalized(GameActor actor)
		{
			if (IsDeadOrDying || actor == null || !IsInstanceValid(actor)) return;

			// 护甲击破：打开本体伤害门。必须先于部件组过滤——护甲不是部件，绝不能走下面的扣血链。
			if (_armor != null && IsInstanceValid(_armor) && actor == _armor)
			{
				_armorBroken = true;
				return;
			}

			if (string.IsNullOrEmpty(PartGroup) || !actor.IsInGroup(PartGroup)) return;
			if (DamagePerPart <= 0) return;

			CurrentHealth = Mathf.Max(0, CurrentHealth - DamagePerPart);
			NotifyHealthChanged();
			if (CurrentHealth <= 0)
				Die();
		}
	}
}
