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

		/// <summary>
		/// 环境强制击杀：非眩晕时免疫一切伤害（CanBeAffected 拒绝）——
		/// 先注入无人机眩晕（drone_stun_ 前缀，CanBeAffected 放行）再伤害，
		/// 走正常死亡流程（飘字 + 死亡动画）；仍失败才直接销毁。
		/// </summary>
		public override void KillForced()
		{
			if (DeliverKillDamage()) return;

			ApplyEffect(new FreezeEffect
			{
				EffectId = "drone_stun_env_clear",
				DisplayName = "清场眩晕",
				Duration = 0.1f,
			});

			if (!DeliverKillDamage() && GodotObject.IsInstanceValid(this))
				QueueFree();
		}

		protected override float GetFrozenRemainingTime()
		{
			if (StateMachine?.CurrentState is EnemyNetAdminFrozenState netAdminFrozen)
				return netAdminFrozen.GetRemainingTime();
			return base.GetFrozenRemainingTime();
		}
	}
}
