using Godot;
using System;
using Kuros.Systems.FSM;
using Kuros.Systems.Loot;
using Kuros.Core.Effects;
using Kuros.Utils;
using Kuros.Core.Stats;
using Kuros.Core.Events;

namespace Kuros.Core
{
	[GlobalClass]
	public partial class GameActor : CharacterBody2D
	{
		public event Action<int, int>? HealthChanged;
		/// <summary>
		/// 实际受到伤害后触发（已扣血）。参数为实际伤害值。
		/// </summary>
		public event Action<int>? DamageTaken;
		/// <summary>
		/// 完整伤害信息（实例级、无条件广播）：伤害值 + 伤害类型 + 攻击者（可为 null）。
		/// 供按目标订阅的伤害检测系统使用（如受伤打断的伤害类型过滤）——无 attacker 门控，环境伤害同样触发。
		/// </summary>
		public event Action<int, Events.DamageSource, GameActor?>? DamageTakenDetailed;
		/// <summary>
		/// 任意 GameActor 受到伤害时触发的全局静态事件。
		/// 参数：victim（受击方）, attacker（攻击方，可为 null）, damage（实际伤害）
		/// </summary>
		public static event Action<GameActor, GameActor?, int>? AnyDamageTaken;
		/// <summary>
		/// 任意 GameActor 死亡流程彻底结束后触发的全局静态事件（FinalizeDeath 已执行）。
		/// 参数为死亡者自身；触发时 actor 尚未释放（QueueFree 为延迟调用），订阅者可安全读取类型/描述。
		/// </summary>
		public static event Action<GameActor>? DeathFinalized;

		[ExportCategory("Stats")]
		[Export] public float Speed = 100.0f;
		[Export] public float AttackDamage = 1.0f;
		/// <summary>受到的伤害倍率（1 = 正常）。由外部效果设置。</summary>
		public float IncomingDamageMultiplier { get; set; } = 1f;
		/// <summary>全局攻速倍率（1 = 正常，2 = 双倍攻速）。由外部效果设置。</summary>
		public float AttackSpeedMultiplier { get; set; } = 1f;
		// [Export] public float AttackRange = 100.0f; // Removed: Deprecated, rely on AttackArea logic
		[Export] public float AttackCooldown = 0f;
		[Export] public int MaxHealth = 15;

		/// <summary>
		/// 基础最大血量（_Ready 时记录，不含 build 效果加成）——供 MaxHealth 类效果（如 NormalMaxHealthBoostEffect）
		/// 做叠加基数：效果只能基于基础值计算加成，场景切换/玩家重建时不会因快照恢复的"含加成值"而重复叠加。
		/// </summary>
		public int BaseMaxHealth { get; protected set; }
		/// <summary>AI 可读描述（供 GameStateProvider 快照喂给 LLM——敌人类型/特点说明）。
		/// 各角色 .tscn 根节点 Inspector 配置；经 characters.csv 导出/导入维护。</summary>
		[Export(PropertyHint.MultilineText)] public string AiDescription { get; set; } = string.Empty;
		[Export] public bool FaceLeftByDefault = false;
		/// <summary>
		/// 初始朝向。true=朝右，false=朝左。在 _Ready 时应用，不影响行为逻辑。
		/// </summary>
		[Export] public bool InitialFacingRight = true;
		
		
		[ExportCategory("Components")]
		[Export] public StateMachine StateMachine { get; private set; } = null!;
		[Export] public EffectController EffectController { get; private set; } = null!;
		[Export] public CharacterStatProfile? StatProfile { get; private set; }

		[ExportCategory("Loot")]
		[Export] public LootDropTable? LootTable { get; set; }

		[ExportCategory("VFX/Damage Flash")]
		[Export] public bool EnableDamageFlash { get; set; } = true;
		[Export] public bool FlashSpineVisual { get; set; } = true;
		[Export] public bool FlashSpriteVisual { get; set; } = true;
		[Export] public Color DamageFlashColor { get; set; } = new Color(1f, 0f, 0f, 1f);
		[Export(PropertyHint.Range, "0,1,0.01,or_greater")]
		public float DamageFlashDuration { get; set; } = 0.1f;

