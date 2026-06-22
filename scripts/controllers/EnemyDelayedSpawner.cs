using Godot;
using Kuros.Core;

namespace Kuros.Controllers
{
	/// <summary>
	/// 延时后生成敌人，可选延时删除自身。
	/// </summary>
	[GlobalClass]
	public partial class EnemyDelayedSpawner : Node2D
	{
		[Export] public PackedScene EnemyScene { get; set; } = null!;
		[Export(PropertyHint.Range, "0,60,0.01")] public float DelayEnemySpawn = 0f;
		[Export(PropertyHint.Range, "0,60,0.01")] public float DelayDestroySelf = 0f;
		[Export] public Vector2 SpawnOffset = Vector2.Zero;
		[Export] public NodePath SpawnParentPath { get; set; } = new();
		[Export] public bool FaceRight = false;
		[Export] public NodePath DestroyTargetPath { get; set; } = new();
		[Export] public Area2D? TriggerArea { get; set; }
		[Export(PropertyHint.Range, "0,100,1")] public float SpawnChance = 100f;

		private Node? _destroyTarget;
		private bool _triggered;
		private bool _hasSpawned;
		private RandomNumberGenerator _rng = new();

		public override void _Ready()
		{
			if (Engine.IsEditorHint()) return;

			if (EnemyScene == null)
			{
				GD.PushWarning($"[{nameof(EnemyDelayedSpawner)}] 未设置 EnemyScene。");
				return;
			}

			_destroyTarget = ResolveDestroyTarget();
			_rng.Randomize();

			if (TriggerArea != null)
			{
				TriggerArea.AreaEntered += OnTriggerAreaEntered;
			}
			else
			{
				if (DelayEnemySpawn <= 0f)
					CallDeferred("DoSpawnDeferred");
				else
				{
					var timer = GetTree().CreateTimer(DelayEnemySpawn);
					timer.Timeout += DoSpawnDeferred;
				}
			}
		}

		private void DoSpawnDeferred()
		{
			if (!IsInstanceValid(this)) return;
			if (_hasSpawned) return;
			_hasSpawned = true;

			var instance = EnemyScene.Instantiate();
			if (instance == null) return;

			var parent = ResolveSpawnParent();
			parent.AddChild(instance);

			if (instance is Node2D node2D)
				node2D.GlobalPosition = GlobalPosition + SpawnOffset;

			if (instance is GameActor actor)
				actor.FlipFacing(FaceRight);

			if (instance is Node node)
				node.Name = System.IO.Path.GetFileNameWithoutExtension(EnemyScene.ResourcePath);

			if (DelayDestroySelf > 0f)
			{
				var destroyTimer = GetTree().CreateTimer(DelayDestroySelf);
				destroyTimer.Timeout += () =>
				{
					var target = _destroyTarget;
					if (IsInstanceValid(target))
						target!.QueueFree();
				};
			}
		}

		private void OnTriggerAreaEntered(Area2D area)
		{
			if (_triggered) return;
			if (TriggerArea == null) return;

			_triggered = true;
			TriggerArea.AreaEntered -= OnTriggerAreaEntered;

			var parent = area.GetParent();
			if (parent == null || !parent.IsInGroup("player")) return;

			if (_rng.Randf() * 100f >= SpawnChance) return;

			if (DelayEnemySpawn <= 0f)
				DoSpawnDeferred();
			else
			{
				var timer = GetTree().CreateTimer(DelayEnemySpawn);
				timer.Timeout += DoSpawnDeferred;
			}
		}

		private Node? ResolveDestroyTarget()
		{
			if (!DestroyTargetPath.IsEmpty)
			{
				var target = GetNodeOrNull<Node>(DestroyTargetPath);
				if (target != null) return target;
			}
			return this;
		}

		private Node ResolveSpawnParent()
		{
			if (!SpawnParentPath.IsEmpty)
			{
				var customParent = GetNodeOrNull<Node>(SpawnParentPath);
				if (customParent != null) return customParent;
			}

			var worldNode = GetTree().CurrentScene?.GetNodeOrNull<Node>("World");
			return worldNode ?? GetTree().CurrentScene ?? GetParent();
		}
	}
}
