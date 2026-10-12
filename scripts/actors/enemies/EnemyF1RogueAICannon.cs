using Godot;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// F1 炮台（Enemy_F1_rogueAI_Cannon）：机械桩固定炮位，攻击特效是"蓄力能量球 → 直线能量弹"。
	/// 全部炮位逻辑（锁定朝向、转身门、镜像前向锚点、沿轨间距、拒绝位移）都在
	/// <see cref="EnemyF1RogueAITurret"/> 里，本类在"把场景与类型对上"之外只覆写一处：近战招期间站桩（见下）。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAICannon : EnemyF1RogueAITurret
	{
		/// <summary>近战招（MeleeAttack，与机枪台共用同一招）期间站桩：不沿轨保持间距、也不推滑槽升降——
		/// 贴脸出招时不该边退边打。开火（SimpleMeleeAttack）不受影响，保持原"攻击期间沿桩退开"设计。</summary>
		protected override bool ShouldSuppressSpacingForAttack()
			=> IsAttackRunning("MeleeAttack");
	}
}
