using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies
{
	public partial class EnemyD1NetAdmin : SampleEnemy
	{
		private CollisionShape2D? _bodyShape;

		public override void _Ready()
		{
			base._Ready();
			ActiveImmunities |= ImmunityFlags.ForcedMovement;
			_bodyShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			bool hasFreeze = EffectController?.GetEffect<FreezeEffect>() != null;
			if (_bodyShape != null)
				_bodyShape.Disabled = !hasFreeze;

			// Hit/Frozen 期间 z_index 降低到 2，其余恢复 3
			string stateName = StateMachine?.CurrentState?.Name ?? string.Empty;
			bool lowered = stateName == "Hit" || stateName == "Frozen";
			ZIndex = lowered ? 2 : 3;
		}

		public override bool CanBeAffected(ActorEffect? effect)
		{
			// 眩晕期间（含 Hit 打断）允许所有效果
			if (EffectController?.GetEffect<FreezeEffect>() != null)
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
