using Godot;
using Kuros.Core;
using Kuros.Environments;

namespace Kuros.Fx
{
	/// <summary>
	/// 生成仓破坏：先播自身的 AnimatedSprite2D（非循环），**动画播完后**从敌人池 / 道具池里随机取一个场景生成到世界，
	/// 再按需销毁自身。用于"罐子被释放/打破后随机开出一个东西"这类一次性开箱效果。
	/// 注意：动画必须是**非循环**（loop = false），否则不会触发 animation_finished。
	///
	/// **生成配额**（对抗"源源不断开敌堆叠"）：内容**池优先**掷骰——敌人概率 =
	/// <see cref="EnemyPoolProbability"/> × clamp(1 − 存活 / 剩余需求, 0, 1)^<see cref="EnemyProbabilitySteepness"/>，
	/// **clamp 归零点即"存活 + 已击杀 = 当前段需求数"的硬上限**（陡峭度只改逼近过程、不动零点；
	/// 敌人被非死亡方式清掉会自动回归概率，自愈）；中则敌人池内均匀，否则道具池内均匀。
	/// **家具在场上限**：生成家具前复查场上家具 &lt; 投送机械的 MaxStagedFurniture（组查找），满位 → 空箱。
	/// 生成物入追踪组（敌人 <see cref="EnemyTrackGroup"/> / 家具 <see cref="FurnitureTrackGroup"/>）——
	/// 磁铁的投掷闸门（EnemyMagnetJarAttack）与"两轴皆满提前收工"（EnemyF1RogueAIMagnet）都读这两组。
	/// 找不到 <see cref="KillProgressAscendController"/> / 未配分段 / 需求数 ≤ 0 → 概率用导出值、无上限（其他关卡行为不变）。
	/// </summary>
	[GlobalClass]
	public partial class EnemySpawnBoxDestroy : Node2D
	{
		/// <summary>本体系（开箱）生成的敌人所在的追踪组：存活计数即取此组（销毁自动出组，无簿记）。
		/// 只统计开箱敌人——磁铁/本体这类不走 FinalizeDeath 的常驻敌人不在此组，不占配额。</summary>
		public const string EnemyTrackGroup = "spawn_box_enemy";

		/// <summary>本体系（开箱）生成的家具所在的追踪组：存活计数用（磁铁的"家具在场上限 F"与投掷闸门）。
		/// 被玩家搬走/投掷/摧毁后自动出组 → 场上腾位，补给恢复。</summary>
		public const string FurnitureTrackGroup = "spawn_box_prop";