		[ExportCategory("Damage Merge")]
		/// <summary>伤害合并窗口（秒）：窗口内多段伤害累计一次结算（扣血/日志/闪白/Hit 状态/事件只执行一次），
		/// 避免极短时间内大量多段伤害逐段放大主线程开销（日志、Spine 闪白、受击动画、事件广播）。0 = 禁用合并。</summary>
		[Export(PropertyHint.Range, "0,0.5,0.01")] public float DamageMergeWindow { get; set; } = 0.1f;

		// Exposed state for States to use
		public int CurrentHealth { get; protected set; }
		public int CurrentShield { get; private set; }
		//public float FrozenStateRemainingTime { get; set; } = 0.0f;
		public float AttackTimer { get; set; } = 0.0f;
		public bool FacingRight { get; protected set; } = true;
		public event Func<DamageEventArgs, bool>? DamageIntercepted;
		public AnimationPlayer? AnimPlayer => _animationPlayer;
		
		/// <summary>
		/// 保存Frozen状态被打断时的剩余时长，用于在Hit后恢复
		/// </summary>
		public float FrozenStateRemainingTime { get; set; } = 0f;
		
		protected Node2D _spineCharacter = null!;
		protected Sprite2D _sprite = null!;
		protected AnimationPlayer _animationPlayer = null!;
		private Color _spineDefaultModulate = Colors.White;
		private Color _spriteDefaultModulate = Colors.White;
		private ulong _spineFlashToken;
		private ulong _spriteFlashToken;
		
		// GDScript Helper to bypass C# wrapper issues with GDExtension
		private float _currentMoveSpeed;
		private Node _spineHelper = null!;

		private bool _deathStarted = false;
		private bool _deathFinalized = false;
		private Area2D? _cachedHitArea;
		private bool _hitAreaResolved;
		private ulong _lastDamageTakenAtMs = 0;

		// 伤害合并窗口状态：窗口内多段伤害累计，到期统一结算
		private int _pendingDamageTotal = 0;
		private GameActor? _pendingAttacker;
		private Vector2? _pendingOrigin;
		private Events.DamageSource _pendingSource;
		private float _damageMergeTimer = 0f;
		private bool _hasPendingDamage = false;

		public bool IsDeathSequenceActive => _deathStarted && !_deathFinalized;
		public bool IsDead => _deathFinalized;
		/// <summary>死亡流程已开始（Dying 或 Dead）：伤害/治疗/特效作用应在此时立即停止，而非等死亡动画结束。</summary>
		public bool IsDeadOrDying => _deathStarted;
		public bool IgnoreHitStateOnDamage { get; set; } = false;
		/// <summary>额外速度加成百分比（加法叠加，供超频/动能增幅等共享——各效果增量写入，移动状态统一消费）。</summary>
		public float SpeedBonusPercent { get; set; } = 0f;
		/// <summary>当前实际移动速度（由移动状态写入：Run/Walk/Dash 写各自速度，Idle 写 0）——供攻击模板查询做"移动速度驱动的位移"，与其他状态最小耦合。</summary>
		public float CurrentMoveSpeed { get => _currentMoveSpeed; set => _currentMoveSpeed = value; }

