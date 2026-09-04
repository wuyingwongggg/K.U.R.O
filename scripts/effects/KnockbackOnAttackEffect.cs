using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Effects
{
    /// <summary>
    /// 攻击时击退目标的效果。
    /// 当携带此效果的角色攻击目标时，目标会被物理击退，自动与场景碰撞停止。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class KnockbackOnAttackEffect : ActorEffect
    {
        /// <summary>
        /// 击退持续时间（秒）
        /// </summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float KnockbackDuration { get; set; } = 0.3f;

        /// <summary>
        /// 击退距离（像素）
        /// </summary>
        [Export(PropertyHint.Range, "50,500,10")]
        public float KnockbackDistance { get; set; } = 150f;

        /// <summary>
        /// 触发击退的攻击段数（1-based）。0 表示所有段都触发
        /// </summary>
        [Export(PropertyHint.Range, "0,10,1")]
        public int TriggerHitStep { get; set; } = 1;

        [Export] public bool EnableKnockbackX { get; set; } = true;
        [Export] public bool EnableKnockbackY { get; set; } = true;

        private GameActor? _actor;

        protected override void OnApply()
        {
            base.OnApply();
            _actor = Actor;
            DamageEventBus.SubscribeWithSource(OnDamageResolved);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (attacker != _actor || target == null) return;

            if (TriggerHitStep > 0 && PlayerAttackTemplate.CurrentAttackHitStep != TriggerHitStep)
            {
                return;
            }

            if (target.ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement)) return;

            Vector2 direction = (target.GlobalPosition - attacker.GlobalPosition);
            if (!EnableKnockbackX) direction.X = 0f;
            if (!EnableKnockbackY) direction.Y = 0f;

            if (direction == Vector2.Zero)
            {
                direction = attacker.FacingRight ? Vector2.Right : Vector2.Left;
            }

            // 位移请求：受击方 Hit 状态在 KnockbackDuration 内匀减速滑完 KnockbackDistance
            // （与武器/技能击退同通道——受击动画随位移锁定，击退表现一致）
            target.ApplyKnockbackDisplacement(direction.Normalized(), KnockbackDistance, KnockbackDuration);
            // 注意：此处不可加 GD.Print/PrintS——反转瞬间大量敌人同时被击退时，
            // 每命中一行同步控制台输出会把主线程拖到 1s+（与伤害日志风暴同类问题）
        }
    }
}
