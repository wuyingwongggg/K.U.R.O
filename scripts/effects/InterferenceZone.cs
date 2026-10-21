using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Effects
{
	/// <summary>
	/// 小型干扰区（世界空间危险区）：玩家进入 → 结算一次伤害，区域随后快速淡出。
	/// 视觉由 Visuals 下**多个独立视觉层叠加**（不同 seed/尺度/偏移/自转/伪 3D 倾角），
	/// 每层只绘制随机激活的小方块，方块内采样屏幕纹理做局部错位扭曲；方块外透明。
	/// 碰撞与伤害判定仍是单个圆形 Area2D。
	/// 目标发现用 Area2D 信号（EFFECT_STANDARD 模式 B），伤害走 DamageDispatcher；
	/// 攻击来源由生成方（InterferenceHazardField）通过 <see cref="IAttackerProvider"/> 注入。
	/// </summary>
	[GlobalClass]
	public partial class InterferenceZone : Node2D, IAttackerProvider
	{
		[ExportCategory("Range 范围")]
		[Export(PropertyHint.Range, "40,800,5")] public float Radius { get; set; } = 150f;
		/// <summary>矩形宽高比（相对 Radius 的半宽/半高倍率）。默认 1 : 0.72。</summary>
		[Export] public Vector2 Aspect { get; set; } = new(1f, 0.72f);
		/// <summary>目标发现物理层（默认 Player 层 = 4）。</summary>
		[Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask { get; set; } = 4;
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.Player;
		[Export] public bool AllowSelfDamage { get; set; } = false;

		[ExportCategory("Damage 伤害")]
		[Export(PropertyHint.Range, "0,200,1")] public int Damage { get; set; } = 20;

		[ExportCategory("Lifecycle 生命周期")]
		[Export(PropertyHint.Range, "0.2,10,0.1")] public float Lifetime { get; set; } = 4f;
		[Export(PropertyHint.Range, "0,1,0.05")] public float FadeInDuration { get; set; } = 0.15f;
		[Export(PropertyHint.Range, "0,1,0.05")] public float FadeOutDuration { get; set; } = 0.4f;
		/// <summary>被踩中后剩余的淡出时间（小于上面两项时取更短）。</summary>
		[Export(PropertyHint.Range, "0.05,1,0.05")] public float TriggerFadeDuration { get; set; } = 0.35f;

		/// <summary>攻击来源（由生成方在 AddChild 前后注入，随场景模板链路）。</summary>
		public GameActor? Attacker { get; set; }

		/// <summary>Visuals 下的单个视觉层：场景里摆好的基准变换，运行时按 Radius 缩放并做相位动画。</summary>
		private sealed class VisualLayer
		{
			public MeshInstance2D Node = null!;
			public ShaderMaterial Material = null!;
			public Vector2 BaseScale;
			public Vector2 BasePosition;
			public float BaseRotation;
			public float Weight;
		}

		private readonly List<VisualLayer> _layers = new();
		private Area2D? _area;
		private CollisionShape2D? _shape;
		private float _elapsed;
		private float _remaining;
		private bool _triggered;

		public override void _Ready()
		{
			_area = GetNodeOrNull<Area2D>("Area2D");
			_shape = _area?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");

			if (_area == null || _shape?.Shape is not RectangleShape2D)
			{
				GD.PushWarning($"[{nameof(InterferenceZone)}] 缺少 Area2D/RectangleShape2D，区域自毁");
				QueueFree();
				return;
			}

			// 每个实例独立的矩形形状（场景子资源默认共享）
			var rect = (RectangleShape2D)_shape.Shape.Duplicate();
			Vector2 aspect = new(Mathf.Max(Aspect.X, 0.05f), Mathf.Max(Aspect.Y, 0.05f));
			rect.Size = new Vector2(2f * Mathf.Max(Radius, 1f) * aspect.X,
				2f * Mathf.Max(Radius, 1f) * aspect.Y);
			_shape.Shape = rect;

			_area.CollisionLayer = 0;
			_area.CollisionMask = TargetCollisionMask;
			_area.Monitorable = false;
			_area.Monitoring = true;
			_area.BodyEntered += OnBodyEntered;

			CollectVisualLayers();

			_remaining = Mathf.Max(Lifetime, 0.1f);
			UpdateLayers(0f);
		}

		public override void _Process(double delta)
		{
			_remaining -= (float)delta;
			if (_remaining <= 0f)
			{
				QueueFree();
				return;
			}

			_elapsed += (float)delta;

			float strength = 1f;
			if (FadeInDuration > 0.001f)
				strength = Mathf.Min(strength, _elapsed / FadeInDuration);
			if (FadeOutDuration > 0.001f)
				strength = Mathf.Min(strength, _remaining / FadeOutDuration);
			UpdateLayers(Mathf.Clamp(strength, 0f, 1f));
		}

		/// <summary>收集 Visuals 下的 MeshInstance2D 层：各自独立材质（不同 seed/参数），
		/// 场景里写的 scale/position/rotation 作为基准（position 以 Radius 为单位）。</summary>
		private void CollectVisualLayers()
		{
			var visuals = GetNodeOrNull<Node2D>("Visuals");
			if (visuals == null) return;

			foreach (Node child in visuals.GetChildren())
			{
				if (child is not MeshInstance2D mesh) continue;
				if (mesh.Material is not ShaderMaterial shared) continue;

				var material = (ShaderMaterial)shared.Duplicate();
				mesh.Material = material;

				float weight = Mathf.Clamp(mesh.Scale.X, 0.35f, 1f);
				_layers.Add(new VisualLayer
				{
					Node = mesh,
					Material = material,
					BaseScale = mesh.Scale,
					BasePosition = mesh.Position,
					BaseRotation = mesh.Rotation,
					Weight = weight,
				});
			}

			if (_layers.Count == 0)
				GD.PushWarning($"[{nameof(InterferenceZone)}] Visuals 下没有 MeshInstance2D 视觉层");
		}

		private void UpdateLayers(float fade)
		{
			float radius = Mathf.Max(Radius, 1f);
			Vector2 aspect = new(Mathf.Max(Aspect.X, 0.05f), Mathf.Max(Aspect.Y, 0.05f));
			foreach (var layer in _layers)
			{
				// 矩形区：Position 直接使用场景里摆好的像素偏移（不再乘 Radius）；
				// 只按 Radius × 宽高比缩放，不再漂移/自转/呼吸。
				layer.Node.Position = layer.BasePosition;
				layer.Node.Rotation = layer.BaseRotation;
				layer.Node.Scale = layer.BaseScale * radius * 1.06f * aspect;
				layer.Material.SetShaderParameter("strength", Mathf.Clamp(fade * layer.Weight, 0f, 1f));
			}
		}

		private void OnBodyEntered(Node2D body)
		{
			if (_triggered || body is not GameActor) return;

			bool accepted = true;
			if (Damage > 0)
			{
				accepted = DamageDispatcher.DealDamage(
					body, Damage, GlobalPosition, Attacker,
					DamageSource.AreaEffect, TargetableFactions, AllowSelfDamage);
			}
			if (!accepted) return;

			_triggered = true;
			if (_area != null) _area.SetDeferred(Area2D.PropertyName.Monitoring, false);
			_remaining = Mathf.Min(_remaining, Mathf.Max(TriggerFadeDuration, 0.05f));
		}
	}
}
