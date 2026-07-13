using Godot;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 暴乱护臂攻击 —— 基础近战攻击 + 前冲位移。
    ///
    /// 移动模式参照 PlayerDashState 的两段速度：
    ///   Warmup  → 不能移动（父类清零速度）
    ///   Active  → DashSpeed 高速前冲，撞到 Targetable 对象立即停止
    ///   Recovery → RecoverySpeed 低速滑行，可自然减速
    ///
    /// 继承 PlayerBasicMeleeAttack 的全部近战逻辑（伤害、动画、命中检测），仅追加位移行为。
    /// </summary>
    public partial class PlayerBrawlRiotBracerAttack : PlayerBasicMeleeAttack
    {
        /// <summary>Active 阶段前冲速度（像素/秒）。</summary>
        [Export(PropertyHint.Range, "100,8000,10")]
        public float DashSpeed = 4000f;

        /// <summary>Recovery 阶段滑行速度（像素/秒）。设为 0 则 Recovery 立即停止。</summary>
        [Export(PropertyHint.Range, "0,3000,10")]
        public float RecoverySpeed = 500f;

        private bool _isDashing;
        private bool _isSliding;
        private bool _hasHitTarget;
        private Vector2 _dashDirection;

        protected override void OnInitialized()
        {
            base.OnInitialized();
        }

        /// <summary>
        /// Active 阶段开始 → 沿朝向高速前冲。
        /// </summary>
        protected override void OnActivePhase()
        {
            base.OnActivePhase();

            _dashDirection = Player.FacingRight ? Vector2.Right : Vector2.Left;
            _hasHitTarget = false;

            _isDashing = true;
            _isSliding = false;
            Player.Velocity = _dashDirection * DashSpeed;
        }

        /// <summary>
        /// 每帧速度控制（参照 PlayerDashState.PhysicsUpdate 每帧赋值模式）。
        /// 前冲期间检测 AttackArea 内是否有 Targetable 对象，命中则立即停冲。
        /// </summary>
        protected override void OnTick(double delta)
        {
            base.OnTick(delta);

            if (_isDashing)
            {
                // 前冲期间检测是否撞到 Targetable 对象（参照 EnemyOnePunchAttack 的 OverlapsBody 检测）
                if (!_hasHitTarget && AttackArea != null && AttackArea.GetOverlappingBodies().Count > 0)
                {
                    _hasHitTarget = true;
                    _isDashing = false;
                    _isSliding = RecoverySpeed > 0f;
                    Player.Velocity = _isSliding ? _dashDirection * RecoverySpeed : Vector2.Zero;
                    return;
                }

                Player.Velocity = _dashDirection * DashSpeed;
            }
            else if (_isSliding)
            {
                Player.Velocity = _dashDirection * RecoverySpeed;
            }
            else if (IsInRecovery)
            {
                Player.Velocity = Vector2.Zero;
            }
        }

        /// <summary>
        /// Recovery 阶段 → 切换标志；速度由 OnTick 每帧接管。
        /// </summary>
        protected override void OnRecoveryStarted()
        {
            _isDashing = false;
            _isSliding = RecoverySpeed > 0f;
        }

        protected override void OnAttackFinished()
        {
            _isDashing = false;
            _isSliding = false;
            _hasHitTarget = false;
            Player.Velocity = Vector2.Zero;
            base.OnAttackFinished();
        }
    }
}