		/// <summary>冲刺动量快照：最近一次冲刺 Burst 段的峰值速度（Dash 状态进入时写入，冲刺攻击起步继承用）。</summary>
		public float LastDashBurstSpeed { get; set; }
		/// <summary>最近一次冲刺帧的时间戳（毫秒，Dash 状态每帧刷新）：冲刺攻击宽限窗口判定用。</summary>
		public ulong LastDashFrameMs { get; set; }
		/// <summary>当前移动方向（归一化，由移动状态写入；静止为 Zero）——供攻击模板继承含 Y 轴的移动方向。</summary>
		public Vector2 CurrentMoveDirection { get; set; } = Vector2.Zero;
		/// <summary>后撤闪避免费窗口判定（B_003 等效果注入：前向闪避后窗口内 backdash 不消耗充能/热量）。</summary>
		public Func<bool>? IsDashBackWindowActive { get; set; }
		/// <summary>是否允许在 holding 状态持投掷武器（IsThrowable &amp;&amp; IsThrowWeapon）时闪避——由构筑效果注入。null = 不允许。</summary>
		public Func<bool>? IsDashFromThrowWeaponHoldingAllowed { get; set; }
		/// <summary>临时验证开关：允许持投掷武器（IsThrowWeapon）时闪避（构建效果卡完成后由效果注入替代）。</summary>
		[Export] public bool AllowDashFromThrowWeaponHolding = false;
		/// <summary>是否允许在 holding 状态持投掷家具（IsThrowable &amp;&amp; !IsThrowWeapon，一次性）时闪避——由构筑效果注入。null = 不允许。</summary>
		public Func<bool>? IsDashFromThrowFurnitureHoldingAllowed { get; set; }
		/// <summary>小伤害不打断：受到的伤害 ≤ MaxHealth × SmallDamageHitThresholdRatio 时不进入 Hit 状态（神经稳压）。</summary>
		public bool SuppressSmallDamageHit { get; set; } = false;
		/// <summary>小伤害判定阈值（最大生命比例，0.1 = 10%）。</summary>
		[Export(PropertyHint.Range, "0,0.5,0.01")] public float SmallDamageHitThresholdRatio { get; set; } = 0.1f;
		/// <summary>
		/// 当前角色持有的免疫标志集合，由 EnemyAttackTemplate 的 GrantedImmunities 字段在攻击期间写入/还原。
		/// 新增免疫类型只需在 <see cref="ImmunityFlags"/> 枚举中追加值，无需修改此类。
		/// </summary>
		public ImmunityFlags ActiveImmunities { get; set; } = ImmunityFlags.None;

		/// <summary>
		/// 统一击退入口（EFFECT_STANDARD.md 第四条）：ForcedMovement 免疫与生死门检查后写入速度。
		/// 所有命中击退必须走此方法，禁止直接赋值 Velocity。
		/// </summary>
		public virtual void ApplyKnockback(Vector2 direction, float speed)
		{
			if (IsDeadOrDying) return;
			if (ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement)) return;
			Velocity = direction * speed;
		}

		public float GetSecondsSinceLastDamageTaken()
		{
			if (_lastDamageTakenAtMs == 0)
			{
				return float.PositiveInfinity;
			}

			ulong now = Time.GetTicksMsec();
			return (now - _lastDamageTakenAtMs) / 1000f;
		}

		public override void _Ready()
		{
			// 记录基础最大血量（不含 build 效果加成）——供 MaxHealth 类效果做叠加基数
			BaseMaxHealth = MaxHealth;
			CurrentHealth = MaxHealth;
			CurrentShield = 0;

			// 临时验证：导出开关驱动持物闪避委托（build 效果卡完成后由效果注入替代）
			IsDashFromThrowWeaponHoldingAllowed = () => AllowDashFromThrowWeaponHolding;
			
			
			
			// Load Spine helper script
			var spineScript = GD.Load<GDScript>("res://scripts/utils/SpineWrapper.gd");
			if (spineScript != null)
			{
				_spineHelper = (Node)spineScript.New();
				AddChild(_spineHelper);
				
			
			}
			
			// Node fetching - DO NOT attempt to cast SpineSprite to Node2D or GodotObject directly
			// checking for existence is fine
			bool hasSpine = HasNode("SpineCharacter") || HasNode("SpineSprite");
			
			// Try to fetch if it's a Node2D wrapper (rare case if GDExtension is missing bindings)
			if (HasNode("SpineCharacter"))
			{
				var variant = Call("get_node", "SpineCharacter");
				if (variant.VariantType == Variant.Type.Object)
				{
					try 
					{ 
						// Only assign if it successfully casts, otherwise leave null
						// Catching generic exception to silence potential wrapper errors if possible
						var obj = variant.As<GodotObject>();
						if (obj is Node2D n2d) _spineCharacter = n2d;
					} 
					catch { }
				}
			}
			
			if (_spineCharacter == null && HasNode("SpineSprite"))
			{
				 var variant = Call("get_node", "SpineSprite");
				 try 
				 { 
					 var obj = variant.As<GodotObject>();
					 if (obj is Node2D n2d) _spineCharacter = n2d;
				 } 
				 catch { }
			}

			if (_spineCharacter != null)
			{
				_spineDefaultModulate = _spineCharacter.Modulate;
			}
			
			_sprite = GetNodeOrNull<Sprite2D>("Sprite2D");
			if (_sprite != null)
			{
				_spriteDefaultModulate = _sprite.Modulate;
			}
			
			if (_spineCharacter != null)
			{
				_animationPlayer = _spineCharacter.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
			}
			
			// Initialize StateMachine if manually assigned or found
			if (StateMachine == null)
			{
				StateMachine = GetNodeOrNull<StateMachine>("StateMachine");
			}

			if (StateMachine != null)
			{
				StateMachine.Initialize(this);
			}

			EffectController ??= GetNodeOrNull<EffectController>("EffectController");
			if (EffectController == null)
			{
				EffectController = new EffectController
				{
					Name = "EffectController"
				};
				AddChild(EffectController);
			}

			ApplyStatProfile();
			NotifyHealthChanged();
			
			// 应用初始朝向（必须在所有子节点初始化之后）
			if (FacingRight != InitialFacingRight)
			{
				FlipFacing(InitialFacingRight);
			}
		}

