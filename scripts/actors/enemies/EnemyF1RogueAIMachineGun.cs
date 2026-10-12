using Godot;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// F1 机枪台（Enemy_F1_rogueAI_MachineGun）：机械桩固定炮位，与炮台同结构——锁定朝向、按玩家位置转身、
	/// 蓄力/开火/收招走 preheat/attack/recover，攻击期间沿轨维持间距。差异：攻击特效（连发弹幕）+ 贴脸近战招，
	/// 外加独有的"常态免伤、仅攻击窗口可受伤害"（见下）。
	/// 全部炮位逻辑在 <see cref="EnemyF1RogueAITurret"/> 里。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIMachineGun : EnemyF1RogueAITurret
	{
		/// <summary>常态免伤（同 rogueAI / 磁铁臂 / netAdmin 的"常态免伤"族，见 EFFECT_STANDARD 第八节）：
		/// 伤害与效果一律拒绝——**唯独 Attack 状态期间放行**：攻击窗口即唯一弱点窗口，玩家要打它只能趁它出招。
		/// 被放行/被拒的副作用与其他同族一致：自动选目标（追踪弹幕/浮游炮/光束截断）会跳过免疫中的它；
		/// Attack 期间被打进 Hit/Frozen 会离开 Attack 状态，弱点窗口随之关闭（想让它挨打时不被硬直，
		/// 给攻击配 GrantImmunities 霸体即可，那是招式的既有开关）。</summary>
		public override bool CanBeAffected(ActorEffect? effect)
			=> (StateMachine?.CurrentState?.Name ?? string.Empty) == "Attack";

		/// <summary>近战招（MeleeAttack）期间站桩：不沿轨保持间距、也不推滑槽升降——贴脸出招时不该边退边打。
		/// 扫射（SimpleMeleeAttack）不受影响，保持原"攻击期间沿桩退开"的设计。</summary>
		protected override bool ShouldSuppressSpacingForAttack()
			=> IsAttackRunning("MeleeAttack");
	}
}
