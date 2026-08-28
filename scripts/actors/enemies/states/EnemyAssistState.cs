using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Managers;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 检测范围内的友方血量，当某个友方血量低于阈值时进入该状态追踪并治疗，结束后回到 Walk。
    /// 优先级：血量比例最低者优先；比例相同时选最近的。
    ///
    /// 性能策略：
    ///   · 用 GetTree().GetNodesInGroup("enemies") 扫描，绕过 DetectionArea 碰撞层限制
    ///     （ControllerDetectionArea.collision_mask=4 只检测玩家，无法感知其他敌人）
    ///   · 每 ScanInterval 秒刷新"范围内友方"集合，同时订阅其 HealthChanged 做即时重算
    ///   · ShouldTriggerAssist() 只读缓存 bool，Idle/Walk 每帧调用无开销
    /// </summary>
    public partial class EnemyAssistState : EnemyState
    {
        [ExportCategory("Detection")]
        [Export(PropertyHint.Range, "100,5000,100")]
        public float ScanRadius { get; set; } = 1500f;  //

        [Export(PropertyHint.Range, "0.2,5,0.1")]
        public float ScanInterval { get; set; } = 1f;       

        [Export(PropertyHint.Range, "1,100,1")]
        public float HealthThresholdPercent { get; set; } = 50f;

        [ExportCategory("Healing")]
        [Export(PropertyHint.Range, "1,100,1")]
        public int HealPercent { get; set; } = 30;

        /// <summary>移动接近点固定距离（像素）：停在 目标中心 - 方向 × 此距离 处（越大停得越远）；
        /// 同时作为有效治疗距离（进入此范围内开始治疗）。</summary>
        [Export(PropertyHint.Range, "10,2000,10")]
        public float ApproachDistance = 100f;

        [Export(PropertyHint.Range, "0,10,0.1")]
        public float HealDelay { get; set; } = 1.5f;

        /// <summary>Assist 移动速度 = Enemy.Speed × 此倍率（基础速度调整时自动适配）。</summary>
        [Export(PropertyHint.Range, "0.1,3,0.05")]
        public float AssistMoveSpeedMultiplier = 1.2f;

        [ExportCategory("Use Cooldown")]
        [Export(PropertyHint.Range, "1,20,1")]
        public int UsesBeforeCooldown = 1; // 完成治疗多少次后进入冷却（被打断不计数）

        [Export(PropertyHint.Range, "0.5,60,0.5")]
        public float CooldownAfterUses = 10.0f;

        /// <summary>打断后拒绝期（秒）：目标丢失/受伤眩晕打断 Assist 后，此期间不重新进入（防反复进出刷治疗）。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")]
        public float InterruptRefuseDuration = 3.0f;

        [ExportCategory("Collision Override")]
        /// <summary>Assist 期间忽略敌人碰撞（禁用 Body 碰撞体——穿过其他敌人去治疗，避障保证不穿墙）。</summary>
        [Export] public bool IgnoreEnemyCollisionDuringAssist = false;

        [ExportCategory("Assist Effect")]
        /// <summary>治疗状态显示特效：Assist 状态期间生成，退出/中断时销毁（让玩家清晰注意该敌人正在治疗）。</summary>
        [Export] public PackedScene? AssistEffectScene { get; set; }
        /// <summary>特效生成锚点（Marker2D 放在敌人根节点下，同 EnemyAttackTemplate.SpawnMarkers 语义）：
        /// 不为空时按数组顺序轮换使用 Marker2D.GlobalPosition 作为生成位置，否则用敌人原点。</summary>
        [Export] public Marker2D[] AssistSpawnMarkers = System.Array.Empty<Marker2D>();
        /// <summary>特效生成位置偏移（叠加在锚点之上，X 按朝向取反）。</summary>
        [Export] public Vector2 AssistEffectOffset = Vector2.Zero;

        [ExportCategory("Damage Interrupt")]
        /// <summary>受伤眩晕打断：Assist 状态下自身受伤时中断治疗并切入 Frozen（眩晕）。</summary>
        [Export] public bool StunInterruptOnDamage = true;
        /// <summary>受伤眩晕时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageTakenFrozenDuration = 1.5f;
        /// <summary>
        /// 触发眩晕打断的伤害类型过滤（Flags）：默认 All（任何伤害都触发，原行为）；
        /// 选中特定类型后，仅该伤害类型命中才中断治疗进入眩晕。
        /// </summary>
        [Export(PropertyHint.Flags, "直接攻击,投掷类,区域效果,暴击追加,效果追加")]
        public DamageSourceFilter InterruptDamageSources { get; set; } = DamageSourceFilter.All;

        // 当前在扫描范围内的友方集合（不含自身）
        private readonly HashSet<SampleEnemy> _nearbyAllies = new();

        private SampleEnemy? _cachedTarget;
        private bool _hasValidTarget;
        private float _scanTimer = 0f;
        private float _healDelayTimer;
        private bool _waitingToHeal;
        private int _useCount;
        private float _cooldownTimer;
        private float _interruptRefuseTimer; // 打断后拒绝期（目标丢失/受伤眩晕后短暂不重新进入）
        private NavigationAgent2D? _navAgent;
        // 敌人碰撞忽略（Assist 期间禁用 Body 碰撞体——其他敌人推不到自己、自己穿过；避障保证不穿墙）
        private CollisionShape2D? _bodyShape;
        private bool _bodyShapeWasDisabled;

        // 治疗显示特效实例（挂敌人下跟随移动，位置由 AssistSpawnMarkers 锚点 + 朝向镜像决定）
        private Node2D? _assistEffectInstance;
        private int _spawnMarkerIndex;
        private Vector2 _effectBaseLocalPos; // marker 局部位置（未镜像，朝向翻转时镜像重算）
        private bool _damageInterruptSubscribed;

        // ─── 生命周期 ────────────────────────────────────────────────────────────

        public override void _Ready()
        {
            base._Ready();
            if (Engine.IsEditorHint()) return;
            CallDeferred(MethodName.RegisterAsBlockedState);
        }

        public override void _ExitTree()
        {
            UnsubscribeAll();
            RestoreEnemyCollisionMask();
            base._ExitTree();
        }

        /// <summary>
        /// 找到 EnemyChaseMovement 组件，将 "Assist" 加入 BlockedStates，
        /// 防止追踪组件在 Assist 状态下把速度覆盖为朝玩家方向。
        /// </summary>
        private void RegisterAsBlockedState()
        {
            if (Enemy == null) return;
            foreach (var child in Enemy.GetChildren())
            {
                if (child is EnemyChaseMovement movement)
                {
                    var assistName = new StringName("Assist");
                    if (!movement.BlockedStates.Contains(assistName))
                        movement.BlockedStates.Add(assistName);
                    break;
                }
            }
        }

        // ─── 主循环：定期刷新范围内友方集合 ──────────────────────────────────────

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint() || Enemy == null) return;

            // 治疗特效位置跟随朝向（FacingRight 翻转时镜像到另一侧）
            if (_assistEffectInstance != null && GodotObject.IsInstanceValid(_assistEffectInstance))
                _assistEffectInstance.Position = ComputeEffectLocalPos();

            // 打断拒绝期倒计时
            if (_interruptRefuseTimer > 0f)
                _interruptRefuseTimer -= (float)delta;

            // 冷却倒计时（无论激活状态始终运行）
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
                if (_cooldownTimer <= 0f)
                {
                    _cooldownTimer = 0f;
                    _useCount = 0;
                }
            }

            _scanTimer -= (float)delta;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                RefreshNearbyAllies();
            }
        }

        /// <summary>
        /// 用 Group 扫描替代 DetectionArea 信号，与碰撞层完全解耦。
        /// 对新进入范围的敌人订阅 HealthChanged，对离开的取消订阅。
        /// </summary>
        private void RefreshNearbyAllies()
        {
            float radiusSq = ScanRadius * ScanRadius;
            var allEnemies = GetTree().GetNodesInGroup("enemies");

            // 找出本次在范围内的集合
            var currentSet = new HashSet<SampleEnemy>();
            foreach (var node in allEnemies)
            {
                if (node is not SampleEnemy other) continue;
                if (other == Enemy) continue;
                if (other.IsDead || other.IsDeathSequenceActive) continue;
                if (Enemy.GlobalPosition.DistanceSquaredTo(other.GlobalPosition) <= radiusSq)
                    currentSet.Add(other);
            }

            // 新进入范围 → 订阅 HealthChanged
            foreach (var e in currentSet)
            {
                if (_nearbyAllies.Add(e))
                    e.HealthChanged += OnAllyHealthChanged;
            }

            // 离开范围 → 取消订阅
            var toRemove = new List<SampleEnemy>();
            foreach (var e in _nearbyAllies)
            {
                if (!currentSet.Contains(e))
                    toRemove.Add(e);
            }
            foreach (var e in toRemove)
            {
                _nearbyAllies.Remove(e);
                if (GodotObject.IsInstanceValid(e))
                    e.HealthChanged -= OnAllyHealthChanged;
            }

            ReevaluateTarget();
        }

        private void UnsubscribeAll()
        {
            foreach (var e in _nearbyAllies)
            {
                if (GodotObject.IsInstanceValid(e))
                    e.HealthChanged -= OnAllyHealthChanged;
            }
            _nearbyAllies.Clear();
        }

        // ─── 事件：友方受伤时即时重算 ────────────────────────────────────────────

        private void OnAllyHealthChanged(int current, int max) => ReevaluateTarget();

        // ─── 目标评估 ────────────────────────────────────────────────────────────

        private void ReevaluateTarget()
        {
            if (Enemy == null) return;

            float threshold = HealthThresholdPercent / 100f;
            SampleEnemy? best = null;
            float bestRatio = float.MaxValue;
            float bestDist  = float.MaxValue;

            foreach (var other in _nearbyAllies)
            {
                if (!GodotObject.IsInstanceValid(other)) continue;
                if (other.IsDead || other.IsDeathSequenceActive) continue;
                if (other.MaxHealth <= 0) continue;

                float ratio = (float)other.CurrentHealth / other.MaxHealth;
                if (ratio >= threshold) continue;

                float dist = Enemy.GlobalPosition.DistanceTo(other.GlobalPosition);

                bool betterRatio     = ratio < bestRatio - 0.01f;
                bool sameRatioCloser = Mathf.Abs(ratio - bestRatio) <= 0.01f && dist < bestDist;
                if (betterRatio || sameRatioCloser)
                {
                    best      = other;
                    bestRatio = ratio;
                    bestDist  = dist;
                }
            }

            _cachedTarget   = best;
            _hasValidTarget = best != null;
        }

        // ─── 状态接口 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 供外部（Idle / Walk）查询是否应进入援助状态。只读缓存，零开销。
        /// 冷却中或打断拒绝期内不触发。
        /// </summary>
        public bool ShouldTriggerAssist() => _cooldownTimer <= 0f && _interruptRefuseTimer <= 0f && _hasValidTarget && _cachedTarget != null;

        public override void Enter()
        {
            // 立刻做一次完整扫描，不等下一个 ScanInterval
            RefreshNearbyAllies();
            if (_cachedTarget == null)
            {
                ChangeState("Walk");
                return;
            }

            _healDelayTimer = 0f;
            _waitingToHeal = false;
            _navAgent = Enemy.GetNodeOrNull<NavigationAgent2D>("NavigationAgent2D");

            // 治疗显示特效（跟随敌人，状态退出时销毁）
            SpawnAssistEffect();
            SubscribeDamageInterrupt();
            // Assist 期间忽略敌人碰撞（穿过其他敌人去治疗）
            ApplyEnemyCollisionMaskOverride();

            Enemy.AnimPlayer?.Play("animations/Walk");
        }

        public override void Exit()
        {
            DestroyAssistEffect();
            UnsubscribeDamageInterrupt();
            RestoreEnemyCollisionMask();
            base.Exit();
        }

        public override void PhysicsUpdate(double delta)
        {
            if (_cachedTarget == null
                || !GodotObject.IsInstanceValid(_cachedTarget)
                || _cachedTarget.IsDead
                || _cachedTarget.IsDeathSequenceActive)
            {
                _cachedTarget   = null;
                _hasValidTarget = false;
                // 目标丢失打断：设置拒绝期（防反复进出刷治疗，不消耗使用次数）
                _interruptRefuseTimer = InterruptRefuseDuration;
                ChangeState("Walk");
                return;
            }

            // 有效治疗距离 = ApproachDistance（进入此范围内开始治疗）
            float dist = Enemy.GlobalPosition.DistanceTo(_cachedTarget.GlobalPosition);
            if (dist <= ApproachDistance)
            {
                if (HealDelay <= 0f)
                {
                    ApplyHeal();
                    return;
                }

                if (!_waitingToHeal)
                {
                    _waitingToHeal = true;
                    _healDelayTimer = HealDelay;
                }

                _healDelayTimer -= (float)delta;
                if (_healDelayTimer <= 0f)
                {
                    ApplyHeal();
                    return;
                }

                // 等待期间保持在治疗范围内，不追踪移动（避免接近点动态重算导致前后反转）
                return;
            }

            _waitingToHeal = false;
            _healDelayTimer = 0f;

            MoveTowardTarget();
        }

        private void MoveTowardTarget()
        {
            if (_cachedTarget == null) return;

            // 已在有效治疗范围内：不移动（防御——避免接近点动态重算导致前后反转）
            if (Enemy.GlobalPosition.DistanceTo(_cachedTarget.GlobalPosition) <= ApproachDistance)
                return;

            // 接近点：目标中心 - 方向 × 固定距离——停在目标外 ApproachDistance 处，不移动到目标中心点
            Vector2 toTarget = _cachedTarget.GlobalPosition - Enemy.GlobalPosition;
            float keepDist = Mathf.Max(ApproachDistance, 10f);
            Vector2 approachTarget = _cachedTarget.GlobalPosition - toTarget.Normalized() * keepDist;

            Vector2 dir;
            float speed = Enemy.Speed * AssistMoveSpeedMultiplier;

            if (_navAgent != null)
            {
                if (_navAgent.TargetPosition.DistanceSquaredTo(approachTarget) > 100f)
                    _navAgent.TargetPosition = approachTarget;

                if (!_navAgent.IsNavigationFinished())
                {
                    Vector2 nextPoint = _navAgent.GetNextPathPosition();
                    dir = (nextPoint - Enemy.GlobalPosition).Normalized();
                }
                else
                {
                    dir = Enemy.GlobalPosition.DirectionTo(approachTarget);
                }
            }
            else
            {
                dir = Enemy.GlobalPosition.DirectionTo(approachTarget);
            }

            if (dir.IsZeroApprox()) return;

            Enemy.Velocity = dir * speed;
            if (Mathf.Abs(dir.X) > 0.1f)
                Enemy.FlipFacing(dir.X > 0);
            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();
        }

        private void ApplyHeal()
        {
            if (_cachedTarget == null) return;

            int amount = Mathf.Max(1, Mathf.RoundToInt(_cachedTarget.MaxHealth * HealPercent / 100f));
            Vector2 healPosition = _cachedTarget.GlobalPosition;
            _cachedTarget.RestoreHealth(_cachedTarget.CurrentHealth + amount);
            FloatingDamageTextManager.Instance.ShowFloatingHealing(amount, healPosition, 0f);
            _cachedTarget = null;
            _hasValidTarget = false;
            _waitingToHeal = false;

            // 完成治疗才计数（被打断不消耗次数）——达到阈值进入冷却
            _useCount++;
            if (UsesBeforeCooldown > 0 && _useCount >= UsesBeforeCooldown)
                _cooldownTimer = Mathf.Max(CooldownAfterUses, 0f);

            ChangeState("Walk");
        }

        // ─── 治疗显示特效 ──────────────────────────────────────────────────────

        /// <summary>
        /// 生成治疗显示特效（挂敌人下跟随移动）：位置 = 锚点（Marker2D 局部位置，朝向镜像）
        /// 或敌人原点 + AssistEffectOffset；朝向翻转时由 _Process 每帧重算位置（跟随镜像）。
        /// </summary>
        private void SpawnAssistEffect()
        {
            if (AssistEffectScene == null || Enemy == null) return;
            var node = AssistEffectScene.Instantiate();
            if (node is not Node2D node2D)
            {
                node?.QueueFree();
                return;
            }

            _effectBaseLocalPos = Vector2.Zero;
            if (AssistSpawnMarkers.Length > 0)
            {
                int idx = _spawnMarkerIndex % AssistSpawnMarkers.Length;
                var marker = AssistSpawnMarkers[idx];
                if (marker != null && GodotObject.IsInstanceValid(marker))
                    _effectBaseLocalPos = marker.Position; // 局部坐标（相对敌人）
                _spawnMarkerIndex++;
            }

            Enemy.AddChild(node2D);
            node2D.Position = ComputeEffectLocalPos();
            _assistEffectInstance = node2D;
        }

        // ─── 敌人碰撞忽略（Assist 期间禁用 Body 碰撞体，穿过其他敌人；避障保证不穿墙）────────

        /// <summary>禁用敌人 Body 碰撞体（Assist 期间不与其他敌人碰撞/不被推动）。</summary>
        private void ApplyEnemyCollisionMaskOverride()
        {
            if (!IgnoreEnemyCollisionDuringAssist || Enemy == null || _bodyShape != null)
                return;

            _bodyShape = FindBodyShape();
            if (_bodyShape == null || _bodyShape.Disabled)
                return;

            _bodyShape.Disabled = true;
            _bodyShapeWasDisabled = true;
        }

        private void RestoreEnemyCollisionMask()
        {
            if (_bodyShape == null)
                return;

            if (GodotObject.IsInstanceValid(_bodyShape) && _bodyShapeWasDisabled)
                _bodyShape.Disabled = false;
            _bodyShape = null;
            _bodyShapeWasDisabled = false;
        }

        /// <summary>查找敌人的主碰撞体（CharacterBody2D 直接子节点的 CollisionShape2D）。</summary>
        private CollisionShape2D? FindBodyShape()
        {
            if (Enemy == null) return null;
            var direct = Enemy.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (direct != null) return direct;
            foreach (Node child in Enemy.GetChildren())
            {
                if (child is CollisionShape2D shape)
                    return shape;
            }
            return null;
        }

        /// <summary>特效局部位置 = 锚点局部坐标（朝向镜像）+ offset（朝向镜像）。</summary>
        private Vector2 ComputeEffectLocalPos()
        {
            bool facingRight = Enemy?.FacingRight ?? true;
            Vector2 p = _effectBaseLocalPos;
            if (!facingRight) p.X = -p.X;
            Vector2 off = AssistEffectOffset;
            if (!facingRight) off.X = -off.X;
            return p + off;
        }

        private void DestroyAssistEffect()
        {
            if (_assistEffectInstance != null && GodotObject.IsInstanceValid(_assistEffectInstance))
                _assistEffectInstance.QueueFree();
            _assistEffectInstance = null;
        }

        // ─── 受伤眩晕打断 ──────────────────────────────────────────────────────

        private void SubscribeDamageInterrupt()
        {
            if (!StunInterruptOnDamage || Enemy == null || _damageInterruptSubscribed) return;
            DamageEventBus.SubscribeWithSource(OnDamageTaken);
            _damageInterruptSubscribed = true;
        }

        private void UnsubscribeDamageInterrupt()
        {
            if (Enemy == null || !_damageInterruptSubscribed) return;
            DamageEventBus.UnsubscribeWithSource(OnDamageTaken);
            _damageInterruptSubscribed = false;
        }

        private void OnDamageTaken(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (target != Enemy) return;
            if (Enemy == null) return;

            // 伤害类型过滤（InterruptDamageSources）：默认 All 全放行（原行为）
            if (!InterruptDamageSources.Matches(source)) return;

            // 中断治疗并切入眩晕（该状态无 Warmup/Active/Recovery 阶段，受伤即眩晕）
            UnsubscribeDamageInterrupt();
            DestroyAssistEffect();
            // 受伤眩晕打断：设置拒绝期（眩晕结束后短暂不重新进入治疗）
            _interruptRefuseTimer = InterruptRefuseDuration;

            // 受伤眩晕打断也计一次使用（与完成治疗并列）——防"打断后立刻重新治疗"反复消耗
            _useCount++;
            if (UsesBeforeCooldown > 0 && _useCount >= UsesBeforeCooldown)
                _cooldownTimer = Mathf.Max(CooldownAfterUses, 0f);
            var frozenState = Enemy.StateMachine?.GetNodeOrNull<EnemyFrozenState>("Frozen");
            if (frozenState != null)
                frozenState.FrozenDuration = Mathf.Max(DamageTakenFrozenDuration, 0.1f);
            Enemy.StateMachine?.ChangeState("Frozen");
        }
    }
}

