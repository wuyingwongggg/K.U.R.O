using System;
using Godot;
using Kuros.Items.World;

namespace Kuros.Fx
{
	public partial class MeetingBarrier : DestructibleObject
	{
		[ExportCategory("Layers")]
		[Export] public NodePath CoreWallPath { get; set; } = new("CoreWall");
		[Export] public NodePath BuildMaskPath { get; set; } = new("BuildMask");
		[Export] public NodePath GlowWallPath { get; set; } = new("GlowWall");
		[Export] public NodePath ScanFXPath { get; set; } = new("ScanFX");

		[ExportCategory("Build Animation")]
		[Export(PropertyHint.Range, "0.1,3,0.05")] public float BuildDuration = 0.5f;
		[Export(PropertyHint.Range, "0.1,3,0.05")] public float DespawnDuration = 0.3f;

		private ShaderMaterial? _buildMaterial;
		private ShaderMaterial? _scanMaterial;

		public bool IsDespawning { get; private set; }

		public override void _Ready()
		{
			ScanlineEnabled = false;
			base._Ready();
			ResolveMaterials();
			PlaySpawnAnimation();
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			if (_scanMaterial != null && MaxHP > 0f)
				_scanMaterial.SetShaderParameter("damage_level", 1.0f - CurrentHP / MaxHP);
		}

		public void PlaySpawnAnimation()
		{
			IsDespawning = false;
			SetBuildProgress(0f);
			SetLayerVisibility(true);

			if (_buildMaterial == null) return;

			var tree = GetTree();
			if (tree == null) return;

			var tween = tree.CreateTween();
			tween.TweenMethod(
				Callable.From<float>(pos => _buildMaterial.SetShaderParameter("build_progress", pos)),
				0f, 1f, BuildDuration);
			tween.SetEase(Tween.EaseType.Out);
			tween.SetTrans(Tween.TransitionType.Cubic);
		}

		public void PlayDespawnAnimation(Action? onDone = null)
		{
			if (IsDespawning)
			{
				onDone?.Invoke();
				return;
			}
			IsDespawning = true;

			if (_buildMaterial == null)
			{
				onDone?.Invoke();
				return;
			}

			var tree = GetTree();
			if (tree == null)
			{
				onDone?.Invoke();
				return;
			}

			var tween = tree.CreateTween();
			tween.TweenMethod(
				Callable.From<float>(pos => _buildMaterial.SetShaderParameter("build_progress", pos)),
				1f, 0f, DespawnDuration);
			tween.SetEase(Tween.EaseType.In);
			tween.SetTrans(Tween.TransitionType.Cubic);
			tween.TweenCallback(Callable.From(() =>
			{
				SetLayerVisibility(false);
				onDone?.Invoke();
			}));
		}

		private void ResolveMaterials()
		{
			if (!BuildMaskPath.IsEmpty)
			{
				var sprite = GetNodeOrNull<Sprite2D>(BuildMaskPath);
				if (sprite?.Material is ShaderMaterial sm)
				{
					_buildMaterial = (ShaderMaterial)sm.Duplicate();
					sprite.Material = _buildMaterial;
				}
			}
			if (!ScanFXPath.IsEmpty)
			{
				var sprite = GetNodeOrNull<Sprite2D>(ScanFXPath);
				if (sprite?.Material is ShaderMaterial sm)
				{
					_scanMaterial = (ShaderMaterial)sm.Duplicate();
					sprite.Material = _scanMaterial;
				}
			}
		}

		private void SetBuildProgress(float progress)
		{
			_buildMaterial?.SetShaderParameter("build_progress", progress);
		}

		private void SetLayerVisibility(bool visible)
		{
			SetSpriteVisible(CoreWallPath, visible);
			SetSpriteVisible(BuildMaskPath, visible);
			SetSpriteVisible(GlowWallPath, visible);
			SetSpriteVisible(ScanFXPath, visible);
		}

		private void SetSpriteVisible(NodePath path, bool visible)
		{
			if (path.IsEmpty) return;
			var sprite = GetNodeOrNull<CanvasItem>(path);
			if (sprite != null) sprite.Visible = visible;
		}
	}
}
