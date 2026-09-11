namespace Kuros.Fx
{
    /// <summary>
    /// 投掷飞行距离注入：生成方（投掷实体按 ItemDefinition + 出手快照修饰解析）显式告知
    /// "投掷即效果"类特效（回旋镖等）本次投掷的实际飞行距离（px）——
    /// 特效据此派生飞行参数，与 ThrowTrajectoryPreview 共用同一数值真源，避免特效自持距离产生双真源。
    /// </summary>
    public interface IThrowFlightDistance
    {
        /// <summary>设置本次投掷的飞行距离（px）。未调用 = 特效使用自身导出参数。</summary>
        void SetThrowFlightDistance(float distance);
    }
}
