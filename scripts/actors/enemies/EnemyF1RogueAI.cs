using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// rogueAI 本体（Enemy_F1_rogueAI 专属脚本）：大型敌人的主体，挂在滑槽上沿轨道滑行。
	/// 移动交给 <see cref="RailChaseMovement"/> 组件（只沿机械轴向追击玩家 + 每帧硬钳在行程内）。
	/// 常态免伤——与磁铁机械臂（EnemyF1RogueAIMagnet）同款：伤害与效果一律拒绝
	/// （CanBeAffected 是所有效果的总闸，TakeDamage 也先过这道闸），血量只为控制台能识别与销毁
	/// （KillForced → 伤害被拒 → 基类兜底 QueueFree）。
	/// 攻击由场景里的 AttackController（EnemyF1RogueAIAttackController）驱动：
	/// 普攻 RogueAISlam 常驻、大招 RogueAIOverload 由关卡击杀进度阈值武装。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAI : SampleEnemy
	{
		public override void _Ready()
		{
			base._Ready();
			// 击退/黑洞吸附等强制位移推不动它（限位另有每帧硬钳兜底）
			ActiveImmunities |= ImmunityFlags.ForcedMovement;
		}

		/// <summary>常态免伤：伤害与效果一律拒绝（与 Enemy_F1_rogueAI_Magnet 同一套判定）。</summary>
		public override bool CanBeAffected(ActorEffect? effect) => false;
	}
}
