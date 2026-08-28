using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 攻击来源提供者：由生成方（EnemyAttackTemplate 等）显式设置攻击者。
	/// 显式传递优于父节点猜测（父下第一个敌人不一定是发射者，会导致 AllowSelfDamage 保护失效）。
	/// </summary>
	public interface IAttackerProvider
	{
		GameActor? Attacker { get; set; }
	}
}
