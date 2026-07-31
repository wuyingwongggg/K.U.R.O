using Godot;
using Kuros.Core;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 暴乱护臂攻击 —— 基础近战攻击 + 前冲位移。
    ///
    /// 移动模式参照 PlayerDashState 的两段速度：
    ///   Warmup  → 不能移动（父类清零速度）
    ///   Active  → DashSpeed 高速前冲，ContactShape 碰到敌人时 DashSpeed 归零
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

        /// <summary>
        /// 接触检测用的 CollisionShape2D 路径。用于 IntersectShape 同步查询，
        /// 独立于 AttackArea 的判定框，可在编辑器中可视化编辑。
        /// </summary>
        [Export] public NodePath ContactShapePath = new();

        private bool _isDashing;
        private bool _isSliding;
        private bool _dashDisabled;
        private float _originalDashSpeed;
        private Vector2 _dashDirection;
        private CollisionShape2D? _contactShape;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _originalDashSpeed = DashSpeed;
        }

        /// <summary>
        /// Active 阶段开始 → 沿朝向高速前冲。
        /// </summary>
        protected override void OnActivePhase()
        {
            base.OnActivePhase();

            _dashDirection = Player.FacingRight ? Vector2.Right : Vector2.Left;

            _isDashing = true;
            _isSliding = false;
            Player.Velocity = _dashDirection * DashSpeed;
        }

        /// <summary>
        /// 每帧速度控制。用 IntersectShape 同步查询 ContactShape 是否碰到敌人，
        /// 命中则 DashSpeed 归零。
        /// </summary>
        protected override void OnTick(double delta)
        {
            base.OnTick(delta);

            if (!_dashDisabled)
            {
                var shape = ResolveContactShape();
                if (shape?.Shape != null)
                {
                    var spaceState = shape.GetWorld2D().DirectSpaceState;
                    var query = new PhysicsShapeQueryParameters2D
                    {
                        Shape = shape.Shape,
                        Transform = shape.GlobalTransform,
                        CollideWithAreas = true,
                        CollideWithBodies = false
                    };
                    foreach (var result in spaceState.IntersectShape(query))
                    {
                        if (!result.TryGetValue("collider", out var collider)) continue;
                        if (collider.As<GodotObject>() is not Area2D area) continue;
                        if ((string)area.Name != "HitArea") continue;
                        var actor = area.GetParent() as GameActor
                            ?? area.GetParent()?.GetParent() as GameActor;
                        if (actor != null
                            && actor.IsInGroup("enemies")
                            && !actor.IsDeathSequenceActive
                            && !actor.IsDead)
                        {
                            _dashDisabled = true;
                            DashSpeed = 0f;
                            _isDashing = false;
                            _isSliding = false;
                            Player.Velocity = Vector2.Zero;
                            break;
                        }
                    }
                }
            }

            if (_isDashing)
            {
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
            _dashDisabled = false;
            DashSpeed = _originalDashSpeed;
            Player.Velocity = Vector2.Zero;
            base.OnAttackFinished();
        }

        private CollisionShape2D? ResolveContactShape()
        {
            if (_contactShape != null && IsInstanceValid(_contactShape) && _contactShape.Shape != null)
                return _contactShape;

            if (ContactShapePath.IsEmpty)
                return null;

            _contactShape = GetNodeOrNull<CollisionShape2D>(ContactShapePath)
                ?? Player?.GetNodeOrNull<CollisionShape2D>(ContactShapePath);
            return _contactShape;
        }
    }
}
