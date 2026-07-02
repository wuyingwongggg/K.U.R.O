using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	public partial class LightningBeam : Node2D, IFacingDirectional
	{
		[ExportCategory("Beam")]
		[Export] public float MaxLength = 3000f;
		[Export(PropertyHint.Range, "0,5,0.05")] public float GrowDuration = 0.1f;
		[Export(PropertyHint.Range, "0,3000,10")] public float MinLength = 0f;

		[ExportCategory("Timing")]
		[Export] public float Lifetime = 0.45f;
		[Export] public float FadeDuration = 0.15f;

		[ExportCategory("Damage")]
		[Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
		public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
		[Export] public bool AllowSelfDamage { get; set; } = false;
		[Export(PropertyHint.Range, "0,500,1")] public int Damage = 0;

		[ExportCategory("Knockback")]
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
		[Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 0f;

		[ExportCategory("Targeting")]
		[Export] public bool AutoAimAtPlayer = true;
		[Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 5f;
		[Export] public bool FacingRight { get; set; } = true;

		private RayCast2D? _ray;
		private Sprite2D? _lightningSprite;
		private float _timer;
		private float _currentLength;
		private float _texWidth;
		private float _texHeight;
		private bool _pendingAutoAim;
		private bool _hasDamaged;
		private Node2D? _cachedPlayer;
		private GameActor? _attacker;

		public override void _Ready()
		{
			_ray = GetNodeOrNull<RayCast2D>("RayCast2D");
			_lightningSprite = GetNodeOrNull<Sprite2D>("LightningSprite");

			if (_ray == null || _lightningSprite == null)
			{
				GD.PushWarning("[LightningBeam] 缺少子节点");
				QueueFree();
				return;
			}

			if (_lightningSprite.Material is ShaderMaterial sm)
				_lightningSprite.Material = (ShaderMaterial)sm.Duplicate();

			_texWidth = _lightningSprite.Texture?.GetWidth() ?? 2f;
			_texHeight = _lightningSprite.Texture?.GetHeight() ?? 2f;
			if (_texWidth <= 0f) _texWidth = 2f;
			if (_texHeight <= 0f) _texHeight = 2f;

			_lightningSprite.Scale = new Vector2(MinLength / _texWidth, _lightningSprite.Scale.Y);

			_ray.TargetPosition = new Vector2(MaxLength, 0f);
			_ray.Enabled = true;

			ResolveAttacker();
			_timer = Lifetime;
			_pendingAutoAim = AutoAimAtPlayer;
		}

		public override void _Process(double delta)
		{
			if (_pendingAutoAim)
			{
				_pendingAutoAim = false;
				var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
				if (player != null)
				{
					_cachedPlayer = player;
					var hitArea = player.GetNodeOrNull<Area2D>("HitArea")
						?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
					var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
					Vector2 aimTarget = hitShape?.GlobalPosition ?? hitArea?.GlobalPosition ?? player.GlobalPosition;
					AimHorizontalWithVerticalTilt(aimTarget);
				}
				UpdateBeam();
			}

			_timer -= (float)delta;
			if (_timer <= 0f) { QueueFree(); return; }

			if (_timer < FadeDuration && FadeDuration > 0f)
			{
				float t = _timer / FadeDuration;
				if (_lightningSprite != null)
				{
					var c = _lightningSprite.Modulate;
					_lightningSprite.Modulate = new Color(c.R, c.G, c.B, t);
				}
			}

			UpdateBeam();

			if (!_hasDamaged && Lifetime - _timer >= GrowDuration)
				TryDamagePlayer();
		}

		public void AimHorizontalWithVerticalTilt(Vector2 globalTarget)
		{
			Vector2 toTarget = globalTarget - GlobalPosition;
			float baseAngle = FacingRight ? 0f : Mathf.Pi;
			bool front = FacingRight ? toTarget.X >= 0f : toTarget.X <= 0f;
			float tilt = 0f;
			if (front && toTarget != Vector2.Zero)
			{
				float maxR = Mathf.DegToRad(MaxVerticalTiltDegrees);
				float dySign = FacingRight ? 1f : -1f;
				tilt = Mathf.Atan2(toTarget.Y * dySign, Mathf.Abs(toTarget.X));
				tilt = Mathf.Clamp(tilt, -maxR, maxR);
			}
			Rotation = baseAngle + tilt;
		}

		public void LookAtGlobal(Vector2 globalTarget)
		{
			Vector2 dir = (globalTarget - GlobalPosition).Normalized();
			if (dir != Vector2.Zero) Rotation = dir.Angle();
		}

		private void UpdateBeam()
		{
			if (_ray == null || _lightningSprite == null) return;
			_ray.ForceRaycastUpdate();
			float rawLength = _ray.IsColliding()
				? ToLocal(_ray.GetCollisionPoint()).Length()
				: MaxLength;

			float elapsed = Lifetime - _timer;
			float grow = GrowDuration > 0f ? Mathf.Clamp(elapsed / GrowDuration, 0f, 1f) : 1f;
			_currentLength = Mathf.Lerp(MinLength, rawLength, grow);

			_lightningSprite.Position = Vector2.Zero;
			_lightningSprite.Scale = new Vector2(_currentLength / _texWidth, _lightningSprite.Scale.Y);
		}

		private void TryDamagePlayer()
		{
			if (_hasDamaged) return;
			if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
			if (_cachedPlayer == null) return;

			var hitArea = _cachedPlayer.GetNodeOrNull<Area2D>("HitArea")
				?? _cachedPlayer.FindChild("HitArea", recursive: true, owned: false) as Area2D;
			var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			Vector2 targetCenter = hitShape?.GlobalPosition ?? hitArea?.GlobalPosition ?? _cachedPlayer.GlobalPosition;

			Vector2 beamDir = new(Mathf.Cos(Rotation), Mathf.Sin(Rotation));
			Vector2 toTarget = targetCenter - GlobalPosition;
			float along = toTarget.Dot(beamDir);
			if (along < 0f || along > _currentLength) return;

			float perp = Mathf.Abs(toTarget.X * beamDir.Y - toTarget.Y * beamDir.X);
			float detectionRadius = 150f;
			if (hitShape?.Shape is CapsuleShape2D cap)
				detectionRadius = cap.Radius * Mathf.Abs(hitShape.GlobalTransform.Scale.X);

			if (perp > detectionRadius) return;
			if (_cachedPlayer is not GameActor actor) return;

			bool alreadyInvincible = actor is Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;
			_hasDamaged = true;

			bool dealt = DamageDispatcher.DealDamage(actor, Damage, GlobalPosition, _attacker,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage);
			if (!dealt) return;

			if (!alreadyInvincible)
			{
				float knockSpeed = KnockbackSpeed > 0f
					? KnockbackSpeed
					: (KnockbackDistance > 0f ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f) : 0f);
				if (knockSpeed > 0f) actor.Velocity = beamDir * knockSpeed;
			}
		}

		private void ResolveAttacker()
		{
			var parent = GetParent();
			if (parent == null) return;
			foreach (var child in parent.GetChildren())
			{ if (child.IsInGroup("enemies") && child is GameActor ga) { _attacker = ga; break; } }
		}
	}
}
