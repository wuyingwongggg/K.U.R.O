using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;

namespace Kuros.Items.World
{
	public partial class DestructibleObject : Node2D, IDirectionalDamageReceiver, IBarrier
	{
		// 方向位掩码：Left=1, Right=2, Up=4, Down=8（与 ReceiveFromDirections 的 Flags 顺序一致）
		private const int DirLeft = 1;
		private const int DirRight = 2;
		private const int DirUp = 4;
		private const int DirDown = 8;
		private const int DirAll = 15;

		[ExportCategory("Health")]
		[Export] public bool Destructible { get; set; } = true;
		[Export(PropertyHint.Range, "1,9999,1")] public float MaxHP = 60f;
		[Export(PropertyHint.Range, "0.1,5,0.05")] public float DamageCooldown = 0.2f;
		public float CurrentHP { get; private set; }

		[ExportCategory("Collision")]
		[Export(PropertyHint.Range, "0,2,0.05")] public float CollisionEnableDelay = 0.15f;
		[Export] public NodePath StaticBodyPath { get; set; } = new("StaticBody2D");

		[ExportCategory("Hit Flash")]
		[Export] public NodePath HitFlashSpritePath { get; set; } = new();
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float HitFlashDuration = 0.15f;
		[Export(PropertyHint.Range, "1,120,1")] public float HitFlashSpeed = 30f;
		[Export(PropertyHint.Range, "0,500,0.1")] public float HitShakeIntensity = 100f;
		[Export] public Color HitFlashColor = new(1f, 1f, 1f, 1f);

		[ExportCategory("Scanline Reveal")]
		[Export] public bool ScanlineEnabled = true;
		[Export] public NodePath ScanlineSpritePath { get; set; } = new("Sprite2D");
		[Export(PropertyHint.Range, "0.05,2,0.05")] public float ScanlineSpawnDuration = 0.3f;
		[Export(PropertyHint.Range, "0.05,2,0.05")] public float ScanlineDespawnDuration = 0.2f;

		[ExportCategory("Directional Receive")]
		/// <summary>接收伤害的方向（Flags 位掩码：Left=1, Right=2, Up=4, Down=8，15 = 全方向）。
		/// 非接收方向的攻击不结算伤害，bullet 类攻击特效直接穿过屏障。</summary>
		[Export(PropertyHint.Flags, "Left,Right,Up,Down")]
		public int ReceiveFromDirections { get; set; } = DirAll;

		[ExportCategory("Destroy")]
		[Export(PropertyHint.Range, "0,120,0.1")] public float LifeTime = 0f;
		[Export] public PackedScene? DestroyEffectScene { get; set; }
		[Export] public NodePath DestructionAnimationPlayerPath { get; set; } = new();
		[Export] public string DestructionAnimationName { get; set; } = "destroy";
		[Export(PropertyHint.Range, "0.01,10,0.01")] public float DestructionAnimationDuration = 0.5f;

		private Sprite2D? _hitFlashTarget;
		private Sprite2D? _hitFlashOverlay;
		private ShaderMaterial? _hitFlashMaterial;
		private float _hitFlashTimer;
		private bool _hitFlashActive;
		private float _damageCooldownRemaining;
		private float _lifeTimer;
		private bool _isDestroying;
		private StaticBody2D? _staticBody;
		private uint _originalCollisionLayer;
		private uint _originalCollisionMask;
		private ShaderMaterial? _scanlineMaterial;

		public override void _Ready()
		{
			if (!IsInGroup("world_items"))
				AddToGroup("world_items");
			// 可破坏物声明：无视攻击方分类过滤（TargetableFactions），接收任何攻击方的伤害
			if (!IsInGroup("damage_receivable"))
				AddToGroup("damage_receivable");

			CurrentHP = MaxHP;
			SetupHitFlash();
			SetupScanline();
			ResolveAndDisableCollision();
			PlayScanlineSpawn();
		}
		public override void _ExitTree()
		{
			if (NavigationRebakeCoordinator.HasNavigationSourceGeometry(this))
				NavigationRebakeCoordinator.RequestRebake(this);
		}