		public void SetShieldValue(int shield)
		{
			CurrentShield = Mathf.Max(0, shield);
		}

		public void AddShield(int shield)
		{
			if (shield <= 0)
			{
				return;
			}

			CurrentShield = Mathf.Max(0, CurrentShield + shield);
		}

		public void ClearShield()
		{
			CurrentShield = 0;
		}

		// ====================== 新增4：递归同步所有子渲染节点Z层级的工具方法 ======================
		private void ForceChildRenderNodesZIndex(Node parentNode, int targetZIndex)
		{
			foreach (Node child in parentNode.GetChildren())
			{
				// 只处理渲染节点（Sprite2D、SpineSprite、ColorRect等）
				if (child is CanvasItem renderNode)
				{
					renderNode.ZIndex = targetZIndex;
				}
				// 递归处理子节点的子节点（确保所有层级都覆盖）
				ForceChildRenderNodesZIndex(child, targetZIndex);
			}
		}
		// ==========================================================================================

		public override void _PhysicsProcess(double delta)
		{
			if (AttackTimer > 0) AttackTimer -= (float)delta;

			// 伤害合并窗口到期：统一结算累计伤害（每帧递减计时器，窗口结束只结算一次；
			// 首段即时结算也会启动窗口计时，故按计时器而非 _hasPendingDamage 判断）
			if (_damageMergeTimer > 0f)
			{
				_damageMergeTimer -= (float)delta;
				if (_damageMergeTimer <= 0f)
				{
					FlushPendingDamage();
				}
			}

			// FSM handles logic, but we can keep global helpers here
			// If using FSM, ensure it is processed either here or by itself (Node process)
			// StateMachine._PhysicsProcess is called automatically by Godot if it's in the tree
		}

		/// <summary>
		/// 目标的视觉锚点（世界坐标）：优先 VisualEffectArea（模拟视觉身高的锚点——高个子敌人如 b1_fat
		/// 在 Sprite2D/VisualEffectArea 下配 CollisionShape2D 标记视觉中心），回退 HitArea 中心，再回退目标原点。
		/// 供生成在目标身上的视觉使用（dot 特效/死亡特效等）——避免高个子敌人特效出现在脚底。
		/// </summary>
		public Vector2 GetVisualAnchorWorld()
		{
			var visualArea = GetNodeOrNull<Area2D>("VisualEffectArea")
				?? GetNodeOrNull<Area2D>("Sprite2D/VisualEffectArea")
				?? FindChild("VisualEffectArea", recursive: true, owned: false) as Area2D;
			var visualShape = visualArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (visualShape != null)
				return visualShape.GlobalPosition;

			var hitArea = ResolvePreferredHitArea();
			var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			if (hitShape != null)
				return hitShape.GlobalPosition;

			return GlobalPosition;
		}

