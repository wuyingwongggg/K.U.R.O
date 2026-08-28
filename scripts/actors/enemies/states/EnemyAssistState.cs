using Godot;
using System.Collections.Generic;
using Kuros.Actors.Enemies.Attacks;
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
        /// <summary>治疗状态显示特效条目（同 EnemyAttackTemplate.Effects 机制：AttackEffectEntry = 场景 + UniqueGroup + PropertyOverrides 重载）。
        /// Assist 状态期间全部生成，退出/中断时统一销毁（让玩家清晰注意该敌人正在治疗）。</summary>
        [Export] public Godot.Collections.Array<AttackEffectEntry> Effects { get; set; } = new();

        [ExportCategory("Damage Interrupt")]
        /// <summary>受伤眩晕打断：Assist 状态下自身受伤时中断治疗并切入 Frozen（眩晕）。</summary>
        [Export] public bool StunInterruptOnDamage = true;
        /// <summary>受伤眩晕时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageTakenFrozenDuration = 1.5f;
        /// <summary>
        /// 触发眩晕打断的伤害类型过滤（Flags）：默认 All（任何伤害都触发，原行为）；
        /// 选中特定类型后，仅该伤害类型命中才中断治疗进入眩晕。
        /// 走实例信号 DamageTakenDetailed（无条件、带来源），不依赖全局总线。
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

        // 治疗显示特效实例列表（挂敌人下跟随移动，位置由各特效自身 Meta 中的锚点 + 朝向镜像决定）
        private readonly List<Node2D> _assistEffectInstances = new();
        private bool _damageInterruptSubscribed;

        // ─── 生命周期 ────────────────────────────────────────────────────────────

        public override void _Ready()
        {
            base._Ready();
            if (Engine.IsEditorHint()) return;
            CallDeferred(MethodName.RegisterAsBlockedState);
            // 兜底订阅：状态节点就绪后延迟注册（Actor 注入完成后）——即使 Enter 链路异常也保证打断订阅存在
            CallDeferred(MethodName.SubscribeDamageInterrupt);
        }

        public override void _ExitTree()
        {
            UnsubscribeAll();
            // 关键：退订 DamageEventBus——否则敌人死亡后失效委托残留在全局静态列表，
            // 后续 Publish 遍历调用已释放对象会中断广播，导致所有打断（Assist/攻击模板）全局失效
            UnsubscribeDamageInterrupt();
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

            // 治疗特效位置跟随朝向（FacingRight 翻转时镜像到另一侧）——各特效用各自的锚点/偏移
            foreach (var fx in _assistEffectInstances)
            {
                if (GodotObject.IsInstanceValid(fx))
                {
                    Vector2 basePos = fx.GetMeta("assist_base_local_pos", Vector2.Zero).AsVector2();
                    Vector2 offset = fx.GetMeta("assist_offset", Vector2.Zero).AsVector2();
                    fx.Position = ComputeEffectLocalPos(basePos, offset);
                }
            }

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
            // 最先注册受伤打断订阅：不依赖后续任何逻辑（目标扫描/特效生成异常都不影响订阅）
            SubscribeDamageInterrupt();

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
        /// 支持多特效条目（Effects：场景 + PropertyOverrides 重载 + UniqueGroup），旧单特效回退。
        /// </summary>
        private void SpawnAssistEffect()
        {
            if (Enemy == null) return;

            for (int i = 0; i < Effects.Count; i++)
            {
                var entry = Effects[i];
                if (entry == null) continue;
                SpawnSingleEffect(entry.Scene, entry, i);
            }
        }

        private void SpawnSingleEffect(PackedScene? scene, AttackEffectEntry? entry, int effectIndex)
        {
            if (scene == null || Enemy == null) return;
            var node = scene.Instantiate();
            if (node is not Node2D node2D)
            {
                node?.QueueFree();
                return;
            }

            if (entry != null)
            {
                entry.ApplyOverrides(node2D);
                // 唯一性组标记（供外部"场上是否已有该特效"检测）
                if (!string.IsNullOrEmpty(entry.UniqueGroup))
                    node2D.AddToGroup(entry.UniqueGroup);
            }

            // 位置走 entry 的 per-entry 配置（Assist 无模板级——fallback 零值）：
            // 锚点 = entry.SpawnMarkerPaths 解析（先按 Assist 状态节点自身，如 "../../Node2D/Marker2D"，失败按敌人根兜底）；偏移 = entry.EffectOffset
            Vector2 entryOffset = entry?.ResolveOffset(Vector2.Zero) ?? Vector2.Zero;
            Marker2D[] markers = entry?.ResolveMarkers(this, Enemy, System.Array.Empty<Marker2D>())
                ?? System.Array.Empty<Marker2D>();

            // 固定对应：第 N 个特效 → 第 N 个 Marker（数量不足时取模循环）。
            // 锚点局部坐标挂在特效自身 Meta——_Process 每帧重算镜像位置时各自用各自的锚点
            Vector2 baseLocalPos = Vector2.Zero;
            if (markers.Length > 0)
            {
                int idx = effectIndex % markers.Length;
                var marker = markers[idx];
                if (marker != null && GodotObject.IsInstanceValid(marker))
                    baseLocalPos = marker.Position; // 局部坐标（相对敌人根）
            }
            node2D.SetMeta("assist_base_local_pos", baseLocalPos);
            node2D.SetMeta("assist_offset", entryOffset);

            Enemy.AddChild(node2D);
            node2D.Position = ComputeEffectLocalPos(baseLocalPos, entryOffset);
            // 抵消敌人根缩放（如 waiterA 根 scale 0.33）：Assist 特效挂在敌人下会继承缩放被缩小，
            // 按世界大小显示（同攻击模板挂世界层的效果）
            if (Enemy.Scale != Vector2.One)
                node2D.Scale = new Vector2(node2D.Scale.X / Enemy.Scale.X, node2D.Scale.Y / Enemy.Scale.Y);
            _assistEffectInstances.Add(node2D);
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
        private Vector2 ComputeEffectLocalPos(Vector2 baseLocalPos, Vector2 offset)
        {
            bool facingRight = Enemy?.FacingRight ?? true;
            Vector2 p = baseLocalPos;
            if (!facingRight) p.X = -p.X;
            Vector2 off = offset;
            if (!facingRight) off.X = -off.X;
            return p + off;
        }

        private void DestroyAssistEffect()
        {
            foreach (var fx in _assistEffectInstances)
            {
                if (GodotObject.IsInstanceValid(fx))
                    fx.QueueFree();
            }
            _assistEffectInstances.Clear();
        }

        // ─── 受伤眩晕打断（DamageTakenDetailed 实例信号：无条件 + 带伤害类型）────────

        private void SubscribeDamageInterrupt()
        {
            if (!StunInterruptOnDamage || Enemy == null || _damageInterruptSubscribed) return;
            Enemy.DamageTakenDetailed += OnDamageTakenDetailed;
            _damageInterruptSubscribed = true;
        }

        private void UnsubscribeDamageInterrupt()
        {
            if (Enemy == null || !_damageInterruptSubscribed) return;
            Enemy.DamageTakenDetailed -= OnDamageTakenDetailed;
            _damageInterruptSubscribed = false;
        }

        private void OnDamageTakenDetailed(int damage, DamageSource source, GameActor? attacker)
        {
            if (Enemy == null) return;

            // _Ready 兜底订阅使回调可能在其他状态到达——只在 Assist 状态内响应打断
            if (Enemy.StateMachine?.CurrentState != this) return;

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

