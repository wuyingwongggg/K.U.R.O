using Godot;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 投掷预载（BuildThrow_B_006）：投掷蓄力——持物时按住攻击键蓄力,每蓄 1 秒伤害与投掷距离
    /// +25%/40%(层1/层2,**连续比例**),满 MaxChargeSeconds 秒自动投掷。
    /// 蓄力值由 PlayerThrowState 写入(ChargeSeconds),经 IThrowableModifiersContributor 在出手快照
    /// (GetThrowableModifiers)时结算:距离复用 DistanceScale、伤害 AttackPowerScale、
    /// 飞行时长 FlightTimeScale(距离增幅的 FlightTimeGain 比例,"弧线略增")。
    /// </summary>
    [GlobalClass]
    public partial class ThrowChargeEffect : ActorEffect, IThrowableModifiersContributor, IThrowChargeModifier
    {
        /// <summary>各层每秒加成百分比(B_006 注入 [25, 40])。</summary>
        [Export] public float[] TierValues { get; set; } = { 25f, 40f };

        /// <summary>蓄力上限(秒):达到即自动投掷。</summary>
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float MaxChargeSeconds { get; set; } = 3f;

        /// <summary>距离增幅转飞行时长的比例(0.5 = 距离 +100% 时飞行时长 +50%)。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float FlightTimeGain { get; set; } = 0.5f;

        private int _tier = 1;

        public bool Charging { get; set; }
        public float ChargeSeconds { get; set; }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public override void OnRemoved()
        {
            Charging = false;
            ChargeSeconds = 0f;
            base.OnRemoved();
        }

        public ThrowableModifiers ModifyThrowableModifiers(ThrowableModifiers mods)
        {
            if (ChargeSeconds <= 0f || TierValues.Length == 0) return mods;

            float perSecond = TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1] / 100f;
            float scale = 1f + ChargeSeconds * perSecond;
            // 乘算前按"未配置 = 1"规整:聚合链起点(None)的标量字段为 0(结构体默认值语义),
            // 消费方全部按 >0?:1 守卫——做乘法的贡献者必须先还原再乘,否则把 0 乘穿整条链
            float dist = mods.DistanceScale > 0f ? mods.DistanceScale : 1f;
            float atk = mods.AttackPowerScale > 0f ? mods.AttackPowerScale : 1f;
            float fly = mods.FlightTimeScale > 0f ? mods.FlightTimeScale : 1f;
            return mods with
            {
                DistanceScale = dist * scale,
                AttackPowerScale = atk * scale,
                FlightTimeScale = fly * (1f + (scale - 1f) * FlightTimeGain),
            };
        }
    }
}
