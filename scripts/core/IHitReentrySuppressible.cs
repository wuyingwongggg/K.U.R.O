namespace Kuros.Core
{
    /// <summary>
    /// Hit 重入打断管理（连击保护）：Hit 硬直中再次受击允许**有限次**完整打断（重播后仰，
    /// 保持连续打击的反馈感），超过上限后抑制重入——当前硬直自然走完，目标得以脱出（防无限屈死）。
    /// 击退豁免：被抑制的命中若带击退（位移请求在伤害事件后写入），Hit 状态消费到新请求时
    /// 补执行完整重入（延迟一物理帧），带击退的打断不受上限限制。
    /// </summary>
    public interface IHitReentrySuppressible
    {
        /// <summary>一次重入尝试（GameActor 在 Hit 中再次受击时调用）：打断计数 +1。
        /// 返回 true = 允许本次重入（未超上限）；false = 抑制（不重入，当前硬直继续走完）。</summary>
        bool OnReentryAttempted();

        /// <summary>重入被抑制后由 GameActor 调用（置标记，供消费到新位移请求时补重置——击退豁免）。</summary>
        void NotifyReentrySuppressed();
    }
}
