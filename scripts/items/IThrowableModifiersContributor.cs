namespace Kuros.Items
{
    /// <summary>
    /// 投掷修饰贡献者（玩家身上的构筑效果实现，如 B_001 轻量化/B_002 重量化）：
    /// SamplePlayer.GetThrowableModifiers 聚合时逐效果叠加，投掷与轨迹预览单点消费。
    /// </summary>
    public interface IThrowableModifiersContributor
    {
        /// <summary>把本效果对投掷的修饰叠加到 mods 上（值语义，返回新实例）。</summary>
        ThrowableModifiers ModifyThrowableModifiers(ThrowableModifiers mods);
    }
}