		protected virtual Area2D? ResolvePreferredHitArea()
		{
			if (_cachedHitArea != null && GodotObject.IsInstanceValid(_cachedHitArea) && _cachedHitArea.IsInsideTree())
			{
				return _cachedHitArea;
			}

			if (_hitAreaResolved)
			{
				return null;
			}

			_cachedHitArea = GetNodeOrNull<Area2D>("HitArea")
				?? GetNodeOrNull<Area2D>("Sprite2D/HitArea")
				?? FindChild("HitArea", recursive: true, owned: false) as Area2D;

			_hitAreaResolved = true;
			return _cachedHitArea;
		}

		public virtual bool IsHitByArea(Area2D? attackerArea)
		{
			if (attackerArea == null || !attackerArea.IsInsideTree())
			{
				return false;
			}

			var hitArea = ResolvePreferredHitArea();
			if (hitArea != null && GodotObject.IsInstanceValid(hitArea) && hitArea.IsInsideTree())
			{
				return attackerArea.OverlapsArea(hitArea);
			}

			return attackerArea.OverlapsBody(this);
		}

		/// <summary>
		/// 受到伤害。返回 true 表示伤害被接受（本次调用会导致扣血，无论立即结算还是进入合并窗口稍后结算）；
		/// false 表示被免疫/拦截/无效（不会扣血）。调用方可用返回值判断击退等副作用是否应生效。
		/// bypassMergeWindow = true 时跳过伤害合并窗口立即结算（用于暴击追加等与基础伤害同源、
		/// 需要同帧结算以便飘字合并显示的伤害）。
		/// </summary>
		/// <summary>
		/// 环境强制击杀（测试控制台清场用）：优先走正常伤害死亡流程（伤害飘字 + 死亡动画）。
		/// 伤害被免疫拒绝时回退直接销毁。免疫解除策略由子类重写（如 netAdmin 需先眩晕）。
		/// </summary>
		public virtual void KillForced()
		{
			if (!DeliverKillDamage() && GodotObject.IsInstanceValid(this))
				QueueFree();
		}

		/// <summary>尝试用巨额伤害击杀；被免疫/拦截返回 false。</summary>
		protected bool DeliverKillDamage()
			=> TakeDamage(9999, GlobalPosition, null);

