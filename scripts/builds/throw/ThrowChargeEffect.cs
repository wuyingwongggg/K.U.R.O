using Godot;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 投掷预载（BuildThrow_B_006）：投掷蓄力——持物时按住攻击键蓄力,每蓄 1 秒投掷伤害**固定** +20/+30(层1/层2)、
    /// 投掷距离与击退 +25%/40%(连续比例),满 MaxChargeSeconds 秒自动投掷。
    /// 伤害用固定加值而非倍率:档位基础伤害 20/60/100(1×/3×/5×),倍率会按档位把收益差放大(+120% 时 +24/+72/+120),
    /// 固定值让各档收益一致(满蓄 +60/+90),也避免与 B_002 重量化/A_006 数据升级的档位类加成叠乘失控;
    /// 距离/击退仍用比例——距离档是反序(轻 700/重 300),固定值会抵消"重物近、轻物远"的档位身份。
    /// 蓄力值由 PlayerThrowState 写入(ChargeSeconds),经 IThrowableModifiersContributor 在出手快照
    /// (GetThrowableModifiers)时结算:伤害 AttackPowerFlat、距离 DistanceScale、击退
    /// KnockbackDistanceScale/KnockbackSpeedScale、飞行与击退初速走同一 TimeGainRatio 比例("弧线略增")。
    /// </summary>
    [GlobalClass]
    public partial class ThrowChargeEffect : ActorEffect, IThrowableModifiersContributor, IThrowChargeModifier
    {
        /// <summary>各层每秒**固定伤害**加值(B_006 注入 [20,30])。</summary>
        [Export] public float[] TierValues { get; set; } = { 20f, 30f };

        /// <summary>各层每秒距离提升百分比(B_006 注入 [25,40])。</summary>
        [Export] public float[] DistanceIncreasePercents { get; set; } = { 25f, 40f };

        /// <summary>蓄力上限(秒):达到即自动投掷。</summary>
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float MaxChargeSeconds { get; set; } = 3f;

        /// <summary>距离增幅转时长的比例(0.5 = 距离 +100% 时飞行/击退时长 +50%)——
        /// 飞行时长与击退时长共用同一比例(位移变远的同时略变快)。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float TimeGainRatio { get; set; } = 0.5f;

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

            int idx = Mathf.Clamp(_tier, 1, TierValues.Length) - 1;
            float damageFlat = ChargeSeconds * TierValues[idx];

            float distPercent = DistanceIncreasePercents.Length > 0
                ? DistanceIncreasePercents[Mathf.Clamp(_tier, 1, DistanceIncreasePercents.Length) - 1]
                : 0f;
            float distScale = 1f + ChargeSeconds * distPercent / 100f;
            float timeScale = 1f + (distScale - 1f) * TimeGainRatio;

            // 乘算前按"未配置 = 1"规整:聚合链起点(None)的标量字段为 0(结构体默认值语义),
            // 消费方全部按 >0?:1 守卫——做乘法的贡献者必须先还原再乘,否则把 0 乘穿整条链
            // (AttackPowerFlat 是加法,0 = 无贡献,无需守卫)
            float dist = mods.DistanceScale > 0f ? mods.DistanceScale : 1f;
            float fly = mods.FlightTimeScale > 0f ? mods.FlightTimeScale : 1f;
            float kbDist = mods.KnockbackDistanceScale > 0f ? mods.KnockbackDistanceScale : 1f;
            float kbSpeed = mods.KnockbackSpeedScale > 0f ? mods.KnockbackSpeedScale : 1f;
            return mods with
            {
                DistanceScale = dist * distScale,
                AttackPowerFlat = mods.AttackPowerFlat + damageFlat,
                FlightTimeScale = fly * timeScale,
                KnockbackDistanceScale = kbDist * distScale,
                KnockbackSpeedScale = kbSpeed * timeScale,
            };
        }
    }
}
