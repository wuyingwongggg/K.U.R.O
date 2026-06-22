using Godot;
using System;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// D1 打印机敌人的攻击控制器。
	/// 攻击切换逻辑：玩家在近战范围内 → SimpleMeleeAttack，否则 → Missile01Attack。
	/// </summary>
	public partial class EnemyD1PrinterMachineAttackController : EnemyAttackController
	{
		[Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
		[Export] public string Missile01AttackName { get; set; } = "Missile01Attack";

		public string CurrentAttackName { get; private set; } = string.Empty;

		private bool _playerInMeleeRange;

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);
			SyncWeightsToRange();
		}

		private void SyncWeightsToRange()
		{
			if (Enemy == null) return;

			bool inMelee = Enemy.IsPlayerInAttackRange();
			if (inMelee == _playerInMeleeRange) return;

			_playerInMeleeRange = inMelee;

			TrySetAttackWeight(MeleeAttackName, inMelee ? 100f : 0f);
			TrySetAttackWeight(Missile01AttackName, inMelee ? 0f : 100f);
		}

		protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
		{
			base.OnChildAttackStarted(attack);
			CurrentAttackName = attack.Name;
		}

		protected override void OnAttackFinished()
		{
			base.OnAttackFinished();
			CurrentAttackName = string.Empty;
		}
	}
}