		[ExportCategory("Spawn 生成")]
		/// <summary>敌人池：掷中敌人池时从中均匀抽一个；受击杀配额限制（存活 + 已击杀 ≥ 需求数后概率归零）。</summary>
		[Export] public Godot.Collections.Array<PackedScene> EnemyScenes { get; set; } = new();
		/// <summary>道具池：掷中道具池（或敌人池被限）时从中均匀抽一个；场上到达上限 F 时不再产出（空箱）。</summary>
		[Export] public Godot.Collections.Array<PackedScene> PropScenes { get; set; } = new();
		/// <summary>敌人池基础概率（0..1，池优先掷骰）：开局概率 = 本值，实际概率 = 本值 × clamp(1 − 存活/剩余需求, 0, 1)^陡峭度。
		/// 场上越挤越低、存活 + 已击杀 = 需求数时归零（= 硬上限）。无控制器/未配分段时直接用本值。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float EnemyPoolProbability { get; set; } = 0.5f;
		/// <summary>衰减陡峭度（指数）：1 = 线性；越大越陡。如 8（基础 0.7、需求 20）：4 个敌人时敌人概率 ≈12%、
		/// 5 个 ≈7%（家具 88~93%，"越挤越先给工具"）。归零点不受影响（仍在存活 = 剩余需求的硬闸上）。</summary>
		[Export(PropertyHint.Range, "1,16,0.5")] public float EnemyProbabilitySteepness { get; set; } = 8f;
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
			PackedScene? scene = PickRandomScene(out bool isEnemy);
			if (scene != null)
			{
				var instance = scene.Instantiate();
				if (instance != null)
				{
					// 先定位再入树：生成的敌人不该被本特效的销毁带走
					Vector2 origin = ResolveSpawnMarker()?.GlobalPosition ?? GlobalPosition;
					ResolveSpawnParent().AddChild(instance);
					// 入追踪组：存活计数用（敌人配额 / 家具在场上限）；被消耗/销毁自动出组
					instance.AddToGroup(isEnemy ? EnemyTrackGroup : FurnitureTrackGroup);

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

		/// <summary>随机取一个场景：**池优先**——先按敌人概率掷池，中则敌人池内均匀，否则道具池内均匀。
		/// isEnemy 标记抽中来源（用于入对应追踪组）。两池都没配返回 null（只播动画）；
		/// 掷中家具但场上已到上限 F 时也返回 null（空箱，**不回落敌人**——保住"越挤越给工具"的衰减节奏）。</summary>
		private PackedScene? PickRandomScene(out bool isEnemy)
		{
			isEnemy = false;
			if (EnemyScenes.Count == 0 && PropScenes.Count == 0) return null;

			float enemyProbability = ResolveEnemyProbability();
			bool enemyAvailable = EnemyScenes.Count > 0 && enemyProbability > 0f;
			bool propConfigured = PropScenes.Count > 0;

			bool pickEnemy = enemyAvailable && (!propConfigured || GD.Randf() < enemyProbability);
			if (pickEnemy)
			{
				isEnemy = true;
				return EnemyScenes[(int)GD.RandRange(0, EnemyScenes.Count - 1)];
			}
			if (!propConfigured) return null;

			// 家具在场上限复查（只有掷中家具才查）：满了 → 空箱
			if (GetTree().GetNodesInGroup(FurnitureTrackGroup).Count >= ResolveFurnitureStagingMax()) return null;
			return PropScenes[(int)GD.RandRange(0, PropScenes.Count - 1)];
		}

		/// <summary>本罐的敌人池概率 = 导出概率 × clamp(1 − 存活/剩余需求, 0, 1)^陡峭度（剩余需求 = 需求数 − 已击杀；
		/// ≤ 0 直接 0，防除零并覆盖到站后的超额击杀窗口）。**clamp 归零点 = "存活 + 已击杀 = 需求数"的硬上限**
		/// ——两条规则合一，不另写上限判断；指数只改变逼近上限的过程（越陡越早让位给家具），不动零点；
		/// 敌人被非死亡方式清掉（存活下降）自动回归正概率（自愈）。
		/// 找不到控制器 / 未配分段 / 需求数 ≤ 0 → 不用配额，返回导出值（旧行为）。</summary>
		private float ResolveEnemyProbability()
		{
			var progress = KillProgressAscendController.Find(this);
			var phase = progress?.CurrentPhase;
			if (progress == null || phase == null || phase.TotalKillsForFull <= 0) return EnemyPoolProbability;

			int remaining = phase.TotalKillsForFull - progress.KillCount;
			if (remaining <= 0) return 0f;
			int alive = GetTree().GetNodesInGroup(EnemyTrackGroup).Count;
			float decay = Mathf.Clamp(1f - (float)alive / remaining, 0f, 1f);
			return EnemyPoolProbability * Mathf.Pow(decay, EnemyProbabilitySteepness);
		}

		/// <summary>家具在场上限 F：取投送机械（组查找——两臂是同一场景实例、天然同值）。
		/// 找不到机械（例如别的关卡单独用这个箱子）→ 不做家具上限。</summary>
		private int ResolveFurnitureStagingMax()
		{
			var deliverer = GetTree()?.GetFirstNodeInGroup(Kuros.Actors.Enemies.EnemyF1RogueAIMagnet.DeliveryGroup)
				as Kuros.Actors.Enemies.EnemyF1RogueAIMagnet;
			return deliverer?.MaxStagedFurniture ?? int.MaxValue;
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