		public virtual bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null, Events.DamageSource damageSource = Events.DamageSource.DirectAttack, bool bypassMergeWindow = false)
		{
			if (!CanBeAffected(null)) return false;
			if (IsDeathSequenceActive || IsDead) return false;

			if (ActiveImmunities.HasFlag(ImmunityFlags.ThrowableDamage)
				&& (damageSource == Events.DamageSource.ThrowableDirectAttack || damageSource == Events.DamageSource.ThrowImpact))
				return false;
			if (ActiveImmunities.HasFlag(ImmunityFlags.NonThrowableDamage)
				&& damageSource != Events.DamageSource.ThrowableDirectAttack
				&& damageSource != Events.DamageSource.ThrowImpact)
				return false;
			if (damage <= 0) return false;

			if (IncomingDamageMultiplier != 1f)
				damage = Mathf.Max(1, Mathf.RoundToInt(damage * IncomingDamageMultiplier));

			if (DamageIntercepted != null)
			{
				var args = new DamageEventArgs(this, damage, attackOrigin);
				foreach (Func<DamageEventArgs, bool> handler in DamageIntercepted.GetInvocationList())
				{
					handler(args);
					if (args.IsBlocked)
					{
						GameLogger.Info(nameof(GameActor), $"{Name} blocked incoming damage.");
						return false;
					}
				}

				damage = args.Damage;
				if (damage <= 0)
				{
					return false;
				}
			}

			// 保持实时更新"最近受击时刻"，AI 的受击判断（GetSecondsSinceLastDamageTaken）不因合并延迟而失真
			_lastDamageTakenAtMs = Time.GetTicksMsec();

			// 伤害合并窗口：窗口内多段伤害累计，到期统一结算（扣血/通知/日志/闪白/Hit 状态/事件只执行一次）。
			// 首段伤害立即结算（即时反馈，飘字/击退/受击动画零延迟），同时启动窗口，
			// 窗口内到达的后续段合并到窗口到期统一结算（性能热点仍被压住）。
			if (DamageMergeWindow > 0f && !bypassMergeWindow)
			{
				if (_damageMergeTimer <= 0f)
				{
					// 首段（窗口未激活）：立即结算并启动合并窗口
					_damageMergeTimer = DamageMergeWindow;
					ApplyPendingDamage(damage, attackOrigin, attacker, damageSource);
					return true;
				}

				// 后续段：进入合并窗口，到期统一结算（避免多段伤害逐段放大副作用开销）
				_pendingDamageTotal += damage;
				_pendingAttacker ??= attacker;
				_pendingOrigin ??= attackOrigin;
				_pendingSource = damageSource;
				_hasPendingDamage = true;

				// 累计已达致死量 → 立即结算，避免死亡反馈延迟
				if (CurrentHealth - _pendingDamageTotal <= 0)
				{
					FlushPendingDamage();
				}
				return true;
			}

			ApplyPendingDamage(damage, attackOrigin, attacker, damageSource);
			return true;
		}

		/// <summary>合并窗口到期（或致死预检）时统一结算累计伤害：只走一次扣血与全部副作用。</summary>
		private void FlushPendingDamage()
		{
			if (!_hasPendingDamage) return;

			int total = _pendingDamageTotal;
			var attacker = _pendingAttacker;
			var origin = _pendingOrigin;
			var source = _pendingSource;

			_hasPendingDamage = false;
			_pendingDamageTotal = 0;
			_pendingAttacker = null;
			_pendingOrigin = null;
			_damageMergeTimer = 0f;

			ApplyPendingDamage(total, origin, attacker, source);
		}

		/// <summary>实际扣血与全部副作用（原 TakeDamage 扣血后的部分，合并窗口内仅执行一次）。</summary>
		private void ApplyPendingDamage(int damage, Vector2? attackOrigin, GameActor? attacker, Events.DamageSource damageSource)
		{
			CurrentHealth -= damage;
			CurrentHealth = Mathf.Max(CurrentHealth, 0);
			NotifyHealthChanged();
			DamageTaken?.Invoke(damage);
			DamageTakenDetailed?.Invoke(damage, damageSource, attacker);
			AnyDamageTaken?.Invoke(this, attacker, damage);

			//GameLogger.Info(nameof(GameActor), $"{Name} took {damage} damage! Health: {CurrentHealth}");

			FlashDamageEffect();

			if (CurrentHealth <= 0)
			{
				Die();
			}
			else
			{
				// Force state change to Hit unless this actor is in super-armor phase.
				bool smallHitSuppressed = SuppressSmallDamageHit
					&& damage <= Mathf.RoundToInt(MaxHealth * SmallDamageHitThresholdRatio);
				if (!IgnoreHitStateOnDamage && !smallHitSuppressed && StateMachine != null)
				{
					if (StateMachine.CurrentState?.Name == "Hit")
					{
						StateMachine.ReenterState("Hit");
					}
					else
					{
						StateMachine.ChangeState("Hit");
					}
				}
			}

			if (attacker != null)
			{
				Events.DamageEventBus.Publish(attacker, this, damage, damageSource);
			}
		}

		public sealed class DamageEventArgs
		{
			public GameActor Target { get; }
			public int Damage { get; set; }
			public Vector2? AttackOrigin { get; }
			public Vector2 AttackDirection { get; }
			public bool IsBlocked { get; set; }

			internal DamageEventArgs(GameActor target, int damage, Vector2? attackOrigin)
			{
				Target = target;
				Damage = damage;
				AttackOrigin = attackOrigin;
				if (attackOrigin.HasValue)
				{
					var delta = target.GlobalPosition - attackOrigin.Value;
					AttackDirection = delta.LengthSquared() > Mathf.Epsilon
						? delta.Normalized()
						: Vector2.Zero;
				}
				else
				{
					AttackDirection = Vector2.Zero;
				}
			}

			public Vector2 Forward => Target.FacingRight ? Vector2.Right : Vector2.Left;
		}

		/// <summary>
		/// 恢复或设置血量（用于加载存档等场景）
		/// </summary>
		public void RestoreHealth(int health, int maxHealth = -1)
		{
			// 死亡后治疗无效（与 TakeDamage 的死亡保护对称）：防止 Dying/Dead 期间
			// P2 治疗等到达时把血量拉回 >0，出现"死了但血条回满"的错误表现
			if (IsDeathSequenceActive || IsDead) return;

			if (maxHealth > 0)
			{
				MaxHealth = maxHealth;
			}
			CurrentHealth = Mathf.Clamp(health, 0, MaxHealth);
			NotifyHealthChanged();
			GameLogger.Info(nameof(GameActor), $"{Name} health restored to {CurrentHealth}/{MaxHealth}");
		}

		/// <summary>
		/// 广播伤害事件到全局监听者。用于非 GameActor 对象（如 Gate）被击中时触发相机抖动、特效等。
		/// </summary>
		/// <param name="victim">受击者（可为 null）</param>
		/// <param name="attacker">攻击者（通常是玩家）</param>
		/// <param name="damage">伤害值</param>
		public static void BroadcastDamage(GameActor? victim, GameActor? attacker, int damage)
		{
			AnyDamageTaken?.Invoke(victim, attacker, damage);
		}

		protected virtual void Die()
		{
			if (_deathStarted) return;

			_deathStarted = true;

			if (StateMachine != null && StateMachine.HasState("Dying"))
			{
				StateMachine.ChangeState("Dying");
			}
			else
			{
				FinalizeDeath();
			}
		}

		public void FinalizeDeath()
		{
			if (_deathFinalized) return;

			_deathFinalized = true;
			OnDeathFinalized();
			DeathFinalized?.Invoke(this);
		}

		protected virtual void OnDeathFinalized()
		{
			HandleLootDrops();
			EffectController?.ClearAll();
			QueueFree();
		}

		protected virtual void HandleLootDrops()
		{
			if (LootTable == null)
			{
				return;
			}

			LootDropSystem.SpawnLootForActor(this, LootTable);
		}

		/// <summary>
