using System;
using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
	public partial class EffectAutoDestroy : AnimatedSprite2D
	{
		public bool FacingRight { get; set; } = true;
		public GameActor? Attacker { get; set; }

		[Export] public PackedScene[] SpawnOnDestroyScenes { get; set; } = Array.Empty<PackedScene>();
		[Export] public float DestroyDelay { get; set; } = 0f;
		[Export] public bool QueueFreeOwner { get; set; } = false;

		public override void _Ready()
		{
			if (!FacingRight)
				Scale = new Vector2(-Scale.X, Scale.Y);

			if (!IsPlaying())
				Play();

			if (DestroyDelay > 0f)
				GetTree().CreateTimer(DestroyDelay).Timeout += SpawnAndDestroy;
			else
				AnimationFinished += OnAnimationFinished;
		}

		private void OnAnimationFinished()
		{
			AnimationFinished -= OnAnimationFinished;
			SpawnAndDestroy();
		}

		private void SpawnAndDestroy()
		{
			Node? spawnParent = QueueFreeOwner
				? Owner?.GetParent() ?? GetParent()
				: GetParent();

			Vector2 spawnPos = GlobalPosition;

			foreach (var scene in SpawnOnDestroyScenes)
			{
				if (scene == null) continue;
				var fx = scene.Instantiate<Node2D>();
				spawnParent?.AddChild(fx);
				fx.GlobalPosition = spawnPos;

				// Pass attacker to child effects
				if (fx is BoomDmgEffect boom)
					boom.Attacker = Attacker;
				else if (fx is LaserBeamPlayerWeapon laser)
					laser.Attacker = Attacker;
			}

			Node nodeToFree = QueueFreeOwner ? (Owner ?? (Node)this) : this;
			nodeToFree.QueueFree();
		}
	}
}
