namespace Kuros.Items.World
{
    /// <summary>
    /// 分裂投掷预览提供者(BuildThrow_A_008 等实现):通用轨迹预览据此画出
    /// 主轨迹 + 各克隆轨迹及其落点。接口定义在 items 层,预览不依赖任何构筑卡。
    /// </summary>
    public interface IThrowSplitPreview
    {
        /// <summary>当前手持的投掷物是否会被分裂(握持态判定,如查家具槽件身份)。</summary>
        bool IsSplittableForHeldItem();

        /// <summary>主轨迹(原件)的落点纵向偏移(px,0 = 不改动)。</summary>
        float CenterLandingOffsetY { get; }

        /// <summary>克隆轨迹的落点纵向偏移列表(px,相对玩家行)。</summary>
        float[] CloneLandingOffsetsY { get; }
    }
}