/// 是否可被指定效果影响。默认 true，子类（如 netAdmin）可覆写实现条件免疫。
/// effect 为 null 时表示普通伤害（非效果触发）。
/// </summary>
public virtual bool CanBeAffected(ActorEffect? effect) => true;

public void ApplyEffect(ActorEffect effect)
		{
			if (!CanBeAffected(effect)) return;
			EffectController?.AddEffect(effect);
		}

		public void RemoveEffect(string effectId)
		{
			var effect = EffectController?.GetEffect(effectId);
			if (effect != null)
			{
				EffectController?.RemoveEffect(effect);
			}
		}

		private void ApplyStatProfile()
		{
			if (StatProfile == null)
			{
				return;
			}

			foreach (var modifier in StatProfile.GetModifiers())
			{
				if (modifier == null || string.IsNullOrWhiteSpace(modifier.StatId)) continue;
				ApplyStatModifier(modifier);
			}

			if (EffectController == null)
			{
				return;
			}

			foreach (var effectScene in StatProfile.GetAttachedEffectScenes())
			{
				if (effectScene == null) continue;
				EffectController.AddEffectFromScene(effectScene);
			}
		}

		protected virtual void ApplyStatModifier(StatModifier modifier)
		{
			switch (modifier.StatId.ToLowerInvariant())
			{
				case "max_health":
					MaxHealth = (int)MathF.Round(ApplyStatOperation(MaxHealth, modifier));
					CurrentHealth = MaxHealth;
					NotifyHealthChanged();
					break;
				case "attack_damage":
					AttackDamage = ApplyStatOperation(AttackDamage, modifier);
					break;
				case "speed":
					Speed = ApplyStatOperation(Speed, modifier);
					break;
			}
		}

		private static float ApplyStatOperation(float baseValue, StatModifier modifier)
		{
			return modifier.Operation switch
			{
				StatOperation.Add => baseValue + modifier.Value,
				StatOperation.Multiply => baseValue * modifier.Value,
				_ => baseValue
			};
		}

		protected virtual void FlashDamageEffect()
		{
			if (!EnableDamageFlash)
			{
				return;
			}

			float duration = Mathf.Max(0f, DamageFlashDuration);

			// Use GDScript helper for Spine
			if (FlashSpineVisual && _spineHelper != null)
			{
				_spineHelper.Call("flash_damage", this, DamageFlashColor, _spineDefaultModulate, duration);
			}
			// Fallback or legacy handling if wrapper exists
			else if (FlashSpineVisual && _spineCharacter != null)
			{
				var visualNode = _spineCharacter;
				Color baseColor = _spineDefaultModulate;
				visualNode.Modulate = DamageFlashColor;
				ulong flashToken = ++_spineFlashToken;

				var tween = CreateTween();
				tween.TweenInterval(duration);
				tween.TweenCallback(Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(visualNode)) return;
					if (flashToken != _spineFlashToken) return;
					visualNode.Modulate = baseColor;
				}));
			}

			if (FlashSpriteVisual && _sprite != null)
			{
				Color baseColor = _spriteDefaultModulate;
				_sprite.Modulate = DamageFlashColor;
				ulong flashToken = ++_spriteFlashToken;

				var tween = CreateTween();
				tween.TweenInterval(duration);
				Node2D targetNode = _sprite;
				tween.TweenCallback(Callable.From(() =>
				{
					if (!GodotObject.IsInstanceValid(targetNode)) return;
					if (flashToken != _spriteFlashToken) return;
					targetNode.Modulate = baseColor;
				}));
			}
		}

		public virtual void FlipFacing(bool faceRight)
		{
			if (FacingRight == faceRight) return;
			
			FacingRight = faceRight;
			
			float sign = faceRight ? 1.0f : -1.0f;
			if (FaceLeftByDefault) sign *= -1.0f;
			
			// Use GDScript helper to flip
			if (_spineHelper != null)
			{
				_spineHelper.Call("flip_facing", this, faceRight, FaceLeftByDefault);
			}
			// Legacy handling
			else if (_spineCharacter != null)
			{
				var scale = _spineCharacter.Scale;
				float absX = Mathf.Abs(scale.X);
				_spineCharacter.Scale = new Vector2(absX * sign, scale.Y);
			}

			if (_sprite != null)
			{
				var scale = _sprite.Scale;
				float absX = Mathf.Abs(scale.X);
				_sprite.Scale = new Vector2(absX * sign, scale.Y);
			}
		}
		
		public void ClampPositionToScreen(float margin = 50f, float bottomOffset = 150f)
		{
			//限制角色移动，代码已弃用 OvO
			// var screenSize = GetViewportRect().Size;
			 // GlobalPosition = new Vector2(
			 // Mathf.Clamp(GlobalPosition.X, margin, screenSize.X - margin),
			 // Mathf.Clamp(GlobalPosition.Y, margin, screenSize.Y - bottomOffset) 
			// );
		}

		protected void NotifyHealthChanged()
		{
			HealthChanged?.Invoke(CurrentHealth, MaxHealth);
		}

		/// <summary>
		/// 禁用自身及所有子节点的 CollisionShape2D。
		/// </summary>
		public void DisableCollisionShape()
		{
			DisableCollisionShapeInNode(this);
		}

		private void DisableCollisionShapeInNode(Node node)
		{
			foreach (Node child in node.GetChildren())
			{
				if (child is CollisionShape2D shape)
				{	
					//shape.Disabled = true;
					shape.SetDeferred(CollisionShape2D.PropertyName.Disabled, true);
				}

				// 递归处理所有子节点
				DisableCollisionShapeInNode(child);
			}
		}
	}
}