		public override void _Process(double delta)
		{
			if (_hitFlashActive)
			{
				_hitFlashTimer -= (float)delta;
				if (_hitFlashTimer <= 0f)
					EndHitFlash();
				else
					_hitFlashMaterial?.SetShaderParameter("hit_effect", _hitFlashTimer / HitFlashDuration);
			}

			if (_damageCooldownRemaining > 0f)
				_damageCooldownRemaining -= (float)delta;

			if (LifeTime > 0f)
			{
				_lifeTimer += (float)delta;
				if (_lifeTimer >= LifeTime)
					Destroy();
			}
		}

		/// <summary>攻击方向向量（如子弹速度方向）是否接收伤害。主分量判定（对角线取主导分量）。
		/// 用本地坐标判定——方向跟随屏障朝向：翻转（Scale.x=-1）/旋转后左右自动反转。</summary>
		public bool AcceptsAttackFromDirection(Vector2 direction)
		{
			if (ReceiveFromDirections == DirAll) return true;
			if (direction == Vector2.Zero) return AcceptsAttackFrom(GlobalPosition);

			Vector2 local = ToLocal(GlobalPosition + direction);
			return ResolveDirection(local);
		}

		/// <summary>攻击来源点 origin 相对本屏障的方向是否接收伤害（无攻击方向向量时的回退）。</summary>
		public bool AcceptsAttackFrom(Vector2 origin)
		{
			if (ReceiveFromDirections == DirAll) return true;
			return ResolveDirection(ToLocal(origin));
		}

		/// <summary>主方向判定（本地坐标）：|X| ≥ |Y| 判左右，否则判上下。</summary>
		private bool ResolveDirection(Vector2 local)
		{
			if (Mathf.Abs(local.X) >= Mathf.Abs(local.Y))
				return (ReceiveFromDirections & (local.X >= 0f ? DirRight : DirLeft)) != 0;
			return (ReceiveFromDirections & (local.Y >= 0f ? DirDown : DirUp)) != 0;
		}

		public void TakeDamage(float damage)
		{
			if (!Destructible) return;
			if (CurrentHP <= 0f) return;
			if (_damageCooldownRemaining > 0f) return;

			CurrentHP = Mathf.Max(0f, CurrentHP - damage);
			_damageCooldownRemaining = DamageCooldown;
			TriggerHitFlash();

			if (CurrentHP <= 0f)
				Destroy();
		}

		protected virtual void Destroy()
		{
			if (_isDestroying) return;
			_isDestroying = true;

			DisableCollision();

			Action doDestroy = () =>
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
			};

			if (_scanlineMaterial != null && ScanlineEnabled)
				PlayScanlineDespawn(doDestroy);
			else
				doDestroy();
		}

		private void ResolveAndDisableCollision()
		{
			if (!StaticBodyPath.IsEmpty)
				_staticBody = GetNodeOrNull<StaticBody2D>(StaticBodyPath);
			_staticBody ??= FindChild("StaticBody2D", recursive: true) as StaticBody2D;

			if (_staticBody == null) return;

			_originalCollisionLayer = _staticBody.CollisionLayer;
			_originalCollisionMask = _staticBody.CollisionMask;
			_staticBody.CollisionLayer = 0;
			_staticBody.CollisionMask = 0;

			if (CollisionEnableDelay > 0f)
				GetTree().CreateTimer(CollisionEnableDelay).Timeout += EnableCollision;
			else
				CallDeferred(MethodName.EnableCollision);
		}

		private void EnableCollision()
		{
			if (_staticBody == null || !IsInstanceValid(_staticBody)) return;
			_staticBody.CollisionLayer = _originalCollisionLayer;
			_staticBody.CollisionMask = _originalCollisionMask;
			if (NavigationRebakeCoordinator.HasNavigationSourceGeometry(this))
				NavigationRebakeCoordinator.RequestRebake(this);
		}

		protected void DisableCollision()
		{
			if (_staticBody == null || !IsInstanceValid(_staticBody)) return;
			_staticBody.CollisionLayer = 0;
			_staticBody.CollisionMask = 0;
		}

