using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	public partial class LaserBeamA : Node2D, IFacingDirectional
	{
		[ExportCategory("Nodes")]
		/// <summary>命中检测射线节点路径（RayCast2D，决定光束长度）。</summary>
		[Export] public NodePath RayCastPath { get; set; } = new("RayCast2D");
		/// <summary>光晕层节点路径（Sprite2D）。</summary>
		[Export] public NodePath GlowSpritePath { get; set; } = new("GlowSprite");
		/// <summary>核心光束层节点路径（Sprite2D）。</summary>
		[Export] public NodePath BeamSpritePath { get; set; } = new("BeamSprite");
		/// <summary>发光点节点路径（Sprite2D，独立生命周期）。</summary>
		[Export] public NodePath SpotlightPath { get; set; } = new("Spotlight");

		[ExportCategory("Delay")]
		/// <summary>延迟射出时长（秒）：施加后整体隐藏，到点才显示。纯前置时间，不占用 Lifetime 与 SpotlightDuration。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float DelaySeconds { get; set; } = 0f;

		[ExportCategory("Beam")]
		/// <summary>光束最大长度（像素，无遮挡时的长度）。</summary>
		[Export] public float MaxLength = 3000f;
		/// <summary>核心光束宽度（像素）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float BeamWidth = 32f;
		/// <summary>光晕宽度（像素）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float GlowWidth = 96f;
		/// <summary>生长动画时长（秒）：从 MinLength 生长到命中长度。</summary>
		[Export(PropertyHint.Range, "0,5,0.05")] public float GrowDuration = 0.1f;
		/// <summary>初始长度（像素，生长起点）。</summary>
		[Export(PropertyHint.Range, "0,3000,10")] public float MinLength = 0f;
		/// <summary>光束总存活时长（秒，不含 Delay）。</summary>
		[Export] public float Lifetime = 0.45f;
		/// <summary>光束淡出时长（秒）：到期前 shader fade。</summary>
		[Export] public float FadeDuration = 0.15f;

		[ExportCategory("Spotlight")]
		/// <summary>发光点独立存活时长（秒）：光束结束后光点继续存在并自毁。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float SpotlightDuration { get; set; } = 1.6f;
		/// <summary>发光点淡入时长（秒）：射出时从透明渐显。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeIn { get; set; } = 0.15f;
		/// <summary>发光点淡出时长（秒）：分离后到点前渐隐再销毁。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeOut { get; set; } = 0.3f;

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
		private Sprite2D? _glowSprite;
		private Sprite2D? _beamSprite;
		private Sprite2D? _spotlight;
		private float _timer;
		private float _currentLength;
		private float _texWidth;
		private float _texHeight;
		private bool _pendingAutoAim;
		private bool _hasDamaged;
		private Node2D? _cachedPlayer;
		private GameActor? _attacker;
		private float _emitElapsed;      // 射出后已过时间（延迟期间为负/0，生长/淡入基于此）
		private bool _emitted;           // 是否已射出（延迟结束置位，首次显示）

		public override void _Ready()
		{
			// 节点引用全部走导出路径（可重命名场景节点，无需改脚本）
			_ray = RayCastPath != null && !RayCastPath.IsEmpty ? GetNodeOrNull<RayCast2D>(RayCastPath) : null;
			_glowSprite = GlowSpritePath != null && !GlowSpritePath.IsEmpty ? GetNodeOrNull<Sprite2D>(GlowSpritePath) : null;
			_beamSprite = BeamSpritePath != null && !BeamSpritePath.IsEmpty ? GetNodeOrNull<Sprite2D>(BeamSpritePath) : null;
			_spotlight = SpotlightPath != null && !SpotlightPath.IsEmpty ? GetNodeOrNull<Sprite2D>(SpotlightPath) : null;

			if (_ray == null || _beamSprite == null)
			{
				GD.PushWarning("[LaserBeamA] 缺少子节点");
				QueueFree();
				return;
			}

			// 每个实例独立复制 Material，防止多实例共享导致 fade 残留
			if (_glowSprite?.Material is ShaderMaterial gm)
			{
				var copy = (ShaderMaterial)gm.Duplicate();
				copy.SetShaderParameter("fade", 1.0f);
				_glowSprite.Material = copy;
			}
			if (_beamSprite?.Material is ShaderMaterial bm)
			{
				var copy = (ShaderMaterial)bm.Duplicate();
				copy.SetShaderParameter("fade", 1.0f);
				_beamSprite.Material = copy;
			}

			_texWidth = _beamSprite.Texture?.GetWidth() ?? 2f;
			_texHeight = _beamSprite.Texture?.GetHeight() ?? 2f;
			if (_texWidth <= 0f) _texWidth = 2f;
			if (_texHeight <= 0f) _texHeight = 2f;

			// 预设初始 scale（MinLength，配合生长动画）
			if (_glowSprite != null) _glowSprite.Scale = new Vector2(MinLength / _texWidth, GlowWidth / _texHeight);
			if (_beamSprite != null) _beamSprite.Scale = new Vector2(MinLength / _texWidth, BeamWidth / _texHeight);

			_ray.TargetPosition = new Vector2(MaxLength, 0f);
			_ray.Enabled = true;

			ResolveAttacker();
			_timer = Lifetime;
			_pendingAutoAim = AutoAimAtPlayer;
			_emitElapsed = 0f;
			_emitted = false;

			// 延迟射出：DelaySeconds 内整体隐藏（光束+光点），到点才显示
			if (DelaySeconds > 0f)
				Visible = false;
			else
				_emitted = true;
		}

		public override void _Process(double delta)
		{
			// 延迟阶段：整体隐藏，不消耗 Lifetime（纯前置时间），到点首次显示
			if (!_emitted)
			{
				_emitElapsed += (float)delta;
				if (_emitElapsed < DelaySeconds) return;
				_emitted = true;
				Visible = true;
				_pendingAutoAim = AutoAimAtPlayer;
			}

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
			if (_timer <= 0f)
			{
				DetachSpotlight(); // 光束结束：光点分离继续存活（独立生命周期）
				QueueFree();
				return;
			}

			// 淡出：shader 通过 uniform fade 控制（仅光束层）。
			// Spotlight 不参与光束淡出——它有独立生命周期（SpotlightDuration/淡入淡出），
			// 否则 FadeDuration ≥ Lifetime 时光点从第一帧就被衰减到不可见，结束后又"突然出现"。
			if (_timer < FadeDuration && FadeDuration > 0f)
			{
				float t = _timer / FadeDuration;
				if (_glowSprite?.Material is ShaderMaterial gm)
					gm.SetShaderParameter("fade", t);
				if (_beamSprite?.Material is ShaderMaterial bm)
					bm.SetShaderParameter("fade", t);
			}

			// 光点淡入（射出后前 SpotlightFadeIn 秒 0 → 1），完成后保持全亮
			if (_spotlight != null && SpotlightFadeIn > 0f)
			{
				var sc = _spotlight.Modulate;
				float fadeInT = Mathf.Clamp((Lifetime - _timer) / SpotlightFadeIn, 0f, 1f);
				_spotlight.Modulate = new Color(sc.R, sc.G, sc.B, fadeInT);
			}

			UpdateBeam();

			// 生长完成后触发伤害，与视觉同步
			if (!_hasDamaged && Lifetime - _timer >= GrowDuration)
				TryDamagePlayer();
		}

		/// <summary>
		/// 分离发光点：光束结束时把 Spotlight 重新挂到当前父级，保持全局位置跟随移动，
		/// 重新亮起（光束淡出可能已压低 alpha），再存活 SpotlightDuration 秒（最后 SpotlightFadeOut 淡出）后自毁。
		/// </summary>
		private void DetachSpotlight()
		{
			if (_spotlight == null || !IsInstanceValid(_spotlight)) return;

			var newParent = GetParent();
			if (newParent == null) return;

			Vector2 globalPos = _spotlight.GlobalPosition;
			_spotlight.GetParent()?.RemoveChild(_spotlight);
			newParent.AddChild(_spotlight);
			_spotlight.GlobalPosition = globalPos;

			var c = _spotlight.Modulate;
			_spotlight.Modulate = new Color(c.R, c.G, c.B, 1f);

			var spotlight = _spotlight;
			_spotlight = null;
			var tree = GetTree();
			if (tree == null) return;

			float fadeOut = Mathf.Max(0f, SpotlightFadeOut);
			float delay = Mathf.Max(0f, SpotlightDuration - fadeOut);
			tree.CreateTimer(delay).Timeout += () =>
			{
				if (!IsInstanceValid(spotlight)) return;
				if (fadeOut <= 0f)
				{
					spotlight.QueueFree();
					return;
				}
				var tween = tree.CreateTween();
				tween.TweenProperty(spotlight, "modulate:a", 0f, fadeOut);
				tween.TweenCallback(Callable.From(() =>
				{
					if (IsInstanceValid(spotlight))
						spotlight.QueueFree();
				}));
			};
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
			if (_ray == null) return;
			_ray.ForceRaycastUpdate();
			float rawLength = _ray.IsColliding()
				? ToLocal(_ray.GetCollisionPoint()).Length()
				: MaxLength;

			float elapsed = Lifetime - _timer;
			float grow = GrowDuration > 0f ? Mathf.Clamp(elapsed / GrowDuration, 0f, 1f) : 1f;
			_currentLength = Mathf.Lerp(MinLength, rawLength, grow);

			if (_glowSprite != null)
			{
				_glowSprite.Position = Vector2.Zero;
				_glowSprite.Scale = new Vector2(_currentLength / _texWidth, GlowWidth / _texHeight);
			}

			if (_beamSprite != null)
			{
				_beamSprite.Position = Vector2.Zero;
				_beamSprite.Scale = new Vector2(_currentLength / _texWidth, BeamWidth / _texHeight);
			}

			if (_spotlight != null)
				_spotlight.Position = Vector2.Zero;
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

			// 俯视角地面判定：判定起点/方向投影到 y=0 地面平面——
			// 光束视觉在发射点高度（含 tilt 俯仰），判定不受视觉高度/倾斜影响
			Vector2 judgeOrigin = new(GlobalPosition.X, 0f);
			Vector2 beamDir = new(Mathf.Cos(Rotation), 0f);
			if (beamDir == Vector2.Zero)
				beamDir = new Vector2(FacingRight ? 1f : -1f, 0f);

			float along = (targetCenter.X - judgeOrigin.X) * beamDir.X;
			if (along < 0f || along > _currentLength) return;

			// 地面平面内玩家高度容差：HitArea 半径（地面判定只关心玩家是否贴地）
			float perp = Mathf.Abs(targetCenter.Y - judgeOrigin.Y);
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
