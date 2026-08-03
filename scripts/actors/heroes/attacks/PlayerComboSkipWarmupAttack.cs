using Godot;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 连击跳过前摇：Recovery 阶段打断攻击时，新攻击直接进入 Active 阶段（伤害立即判定），跳过 Warmup。
    /// 继承 PlayerBasicMeleeAttack 的全部近战逻辑，仅重载阶段流。
    /// </summary>
    public partial class PlayerComboSkipWarmupAttack : PlayerBasicMeleeAttack
    {
        public override bool SkipWarmupOnRecoveryRestart => true;
    }
}
