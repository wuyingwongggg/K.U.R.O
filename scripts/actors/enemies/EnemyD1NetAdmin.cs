using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Controllers;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies
{
	public partial class EnemyD1NetAdmin : SampleEnemy
	{
		protected override float GetFrozenRemainingTime()
		{
			if (StateMachine?.CurrentState is EnemyNetAdminFrozenState netAdminFrozen)
				return netAdminFrozen.GetRemainingTime();
			return base.GetFrozenRemainingTime();
		}
	}
}
