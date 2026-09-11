namespace Kuros.Items
{
    /// <summary>
    /// 投掷蓄力口（B_006 投掷预载）:PlayerThrowState 蓄力窗口驱动其 Charging/ChargeSeconds——
    /// 出手快照(GetThrowableModifiers)按 ChargeSeconds 结算加成(贡献门槛必须用 ChargeSeconds>0,
    /// 出手时 Charging 已置 false);轨迹预览按 Charging 在 Throw 状态放行显示。
    /// </summary>
    public interface IThrowChargeModifier
    {
        /// <summary>蓄力窗口进行中(仅状态/动画/预览门槛;出手快照时已为 false——加成计算勿以它为准)。</summary>
        bool Charging { get; set; }

        /// <summary>蓄力上限(秒):达到即自动投掷。</summary>
        float MaxChargeSeconds { get; }

        /// <summary>当前蓄力秒数(Throw 状态写入,出手快照/贡献读取;离场归零)。</summary>
        float ChargeSeconds { get; set; }
    }
}
