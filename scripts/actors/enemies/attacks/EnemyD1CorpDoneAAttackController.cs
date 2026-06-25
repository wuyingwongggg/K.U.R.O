using Godot;
using System;

namespace Kuros.Actors.Enemies.Attacks
{
	public partial class EnemyD1CorpDoneAAttackController : EnemyAttackController
	{
		[Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
		[Export] public string PinballAttackName { get; set; } = "PinballAttack";

		public string CurrentAttackName { get; private set; } = string.Empty;

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
