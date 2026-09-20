using Godot;
using Kuros.Environments;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// Enemy_F1_rogueAI 的攻击控制器：疲劳权重（基类）+ **关卡击杀进度阈值**强制大招。
	/// 触发源不是自身血量（本体常态免伤、血量恒定，血量阈值那套在这里永远不会跨过），而是
	/// <see cref="KillProgressAscendController"/>：进度每跨过一个阈值就 +1 次"大招配额"。
	///   · 配额记在 _pendingUltimates 队列里；队列非空 → 大招权重拉满、普攻清零（_pendingWeightRestore 记账）；
	///   · 大招**真正起手**才出队（避免被别的攻击抢走配额）；
	///   · Warmup/Active 被打断 = 没放出来 → 退还配额（Recovery 打断视为已释放，不退）；
	///   · Initialize 时按 EarnedUltimateCount 追赶——本体由过场投放，早于它生成时跨过的阈值不丢。
	/// 注意：模板**节点名必须等于 AttackName**（TrySetAttackWeight/IsAttack 都按节点名比较），
	/// 且两招用全局唯一名（IsOtherEnemyAttacking 按攻击名在全体敌人间去重）。
	/// </summary>
	public partial class EnemyF1RogueAIAttackController : EnemyFatigueAttackControllerBase
	{
		/// <summary>大招攻击名。**必须与节点名一致，且全项目唯一**——
		/// EnemyAttackController.IsOtherEnemyAttacking 按攻击名在全体敌人间去重，
		/// 用通用名（如 netAdmin 已占用的 "UltimateAttack"）会和那台敌人互相阻断出招。</summary>
		[Export] public string UltimateAttackName { get; set; } = "RogueAIOverload";
		/// <summary>两次大招之间的最小间隔（秒）：到点后若队列还有配额就重新武装。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float UltimateRetriggerDelay { get; set; } = 4f;
		/// <summary>待放大招队列上限（进度一次跨多个阈值时防止连发）。</summary>
		[Export(PropertyHint.Range, "1,10,1")] public int MaxQueuedUltimates { get; set; } = 2;
		[Export] public bool EnableDebugLogs { get; set; } = false;

		private KillProgressAscendController? _progress;
		private int _pendingUltimates;
		private bool _pendingWeightRestore;
		private bool _ultimateInFlight;
		private bool _boundOnce;
		private bool _missingWarned;
		private float _ultRetriggerTimer;

		/// <summary>调试：已获得但还没释放的大招次数。</summary>
		public int PendingUltimates => _pendingUltimates;

		public override void Initialize(SampleEnemy enemy)
		{
			base.Initialize(enemy);   // 基类按 metadata/attack_weight 缓存原始权重

			_pendingUltimates = 0;
			_pendingWeightRestore = false;
			_ultimateInFlight = false;
			_ultRetriggerTimer = 0f;
			_boundOnce = false;
			_missingWarned = false;

			BindProgress();
			ConfigureNextAttack();
		}

		public override void _ExitTree()
		{
			UnbindProgress();
			base._ExitTree();
		}

		public override void _PhysicsProcess(double delta)
		{
			if (_progress == null || !GodotObject.IsInstanceValid(_progress))
				BindProgress();   // 关卡晚于本控制器初始化时也能补上

			if (_ultRetriggerTimer > 0f)
			{
				_ultRetriggerTimer -= (float)delta;
				if (_ultRetriggerTimer <= 0f)
					ConfigureNextAttack();   // 间隔到点：队列还有配额就重新武装
			}

			base._PhysicsProcess(delta);
		}

		protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
		{
			base.OnChildAttackStarted(attack);   // 疲劳逻辑（连续同招降权 / 换招还原）

			if (IsAttack(attack.Name, UltimateAttackName))
			{
				_ultimateInFlight = true;
				ConsumeUltimate();
				_ultRetriggerTimer = UltimateRetriggerDelay;
			}
			ConfigureNextAttack();
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			_ultimateInFlight = false;
			ConfigureNextAttack();   // 攻击期间可能又跨了阈值
		}

		/// <summary>子攻击在 Warmup/Active 被打断时基类会回调这里（Recovery 打断不会走到）：
		/// 大招没放出来 → 退还配额并允许立刻重放。用 _ultimateInFlight 兜底，避免"未起手就取消"也被计入。</summary>
		protected override void OnAttackInterrupted(EnemyAttackTemplate attack)
		{
			base.OnAttackInterrupted(attack);

			if (IsAttack(attack.Name, UltimateAttackName) && _ultimateInFlight)
			{
				_ultimateInFlight = false;
				_pendingUltimates = Mathf.Min(_pendingUltimates + 1, MaxQueuedUltimates);
				_pendingWeightRestore = false;
				_ultRetriggerTimer = 0f;
				Log($"大招被打断 → 退还配额（pending={_pendingUltimates}）");
			}
			ConfigureNextAttack();
		}

		/// <summary>大招期间玩家跑远也不中断（配合全场覆盖的检测区半径）。</summary>
		protected override bool ShouldInterruptOnPlayerExit() => false;

		// ── 内部 ──────────────────────────────────────────────────────────

		private void BindProgress()
		{
			if (_progress != null && GodotObject.IsInstanceValid(_progress)) return;

			_progress = KillProgressAscendController.Find(this);
			if (_progress == null)
			{
				if (!_missingWarned)
				{
					_missingWarned = true;
					GD.PushWarning($"{Enemy?.Name ?? Name}: 未找到 KillProgressAscendController（组 kill_progress），"
						+ "大招阈值不会触发（单独测试本体时属正常）");
				}
				return;
			}

			_progress.UltimateThresholdReached += OnThresholdReached;

			if (!_boundOnce)
			{
				_boundOnce = true;
				// 追赶：本体由过场投放，生成前已跨过的阈值照单全收
				_pendingUltimates = Mathf.Clamp(_progress.EarnedUltimateCount, 0, MaxQueuedUltimates);
				Log($"绑定进度控制器：已获得 {_pendingUltimates} 次大招配额");
			}
			ConfigureNextAttack();
		}

		private void UnbindProgress()
		{
			if (_progress != null && GodotObject.IsInstanceValid(_progress))
				_progress.UltimateThresholdReached -= OnThresholdReached;
			_progress = null;
		}

		private void OnThresholdReached(int thresholdIndex, float thresholdValue)
		{
			_pendingUltimates = Mathf.Min(_pendingUltimates + 1, MaxQueuedUltimates);
			Log($"进度阈值 {thresholdValue:P0} 达成 → 排队大招（pending={_pendingUltimates}）");
			ConfigureNextAttack();   // 立刻武装，不等下一招起手
		}

		private void ConsumeUltimate()
		{
			if (_pendingUltimates > 0) _pendingUltimates--;
			Log($"大招起手（剩余配额 {_pendingUltimates}）");
		}

		private void ConfigureNextAttack()
		{
			bool armed = _pendingUltimates > 0 && _ultRetriggerTimer <= 0f;

			if (armed)
			{
				TrySetAttackWeight(UltimateAttackName, 1f);
				TrySetAttackWeight(MeleeAttackName, 0f);
				_pendingWeightRestore = true;

				// 关键：队列里可能已经排着"当前不可启动"的普攻（EnemySimpleMeleeAttack.CanStart 要求玩家
				// 站在它的判定列内）——排队攻击没进冷却时空闲循环不会重新选招，配额会被永久卡住。
				// 所以武装瞬间把陈旧队列踢掉，让空闲循环按新权重（只有大招有权重）重新选；代价是一次呼吸窗。
				if (!IsRunning && !string.IsNullOrEmpty(QueuedAttackName)
					&& !IsAttack(QueuedAttackName, UltimateAttackName))
				{
					Log($"清掉陈旧队列（{QueuedAttackName}）→ 让大招上位");
					ForceQueueNextAttack("UltimateArmed");
				}
				return;
			}

			TrySetAttackWeight(UltimateAttackName, 0f);

			// 大招起手后（配额已消费）：一次性还原被清零的普攻权重，之后交回疲劳逻辑
			if (_pendingWeightRestore)
			{
				RestoreAttackWeight(MeleeAttackName);
				_pendingWeightRestore = false;
			}
		}

		private static bool IsAttack(string attackName, string expectedName)
			=> attackName.Equals(expectedName, System.StringComparison.OrdinalIgnoreCase);

		private void Log(string message)
		{
			if (EnableDebugLogs) GD.Print($"[{Enemy?.Name ?? Name}] {message}");
		}
	}
}
