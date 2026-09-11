using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 攻击后短暂后撤（闪避）：玩家攻击时向后远离玩家，距离 + 时间驱动（匀速）。
    /// 触发：由 EnemyThinWalkState 检测玩家攻击（IsPlayerAttacking + 攻击范围）后切入。
    /// 结束后分流：玩家仍在攻击范围 → 直接 Attack；否则回 NextStateName（默认 Walk）。
    /// 期间可提供伤害免疫（DamageIntercepted 拦截）与超级装甲（IgnoreHitStateOnDamage）。
    /// 使用次数冷却：每次后撤后短冷却防连续闪避；达上限进入长冷却，结束后重置计数。
    /// </summary>
    public partial class EnemyDashBackState : EnemyState
    {
        /// <summary>后撤总距离（像素）。</summary>
        [Export(PropertyHint.Range, "10,2000,10")] public float DashDistance = 200f;

        /// <summary>后撤总时长（秒）：速度 = 距离 ÷ 时间（派生）——匀速移动固定距离；
        /// 被障碍阻挡时时间到即退（超时兜底，防止无限闪避）。</summary>
        [Export(PropertyHint.Range, "0.05,2,0.01")] public float DashDuration = 0.2f;

        /// <summary>派生速度 = DashDistance / DashDuration（Enter 时计算，匀速）。</summary>
        private float _dashSpeed;

        /// <summary>后撤动画名（播放一次，通常复用 walk 动画）。</summary>
        [Export] public string AnimationName = "animations/walk";

        /// <summary>后撤结束后的默认下一状态（玩家不在攻击范围时）。</summary>
        [Export] public string NextStateName = "Walk";

        [ExportCategory("Obstacle Avoidance")]
        /// <summary>避障射线长度（像素）。</summary>
        [Export(PropertyHint.Range, "10,500,10")] public float RaycastDistance = 100f;

        /// <summary>后撤前是否做障碍检测（找通畅方向；主方向被堵时尝试 ±45°/90°/180°）。</summary>
        [Export] public bool EnableObstacleAvoidance = true;

        [ExportCategory("Interrupt")]
        /// <summary>后撤期间拦截受到的伤害（DamageIntercepted → IsBlocked，配合免伤表现）。</summary>
        [Export] public bool EnableDamageImmunity = true;

        /// <summary>后撤期间超级装甲：忽略受击硬直（IgnoreHitStateOnDamage），且不被普通状态打断（CanExitTo 限制）。</summary>
        [Export] public bool EnableSuperArmor = true;

        [ExportCategory("Use Cooldown")]
        /// <summary>长冷却前的使用次数上限（达上限进入 CooldownAfterUses）。</summary>
        [Export(PropertyHint.Range, "1,20,1")] public int UsesBeforeCooldown = 2;

        /// <summary>达到使用上限后的长冷却时长（秒），结束后重置使用计数。</summary>
        [Export(PropertyHint.Range, "0.5,60,0.5")] public float CooldownAfterUses = 5.0f;

        /// <summary>每次 dash 后的短冷却（未达使用上限时）：防止连续闪避浪费次数。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float ShortCooldownAfterDash = 0.5f;

        /// <summary>已使用次数（长冷却结束后清零；短冷却不重置——否则计数被清、长冷却永不触发）。</summary>
        private int _useCount;
        /// <summary>冷却倒计时（短/长共用；CanEnterFrom 拒绝重入）。</summary>
        private float _cooldownTimer;
        /// <summary>当前是否为长冷却（结束后重置使用计数；短冷却不重置）。</summary>
        private bool _longCooldownActive;

        /// <summary>后撤剩余时间（超时兜底）。</summary>
        private float _timer;
        /// <summary>后撤方向（Enter 时确定：背对玩家 + 避障修正）。</summary>
        private Vector2 _dashDirection = Vector2.Zero;
        /// <summary>后撤起点（距离驱动：达到 DashDistance 即结束）。</summary>
        private Vector2 _startPosition;
        /// <summary>进入前的 IgnoreHitStateOnDamage（退出时还原）。</summary>
        private bool? _previousIgnoreHitStateOnDamage;

        // ── 使用次数冷却 ──────────────────────────────────────────────────────
        public override void _Process(double delta)
        {
            // 无论当前处于哪个状态都持续倒计时
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
                if (_cooldownTimer <= 0f)
                {
                    _cooldownTimer = 0f;
                    // 仅长冷却结束后重置次数；短冷却不重置（保持计数，防连续闪避的间隔不吞使用次数）
                    if (_longCooldownActive)
                    {
                        _useCount = 0;
                        _longCooldownActive = false;
                    }
                }
            }
        }

        /// <summary>冷却期间禁止进入（防止连续后撤）。</summary>
        public override bool CanEnterFrom(string? currentStateName)
        {
            if (_cooldownTimer > 0f) return false;
            return base.CanEnterFrom(currentStateName);
        }

        /// <summary>
        /// 进入后撤：确定方向（背对玩家 + 避障修正）、派生速度、计使用次数（短/长冷却）、
        /// 开启伤害免疫/超级装甲、播放动画。
        /// </summary>
        public override void Enter()
        {
            _timer = Mathf.Max(DashDuration, 0.01f);
            _startPosition = Enemy.GlobalPosition;
            // 速度派生：距离 ÷ 时间——匀速，固定时间内移动固定距离
            _dashSpeed = DashDistance / Mathf.Max(DashDuration, 0.01f);

            // 累加使用次数：达上限 → 长冷却；否则每次 dash 后设短冷却（防止连续闪避浪费次数）
            _useCount++;
            if (UsesBeforeCooldown > 0 && _useCount >= UsesBeforeCooldown)
            {
                _cooldownTimer = Mathf.Max(CooldownAfterUses, 0f);
                _longCooldownActive = true;
            }
            else
            {
                _cooldownTimer = Mathf.Max(ShortCooldownAfterDash, 0f);
                _longCooldownActive = false;
            }

            // 后撤主方向：背对玩家（远离）；无玩家方向时按当前朝向反向（默认后退）
            Vector2 preferredDirection = Vector2.Zero;
            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer != Vector2.Zero)
            {
                preferredDirection = -toPlayer;
            }
            else
            {
                preferredDirection = Enemy.FacingRight ? Vector2.Left : Vector2.Right;
            }

            // 在后撤前检测障碍并确定最终方向（主方向被堵时尝试替代角度）
            if (EnableObstacleAvoidance && preferredDirection != Vector2.Zero)
            {
                _dashDirection = FindClearDirection(preferredDirection).Normalized();
            }
            else
            {
                _dashDirection = preferredDirection.Normalized();
            }

            // 超级装甲：记录并覆盖 IgnoreHitStateOnDamage（退出时还原）
            if (EnableSuperArmor)
            {
                _previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
                Enemy.IgnoreHitStateOnDamage = true;
            }
            else
            {
                _previousIgnoreHitStateOnDamage = null;
            }

            // 伤害免疫：拦截受到的伤害（先退订防重复订阅）
            Enemy.DamageIntercepted -= OnEnemyDamageIntercepted;
            if (EnableDamageImmunity)
            {
                Enemy.DamageIntercepted += OnEnemyDamageIntercepted;
            }

            if (Enemy.AnimPlayer != null && !string.IsNullOrEmpty(AnimationName) && Enemy.AnimPlayer.HasAnimation(AnimationName))
            {
                Enemy.AnimPlayer.Play(AnimationName);
            }
        }

        /// <summary>退出后撤：归零速度、退订伤害拦截、还原超级装甲（IgnoreHitStateOnDamage）。</summary>
        public override void Exit()
        {
            if (Enemy != null && GodotObject.IsInstanceValid(Enemy))
            {
                Enemy.Velocity = Vector2.Zero;
                Enemy.DamageIntercepted -= OnEnemyDamageIntercepted;

                if (_previousIgnoreHitStateOnDamage.HasValue)
                {
                    Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
                    _previousIgnoreHitStateOnDamage = null;
                }
            }
        }

        /// <summary>后撤结束（计时耗尽）允许切任何状态；期间仅允许死亡/濒死；
        /// 超级装甲开启时禁止被 Hit/Frozen 等打断。</summary>
        public override bool CanExitTo(string nextStateName)
        {
            if (_timer <= 0f)
            {
                return true;
            }

            if (nextStateName == "Dying" || nextStateName == "Dead")
            {
                return true;
            }

            if (EnableSuperArmor)
            {
                return false;
            }

            return nextStateName == "Hit"
                || nextStateName == "Frozen"
                || nextStateName == "CooldownFrozen";
        }

        /// <summary>
        /// 解析 DashBack 结束后的下一状态：玩家在攻击范围内（CanStartAttack 通过）→ 直接 Attack；
        /// 否则回 NextStateName（默认 Walk）。
        /// </summary>
        private string ResolveNextStateName()
        {
            if (Enemy.CanStartAttack() && Enemy.StateMachine?.HasState("Attack") == true)
                return "Attack";
            return NextStateName;
        }

        /// <summary>伤害拦截回调：后撤期间（计时未耗尽）拦截全部伤害（免伤表现）。</summary>
        private bool OnEnemyDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (!EnableDamageImmunity || _timer <= 0f)
            {
                return false;
            }

            args.IsBlocked = true;
            return true;
        }

        /// <summary>
        /// 后撤移动：按派生速度沿 _dashDirection 匀速移动（MoveAndSlide）。
        /// 达到 DashDistance 或超时（_timer 耗尽，被障碍阻挡时兜底）→ 归零速度并切换到下一状态。
        /// </summary>
        public override void PhysicsUpdate(double delta)
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
            {
                return;
            }

            _timer -= (float)delta;

            // 距离 + 时间驱动后撤：匀速移动（速度 = 距离 ÷ 时间），距离到或时间到即结束
            Enemy.Velocity = _dashDirection * _dashSpeed;
            Enemy.MoveAndSlide();

            bool distanceReached = Enemy.GlobalPosition.DistanceTo(_startPosition) >= DashDistance;
            if (distanceReached || _timer <= 0f)
            {
                Enemy.Velocity = Vector2.Zero;

                if (Enemy.StateMachine != null)
                {
                    string next = ResolveNextStateName();
                    if (Enemy.StateMachine.HasState(next))
                        Enemy.StateMachine.ChangeState(next);
                    else
                        Enemy.StateMachine.ChangeState("Walk");
                }
            }
        }

        /// <summary>
        /// 检测给定方向是否通畅，如果不通畅则尝试替代方向。
        /// 优先级：主方向 > 左前45° > 右前45° > 左90° > 右90°
        /// </summary>
        private Vector2 FindClearDirection(Vector2 preferredDirection)
        {
            if (preferredDirection == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            preferredDirection = preferredDirection.Normalized();

            // 需要尝试的方向列表（角度偏移）
            var directionsToTry = new[]
            {
                0f,      // 主方向
                -45f,    // 左前45°
                45f,     // 右前45°
                -90f,    // 左90°
                90f,     // 右90°
                180f,    // 后退180°
            };

            foreach (float angleDelta in directionsToTry)
            {
                Vector2 testDirection = preferredDirection.Rotated(Mathf.DegToRad(angleDelta));
                if (IsDirectionClear(testDirection))
                {
                    return testDirection;
                }
            }

            // 所有方向都有障碍，返回原方向（让游戏逻辑处理碰撞）
            return preferredDirection;
        }

        /// <summary>
        /// 使用射线检测判断给定方向是否通畅。
        /// 排除玩家身体：后撤路径上站着玩家（贴脸）不算障碍——否则替代方向可能选成 180°（朝玩家），
        /// 后撤变成冲向玩家露出破绽。
        /// </summary>
        private bool IsDirectionClear(Vector2 direction)
        {
            if (Enemy == null || direction == Vector2.Zero)
            {
                return false;
            }

            var query = PhysicsRayQueryParameters2D.Create(
                Enemy.GlobalPosition,
                Enemy.GlobalPosition + direction.Normalized() * RaycastDistance
            );

            // 排除自身和玩家的碰撞检测
            query.CollisionMask = Enemy.CollisionMask;
            var player = Enemy.PlayerTarget;
            if (player != null && GodotObject.IsInstanceValid(player))
                query.Exclude = new Godot.Collections.Array<Rid> { player.GetRid() };

            var result = Enemy.GetWorld2D().DirectSpaceState.IntersectRay(query);

            // 如果没有碰撞则返回 true（方向通畅）
            return result.Count == 0;
        }
    }
}
