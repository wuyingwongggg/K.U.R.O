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
		[Export(PropertyHint.Range, "0.1,3,0.05")] public float DespawnDuration = 0.8f;

		private ShaderMaterial? _buildMaterial;
		private ShaderMaterial? _coreMaterial;
		private ShaderMaterial? _glowMaterial;
		private ShaderMaterial? _scanMaterial;
		private Sprite2D? _coreSprite;
		private Sprite2D? _buildSprite;
		private Sprite2D? _glowSprite;
		private Sprite2D? _scanSprite;

		private bool _despawning;
		private float _despawnTimer;

		public override void _Ready()
		{
			ScanlineEnabled = false;
			base._Ready();
			ResolveSprites();
			ResolveMaterials();
			PlaySpawnAnimation();
		}

		public override void _Process(double delta)
		{
			base._Process(delta);

			if (MaxHP > 0f)
			{
				float level = 1.0f - CurrentHP / MaxHP;
				_scanMaterial?.SetShaderParameter("damage_level", level);
				_coreMaterial?.SetShaderParameter("damage_level", level);
			}

			if (_despawning)
			{
				_despawnTimer -= (float)delta;
				if (_despawnTimer <= 0f)
				{
					SetLayerVisibility(false);
					DoBaseDestroy();
				}
				else
				{
					float t = Mathf.Max(0f, _despawnTimer / DespawnDuration);
					_coreMaterial?.SetShaderParameter("alpha", t);
					_glowMaterial?.SetShaderParameter("alpha", t);
					_scanMaterial?.SetShaderParameter("alpha", t);
				}
			}
		}

		public void PlaySpawnAnimation()
		{
			_despawning = false;
			SetShaderAlpha(1f);
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

		protected override void Destroy()
		{
			if (_despawning) return;
			_despawning = true;
			_despawnTimer = DespawnDuration;
			DisableCollision();
		}

		private void DoBaseDestroy()
		{
			Vector2 spawnPos = GlobalPosition;
			if (DestroyEffectScene != null)
			{
				var effect = DestroyEffectScene.Instantiate();
				if (effect is Node2D node2D)
				{
					GetParent()?.AddChild(node2D);
					node2D.GlobalPosition = spawnPos;
				}
				else
				{
					effect.QueueFree();
				}
			}

			AnimationPlayer? animPlayer = null;
			if (!DestructionAnimationPlayerPath.IsEmpty)
				animPlayer = GetNodeOrNull<AnimationPlayer>(DestructionAnimationPlayerPath);
			animPlayer ??= FindChild("AnimationPlayer", recursive: true) as AnimationPlayer;

			if (animPlayer != null && animPlayer.HasAnimation(DestructionAnimationName))
			{
				animPlayer.Play(DestructionAnimationName);
				animPlayer.AnimationFinished += _ => QueueFree();
				return;
			}

			if (DestructionAnimationDuration > 0f)
				GetTree().CreateTimer(DestructionAnimationDuration).Timeout += () => QueueFree();
			else
				QueueFree();
		}

		private void ResolveSprites()
		{
			_coreSprite = GetSprite(CoreWallPath);
			_buildSprite = GetSprite(BuildMaskPath);
			_glowSprite = GetSprite(GlowWallPath);
			_scanSprite = GetSprite(ScanFXPath);
		}

		private Sprite2D? GetSprite(NodePath path)
		{
			if (path.IsEmpty) return null;
			return GetNodeOrNull<Sprite2D>(path);
		}

		private void ResolveMaterials()
		{
			if (_coreSprite?.Material is ShaderMaterial smc)
			{
				_coreMaterial = (ShaderMaterial)smc.Duplicate();
				_coreSprite.Material = _coreMaterial;
			}
			if (_buildSprite?.Material is ShaderMaterial sm)
			{
				_buildMaterial = (ShaderMaterial)sm.Duplicate();
				_buildSprite.Material = _buildMaterial;
			}
			if (_glowSprite?.Material is ShaderMaterial smg)
			{
				_glowMaterial = (ShaderMaterial)smg.Duplicate();
				_glowSprite.Material = _glowMaterial;
			}
			if (_scanSprite?.Material is ShaderMaterial sm2)
			{
				_scanMaterial = (ShaderMaterial)sm2.Duplicate();
				_scanSprite.Material = _scanMaterial;
			}
		}

		private void SetBuildProgress(float progress)
		{
			_buildMaterial?.SetShaderParameter("build_progress", progress);
		}

		private void SetShaderAlpha(float a)
		{
			_coreMaterial?.SetShaderParameter("alpha", a);
			_glowMaterial?.SetShaderParameter("alpha", a);
			_scanMaterial?.SetShaderParameter("alpha", a);
		}

		private void SetLayerVisibility(bool visible)
		{
			SetSpriteVisible(_coreSprite, visible);
			SetSpriteVisible(_buildSprite, visible);
			SetSpriteVisible(_glowSprite, visible);
			SetSpriteVisible(_scanSprite, visible);
		}

		private static void SetSpriteVisible(CanvasItem? sprite, bool visible)
		{
			if (sprite != null) sprite.Visible = visible;
		}
	}
}