		private void SetupHitFlash()
		{
			if (HitFlashSpritePath.IsEmpty) return;

			_hitFlashTarget = GetNodeOrNull<Sprite2D>(HitFlashSpritePath);
			if (_hitFlashTarget == null) return;

			var shader = GD.Load<Shader>("res://shaders/materials/trigger_hit.gdshader");
			if (shader == null) return;

			_hitFlashMaterial = new ShaderMaterial();
			_hitFlashMaterial.Shader = shader;
			_hitFlashMaterial.SetShaderParameter("get_hit", false);
			_hitFlashMaterial.SetShaderParameter("hit_effect", 0f);
			_hitFlashMaterial.SetShaderParameter("flash_color", HitFlashColor);
			_hitFlashMaterial.SetShaderParameter("flash_speed", HitFlashSpeed);
			_hitFlashMaterial.SetShaderParameter("shake_intensity", HitShakeIntensity);

			_hitFlashOverlay = new Sprite2D();
			_hitFlashOverlay.Name = "_HitFlashOverlay";
			_hitFlashOverlay.Texture = _hitFlashTarget.Texture;
			_hitFlashOverlay.Centered = _hitFlashTarget.Centered;
			_hitFlashOverlay.Offset = _hitFlashTarget.Offset;
			_hitFlashOverlay.RegionEnabled = _hitFlashTarget.RegionEnabled;
			_hitFlashOverlay.RegionRect = _hitFlashTarget.RegionRect;
			_hitFlashOverlay.Scale = _hitFlashTarget.Scale;
			_hitFlashOverlay.Visible = false;
			_hitFlashOverlay.ZIndex = _hitFlashTarget.ZIndex + 1;
			_hitFlashTarget.AddSibling(_hitFlashOverlay);
		}

		private void TriggerHitFlash()
		{
			if (_hitFlashOverlay == null || _hitFlashMaterial == null) return;

			_hitFlashOverlay.Material = _hitFlashMaterial;
			_hitFlashOverlay.Visible = true;
			_hitFlashMaterial.SetShaderParameter("get_hit", true);
			_hitFlashMaterial.SetShaderParameter("hit_effect", 1f);
			_hitFlashTimer = HitFlashDuration;
			_hitFlashActive = true;
		}

		private void EndHitFlash()
		{
			_hitFlashActive = false;
			_hitFlashMaterial?.SetShaderParameter("get_hit", false);
			_hitFlashMaterial?.SetShaderParameter("hit_effect", 0f);

			if (_hitFlashOverlay != null)
				_hitFlashOverlay.Visible = false;
		}

		private void SetupScanline()
		{
			if (!ScanlineEnabled) return;

			Sprite2D? scanlineSprite = null;
			if (!ScanlineSpritePath.IsEmpty)
				scanlineSprite = GetNodeOrNull<Sprite2D>(ScanlineSpritePath);
			scanlineSprite ??= FindChild("Sprite2D", recursive: true) as Sprite2D;

			if (scanlineSprite?.Material is ShaderMaterial sm)
				_scanlineMaterial = sm;
		}

		private void PlayScanlineSpawn()
		{
			if (_scanlineMaterial == null) return;
			var tree = GetTree();
			if (tree == null) return;

			_scanlineMaterial.SetShaderParameter("reverse_scan", false);
			_scanlineMaterial.SetShaderParameter("scanline_pos", 0f);

			var tween = tree.CreateTween();
			tween.TweenMethod(
				Callable.From<float>(pos => _scanlineMaterial.SetShaderParameter("scanline_pos", pos)),
				0f, 1f, ScanlineSpawnDuration);
			tween.TweenCallback(Callable.From(() =>
			{
				_scanlineMaterial?.SetShaderParameter("scanline_pos", -1f);
			}));
		}

		private void PlayScanlineDespawn(Action onDone)
		{
			if (_scanlineMaterial == null) { onDone(); return; }
			var tree = GetTree();
			if (tree == null) { onDone(); return; }

			_scanlineMaterial.SetShaderParameter("reverse_scan", true);
			_scanlineMaterial.SetShaderParameter("scanline_pos", 1f);

			var tween = tree.CreateTween();
			tween.TweenMethod(
				Callable.From<float>(pos => _scanlineMaterial.SetShaderParameter("scanline_pos", pos)),
				1f, 0f, ScanlineDespawnDuration);
			tween.TweenCallback(Callable.From(onDone));
		}
	}
}
