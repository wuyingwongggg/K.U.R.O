using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 生成仓破坏：先播自身的 AnimatedSprite2D（非循环），**动画播完后**从 SpawnScenes 里随机取一个场景生成到世界，
	/// 再按需销毁自身。用于"罐子被释放/打破后随机开出一个东西"这类一次性开箱效果。
	/// 注意：动画必须是**非循环**（loop = false），否则不会触发 animation_finished。
	/// </summary>
	[GlobalClass]
	public partial class EnemySpawnBoxDestroy : Node2D
	{
		[ExportCategory("Spawn 生成")]
		/// <summary>随机池：动画播完后随机取一个生成（空 = 只播动画，不生成任何东西）。</summary>
		[Export] public Godot.Collections.Array<PackedScene> SpawnScenes { get; set; } = new();
		/// <summary>生成位置锚点（可选）：用它的世界坐标作为生成点；空 = 用本节点自身位置。只影响**位置**。</summary>
		[Export] public NodePath SpawnMarkerPath { get; set; } = new();
		/// <summary>生成位置偏移（在锚点/自身位置上叠加）。</summary>
		[Export] public Vector2 SpawnOffset { get; set; } = Vector2.Zero;
		/// <summary>生成父级（**归属容器**，与位置无关；空 = 场景里的 World 节点 → 当前场景 → 本节点父级）。
		/// 注意：不要指向本节点自己的子节点——那样生成物会随本特效一起被销毁（脚本会自动兜底到世界节点并警告）。</summary>
		[Export] public NodePath SpawnParentPath { get; set; } = new();
		/// <summary>生成物是 GameActor 时套用的朝向。</summary>
		[Export] public bool FaceRight { get; set; }
		/// <summary>动画播完后延迟多久生成（秒；0 = 播完立即生成）。与 DelayDestroySelf 相互独立。</summary>
		[Export(PropertyHint.Range, "0,10,0.01")] public float DelaySpawnAfterAnimation { get; set; } = 0f;

		[ExportCategory("Animation 动画")]
		/// <summary>要播放的动画名（空 = 用 sprite 自己配置的 animation / autoplay）。</summary>
		[Export] public string AnimationName { get; set; } = string.Empty;
		/// <summary>AnimatedSprite2D 路径（相对本节点；场景结构为"容器 + 子 AnimatedSprite2D"）。</summary>
		[Export] public NodePath AnimatedSpritePath { get; set; } = new("AnimatedSprite2D");

		[ExportCategory("Lifecycle 生命周期")]
		/// <summary>生成后多久销毁自身（秒；0 = 立即销毁）。</summary>
		[Export(PropertyHint.Range, "0,10,0.01")] public float DelayDestroySelf { get; set; } = 0f;

		private AnimatedSprite2D? _sprite;
		private bool _spawned;

		public override void _Ready()
		{
			if (Engine.IsEditorHint()) return;

			if (!AnimatedSpritePath.IsEmpty)
				_sprite = GetNodeOrNull<AnimatedSprite2D>(AnimatedSpritePath);

			if (_sprite == null)
			{
				GD.PushWarning($"[{nameof(EnemySpawnBoxDestroy)}] 未找到 AnimatedSprite2D，直接生成。");
				OnAnimationFinished();
				return;
			}

			_sprite.AnimationFinished += OnAnimationFinished;
			if (!string.IsNullOrEmpty(AnimationName))
				_sprite.Play(AnimationName);
			else if (!_sprite.IsPlaying())
				_sprite.Play();
		}

		public override void _ExitTree()
		{
			if (_sprite != null && GodotObject.IsInstanceValid(_sprite))
				_sprite.AnimationFinished -= OnAnimationFinished;
		}

		private void OnAnimationFinished()
		{
			if (_spawned) return;
			_spawned = true;

			if (DelaySpawnAfterAnimation <= 0f)
			{
				DoSpawn();
				return;
			}

			var timer = GetTree().CreateTimer(DelaySpawnAfterAnimation);
			timer.Timeout += () =>
			{
				if (IsInstanceValid(this)) DoSpawn();
			};
		}

		private void DoSpawn()
		{
			PackedScene? scene = PickRandomScene();
			if (scene != null)
			{
				var instance = scene.Instantiate();
				if (instance != null)
				{
					// 先定位再入树：生成的敌人不该被本特效的销毁带走
					Vector2 origin = ResolveSpawnMarker()?.GlobalPosition ?? GlobalPosition;
					ResolveSpawnParent().AddChild(instance);

					if (instance is Node2D node2D)
						node2D.GlobalPosition = origin + SpawnOffset;
					if (instance is GameActor actor)
						actor.FlipFacing(FaceRight);

					string baseName = System.IO.Path.GetFileNameWithoutExtension(scene.ResourcePath);
					if (!string.IsNullOrEmpty(baseName))
						instance.Name = baseName;
				}
			}

			if (DelayDestroySelf <= 0f)
			{
				QueueFree();
				return;
			}

			var timer = GetTree().CreateTimer(DelayDestroySelf);
			timer.Timeout += () =>
			{
				if (IsInstanceValid(this)) QueueFree();
			};
		}

		/// <summary>生成位置锚点（可空）。</summary>
		private Node2D? ResolveSpawnMarker()
		{
			if (SpawnMarkerPath.IsEmpty) return null;
			return GetNodeOrNull<Node2D>(SpawnMarkerPath);
		}

		private PackedScene? PickRandomScene()
		{
			if (SpawnScenes == null || SpawnScenes.Count == 0) return null;
			int index = (int)GD.RandRange(0, SpawnScenes.Count - 1);
			return SpawnScenes[index];
		}

		private Node ResolveSpawnParent()
		{
			if (!SpawnParentPath.IsEmpty)
			{
				var customParent = GetNodeOrNull<Node>(SpawnParentPath);
				// 防呆：父级落在自身子树内 → 生成物会随本特效销毁，退回世界节点
				if (customParent != null && !IsSelfOrDescendant(customParent))
					return customParent;

				if (customParent != null)
					GD.PushWarning($"[{nameof(EnemySpawnBoxDestroy)}] SpawnParentPath 指向本节点自己的子节点（{SpawnParentPath}），" +
						"生成的场景会随特效一起被销毁——已改用世界节点。位置请用 SpawnMarkerPath。");
			}

			var worldNode = GetTree().CurrentScene?.GetNodeOrNull<Node>("World");
			return worldNode ?? GetTree().CurrentScene ?? GetParent();
		}

		private bool IsSelfOrDescendant(Node node)
		{
			for (Node? current = node; current != null; current = current.GetParent())
			{
				if (current == this) return true;
			}
			return false;
		}
	}
}
