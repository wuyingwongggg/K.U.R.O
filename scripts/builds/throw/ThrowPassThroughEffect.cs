using Godot;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 延迟销毁（BuildThrow_B_008）：投掷一次性道具后,道具不再在命中敌人时停留销毁,
    /// 穿透敌人继续飞行,只在落点走正常销毁流程(沿途仍逐敌结算伤害,落点砸地结算保留)。
    /// 实现 = 出手快照修饰(PassThroughEnemies),由实体命中敌人的 StopOnHit 分支消费;
    /// 仅一次性道具(实体侧以 !IsThrowWeapon 门控,投掷武器不受影响);墙壁/障碍物照常停止。
    /// </summary>
    [GlobalClass]
    public partial class ThrowPassThroughEffect : ActorEffect, IThrowableModifiersContributor
    {
        public ThrowableModifiers ModifyThrowableModifiers(ThrowableModifiers mods)
            => mods with { PassThroughEnemies = true };
    }
}
