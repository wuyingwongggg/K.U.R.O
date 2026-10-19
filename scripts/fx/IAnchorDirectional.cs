namespace Kuros.Fx
{
	/// <summary>
	/// 生成锚点朝向消费接口（能力接口，同 <see cref="IFollowAnchor"/>）：生成方把**所选锚点的局部旋转**
	/// （换算为度）注入给特效，特效自行决定怎么用（FanBeam 叠加到基准角度上，扇束朝向随锚点在编辑器里的摆放方向走）。
	/// 只取局部旋转、不读全局旋转——骨架下的锚点（SpineBoneAnchor）朝向来自骨骼矩阵，读全局会把骨骼姿态卷进来；
	/// 现有锚点全都没摆旋转 → 注入恒 0，对既有特效零影响。
	/// </summary>
	public interface IAnchorDirectional
	{
		/// <summary>锚点朝向偏移（度 = 所选锚点的局部旋转；无锚点时 0）。默认 0 = 不改变特效原有朝向。</summary>
		float AnchorAngleDegrees { get; set; }
	}
}
