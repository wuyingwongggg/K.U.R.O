using Godot;

namespace Kuros.Actors.Enemies
{
    /// <summary>
    /// 敌人战斗行为配置（Resource），挂载到 SampleEnemy 的 BehaviorConfig 字段上，
    /// 控制敌人在战斗中的站位策略和距离参数。
    /// </summary>
    [GlobalClass]
    public partial class EnemyBehaviorConfig : Resource
    {
        /// <summary>
        /// 战斗站位策略
        /// </summary>
        public enum PositioningStrategy
        {
            CloseIn,      // 贴脸近战，始终靠近玩家
            KeepDistance, // 保持距离，玩家靠近时后退
            Adaptive      // 动态调整：有远程攻击可用时保持距离，否则贴脸
        }

        [ExportCategory("Positioning")]
        [Export]
        public PositioningStrategy Positioning = PositioningStrategy.CloseIn;

        /// <summary>
        /// 与玩家之间的最小舒适距离。玩家突破此距离时触发后撤。
        /// 仅 KeepDistance / Adaptive 模式生效。
        /// </summary>
        [Export(PropertyHint.Range, "0,5000,10")]
        public float MinComfortDistance = 120f;

        /// <summary>
        /// 后撤时要退到的目标距离。设为 0 则使用 MinComfortDistance 的值。
        /// 仅 KeepDistance / Adaptive 模式生效。
        /// </summary>
        [Export(PropertyHint.Range, "0,5000,10")]
        public float FleeTargetDistance = 0f;

        /// <summary>
        /// 后撤时的速度倍率。
        /// </summary>
        [Export(PropertyHint.Range, "0.5,10,0.1")]
        public float FleeSpeedMultiplier = 1.5f;

        /// <summary>
        /// 后撤时是否免疫伤害。
        /// </summary>
        [Export]
        public bool FleeDamageImmune = false;

        /// <summary>
        /// 获取实际后撤目标距离。
        /// </summary>
        public float EffectiveFleeTargetDistance =>
            FleeTargetDistance > 0f ? FleeTargetDistance : MinComfortDistance;
    }
}
