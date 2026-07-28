using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies
{
	public partial class EnemyD1NetAdmin : SampleEnemy
	{
		public override void _Ready()
		{
			base._Ready();
			ActiveImmunities |= ImmunityFlags.ForcedMovement;
		}

		public override bool CanBeAffected(ActorEffect? effect)
		{
			// 眩晕状态允许所有效果
			if (StateMachine?.CurrentState?.Name == "Frozen")
				return true;

			// 非眩晕状态仅允许无人机眩晕效果
			if (effect is FreezeEffect freeze
				&& freeze.EffectId?.StartsWith("drone_stun_") == true)
				return true;

			return false;
		}

		protected override float GetFrozenRemainingTime()
		{
			if (StateMachine?.CurrentState is EnemyNetAdminFrozenState netAdminFrozen)
				return netAdminFrozen.GetRemainingTime();
			return base.GetFrozenRemainingTime();
		}
	}
}
