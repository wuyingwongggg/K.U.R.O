using Godot;

namespace Kuros.Fx
{
	/// <summary>
	/// 激光束视觉基类：节点解析、材质复制、计时模型（光束/光点独立并行）、
	/// 长度/宽度生长收缩动画、Spotlight 独立生命周期（淡入/存活/淡出由 _Process 每帧驱动，不用 tween）。
	///
	/// 生命周期：
	///   · 光束：GrowDuration（生长）→ BeamDuration（全亮）→ FadeDuration（shader fade + 宽度收缩）
	///   · 光点：SpotlightFadeIn（淡入）→ SpotlightDuration（存活）→ SpotlightFadeOut（淡出）
	///   · Lifetime：LaserBeamA 节点总时长兜底（到期强制销毁一切）
	///
	/// 子类职责：InitializeDirection（方向初始化）、OnBeamGrown（生长完成后的伤害/击退）。
	/// </summary>
	public abstract partial class LaserBeamVisualBase : Node2D
	{
		[ExportCategory("Nodes")]
		/// <summary>光晕层节点路径（Sprite2D）。</summary>
		[Export] public NodePath GlowSpritePath { get; set; } = new("GlowSprite");
		/// <summary>核心光束层节点路径（Sprite2D）。</summary>
		[Export] public NodePath BeamSpritePath { get; set; } = new("BeamSprite");
		/// <summary>发光点节点路径（Sprite2D，独立生命周期）。</summary>
		[Export] public NodePath SpotlightPath { get; set; } = new("Spotlight");
		/// <summary>发光点外圈节点路径（Sprite2D，可选——与 Spotlight 同步淡入/存活/淡出，同光束 Glow/Beam 双层结构）。</summary>
		[Export] public NodePath SpotGlowSpritePath { get; set; } = new("SpotGlow");
		/// <summary>光束判定区节点路径（Area2D，判定带随光束生长）。</summary>
		[Export] public NodePath HitAreaPath { get; set; } = new("BeamHitArea");

		[ExportCategory("Beam")]
		/// <summary>光束最大长度（像素，无遮挡时的长度）。</summary>
		[Export] public float MaxLength = 3000f;
		/// <summary>核心光束宽度（像素）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float BeamWidth = 32f;
		/// <summary>光晕宽度（像素）。</summary>
		[Export(PropertyHint.Range, "1,2000,1")] public float GlowWidth = 96f;
		/// <summary>初始长度（像素，生长起点）。</summary>
		[Export(PropertyHint.Range, "0,3000,10")] public float MinLength = 0f;
		/// <summary>光束延迟时长（秒）：发射后先等待此时间才开始生长（前摇）。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float BeamDelay { get; set; } = 0f;
		/// <summary>生长动画时长（秒）：从 MinLength 生长到最大长度，宽度同步 0 → 目标。</summary>
		[Export(PropertyHint.Range, "0,5,0.05")] public float GrowDuration = 0.1f;
		/// <summary>光束全亮保持时长（秒）：生长完成后保持最大长度/宽度。</summary>
		[Export(PropertyHint.Range, "0,10,0.05")] public float BeamDuration = 0.4f;
		/// <summary>光束淡出时长（秒）：光束生命周期最后阶段 shader fade + 宽度收缩。</summary>
		[Export] public float FadeDuration = 0.15f;

		[ExportCategory("Spotlight")]
		/// <summary>光点延迟时长（秒）：发射后先等待此时间才开始淡入（前摇）。</summary>
		[Export(PropertyHint.Range, "0,2,0.05")] public float SpotlightDelay { get; set; } = 0f;
		/// <summary>发光点淡入时长（秒）：射出时从透明渐显。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeIn { get; set; } = 0.15f;
		/// <summary>发光点独立存活时长（秒）：淡入后保持全亮。</summary>
		[Export(PropertyHint.Range, "0.1,10,0.1")] public float SpotlightDuration { get; set; } = 1.6f;
		/// <summary>发光点淡出时长（秒）：存活期结束后渐隐再销毁。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeOut { get; set; } = 0.3f;

		[ExportCategory("Lifetime")]
		/// <summary>节点总存活时长（秒，不含 Delay）：存在上限（光束/光点各自生命周期独立，此值只作兜底）。</summary>
		[Export] public float Lifetime = 0.7f;

		[ExportCategory("Detection")]
		/// <summary>地面判定带垂直半高（像素）：目标 HitArea 与此带重叠即命中。</summary>
		[Export(PropertyHint.Range, "10,500,1")] public float DetectionRadius = 150f;

		protected Sprite2D? _glowSprite;
		protected Sprite2D? _beamSprite;
		protected Sprite2D? _spotlight;
		protected Sprite2D? _spotGlowSprite;
		protected Node2D? _visual;
		protected Area2D? _hitArea;
		protected CollisionShape2D? _hitShape;
		protected float _totalTimer;          // 节点总时长倒计时（Lifetime，从发射起）
		protected float _elapsed;             // 发射起已过时间（统一时钟）
		protected float _beamPhaseElapsed;    // 光束阶段时间（扣除 BeamDelay，驱动生长/全亮/淡出）
		protected float _spotlightPhaseElapsed; // 光点阶段时间（扣除 SpotlightDelay）
		protected float _currentLength;
		protected float _texWidth;
		protected float _texHeight;
		protected bool _hasDamaged;
		// 判定带相对根的初始偏移（如 (0,200) 地面层）——每帧按世界方向定位，
		// 避免根翻转（旋转 π/镜像）时局部偏移被翻转（判定带跑到另一侧）
		private Vector2 _hitAreaLocalOffset;

		private bool _directionInitialized;

		public override void _Ready()
		{
			_glowSprite = ResolveNode<Sprite2D>(GlowSpritePath, "GlowSprite");
			_beamSprite = ResolveNode<Sprite2D>(BeamSpritePath, "BeamSprite");
			_spotlight = ResolveNode<Sprite2D>(SpotlightPath, "Spotlight");
			_spotGlowSprite = ResolveNode<Sprite2D>(SpotGlowSpritePath, "SpotGlow");
			_visual = GetNodeOrNull<Node2D>("Visual");
			_hitArea = ResolveNode<Area2D>(HitAreaPath, "BeamHitArea");
			if (_hitArea != null)
			{
				_hitAreaLocalOffset = _hitArea.Position;
				_hitArea.CollisionLayer = 0;
				_hitShape = _hitArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
				if (_hitShape?.Shape is RectangleShape2D rs)
				{
					rs.Size = new Vector2(MinLength, DetectionRadius * 2f);
					// 带从发射点向一端伸展（同光束视觉 centered=false）：shape 居中前移半个长度，方向由判定带旋转驱动
					_hitShape.Position = new Vector2(MinLength * 0.5f, 0f);
				}
			}

			if (_beamSprite == null)
			{
				GD.PushWarning("[LaserBeamVisualBase] 缺少光束子节点");
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

			// 预设初始 scale（MinLength、宽度 0——生长动画同时从 0 宽展开到目标宽度）
			if (_glowSprite != null) _glowSprite.Scale = new Vector2(MinLength / _texWidth, 0f);
			if (_beamSprite != null) _beamSprite.Scale = new Vector2(MinLength / _texWidth, 0f);

			_totalTimer = Lifetime;
			_elapsed = 0f;
			_beamPhaseElapsed = 0f;
			_spotlightPhaseElapsed = 0f;
		}

		public override void _Process(double delta)
		{
			// 首帧方向初始化（子类覆写：朝向/GlobalRotation 迁移等）
			if (!_directionInitialized)
			{
				_directionInitialized = true;
				InitializeDirection();
			}

			_totalTimer -= (float)delta;
			_elapsed += (float)delta;

			// 阶段时间 = 发射起时间 - 各自延迟（延迟期内阶段时间为负 → 长度/透明度为 0，视觉不可见）
			_beamPhaseElapsed = _elapsed - Mathf.Max(BeamDelay, 0f);
			_spotlightPhaseElapsed = _elapsed - Mathf.Max(SpotlightDelay, 0f);

			// 节点总时长到期：兜底强制销毁一切
			if (_totalTimer <= 0f)
			{
				QueueFree();
				return;
			}

			// 判定带按世界方向定位（根当前位置 + 初始偏移）——跟随移动保持，根翻转不影响 Y 偏移
			if (_hitArea != null && _hitAreaLocalOffset != Vector2.Zero)
				_hitArea.GlobalPosition = GlobalPosition + _hitAreaLocalOffset;

			// 光束生命周期：BeamDelay → Grow → Beam（全亮）→ Fade
			float beamTotal = GrowDuration + BeamDuration + FadeDuration;
			bool beamFinished = _beamPhaseElapsed >= beamTotal;

			if (!beamFinished)
			{
				// 光束淡出：shader fade（仅光束层），淡出期 = 光束生命周期的最后 FadeDuration 秒
				float fadeStart = GrowDuration + BeamDuration;
				if (_beamPhaseElapsed > fadeStart && FadeDuration > 0f)
				{
					float t = 1f - Mathf.Clamp((_beamPhaseElapsed - fadeStart) / FadeDuration, 0f, 1f);
					SetBeamFade(t);
				}

				UpdateBeam();

				// 生长完成后触发伤害，与视觉同步（子类实现）
				if (!_hasDamaged && _beamPhaseElapsed >= GrowDuration)
					OnBeamGrown();
			}

			// 光点独立生命周期（与光束并行，互不影响）：SpotlightDelay → 淡入 → 存活 → 淡出
			UpdateSpotlight();

			// 节点销毁：光束结束 且 光点结束（或不存在）
			if (beamFinished && (_spotlight == null || !IsInstanceValid(_spotlight)))
			{
				QueueFree();
				return;
			}
		}

		/// <summary>子类覆写：首帧方向初始化（LaserBeamA 按朝向设旋转；PlayerWeapon 迁移 GlobalRotation）。</summary>
		protected virtual void InitializeDirection() { }

		/// <summary>子类覆写：光束生长完成后的伤害/击退。</summary>
		protected virtual void OnBeamGrown() { }

		/// <summary>光束长度/宽度动画 + 判定带同步扩展。</summary>
		protected virtual void UpdateBeam()
		{
			// 光束恒为 MaxLength 全长（不撞墙、不撞目标截断），仅保留生长动画
			// 阶段时间 < 0（BeamDelay 内）→ grow 为 0，长度 MinLength、宽度 0（不可见）
			float phase = Mathf.Max(_beamPhaseElapsed, 0f);
			float grow = GrowDuration > 0f ? Mathf.Clamp(phase / GrowDuration, 0f, 1f) : 1f;
			_currentLength = Mathf.Lerp(MinLength, MaxLength, grow);

			// 宽度动画：生长阶段 0 → 目标宽度；淡出阶段 目标宽度 → 0（与 shader fade 的透明度叠加）
			float widthFactor = grow;
			float fadeStart = GrowDuration + BeamDuration;
			if (FadeDuration > 0f && phase > fadeStart)
				widthFactor *= 1f - Mathf.Clamp((phase - fadeStart) / FadeDuration, 0f, 1f);

			if (_glowSprite != null)
			{
				_glowSprite.Position = Vector2.Zero;
				_glowSprite.Scale = new Vector2(_currentLength / _texWidth, GlowWidth * widthFactor / _texHeight);
			}

			if (_beamSprite != null)
			{
				_beamSprite.Position = Vector2.Zero;
				_beamSprite.Scale = new Vector2(_currentLength / _texWidth, BeamWidth * widthFactor / _texHeight);
			}

			if (_spotlight != null)
				_spotlight.Position = Vector2.Zero;
			if (_spotGlowSprite != null)
				_spotGlowSprite.Position = Vector2.Zero;

			// 判定带随光束生长同步扩展（Area2D 物理重叠由引擎维护）；带从发射点向一端伸展，方向由判定带旋转驱动
			if (_hitShape?.Shape is RectangleShape2D rs)
			{
				rs.Size = new Vector2(_currentLength, DetectionRadius * 2f);
				_hitShape.Position = new Vector2(_currentLength * 0.5f, 0f);
			}
		}

		/// <summary>
		/// 光点独立生命周期（每帧驱动，不用 tween）：内圈（Spotlight）与外圈（SpotGlow）同步 alpha。
		/// </summary>
		private void UpdateSpotlight()
		{
			if (_spotlight == null || !IsInstanceValid(_spotlight)) return;

			// 阶段时间 < 0（SpotlightDelay 内）→ alpha 0（不可见）
			float phase = Mathf.Max(_spotlightPhaseElapsed, 0f);

			float spotFadeIn = Mathf.Max(SpotlightFadeIn, 0f);
			float spotDuration = Mathf.Max(SpotlightDuration, 0f);
			float spotFadeOut = Mathf.Max(SpotlightFadeOut, 0f);

			float alpha;
			bool finished = false;
			if (phase < spotFadeIn)
			{
				// 淡入：alpha 0 → 1
				alpha = spotFadeIn > 0f ? Mathf.Clamp(phase / spotFadeIn, 0f, 1f) : 1f;
			}
			else if (phase >= spotFadeIn + spotDuration)
			{
				// 淡出：alpha 1 → 0
				float fadeOutT = spotFadeOut > 0f
					? Mathf.Clamp((phase - spotFadeIn - spotDuration) / spotFadeOut, 0f, 1f)
					: 1f;
				alpha = 1f - fadeOutT;
				finished = fadeOutT >= 1f;
			}
			else
			{
				// 存活期：保持全亮
				alpha = 1f;
			}

			var sc = _spotlight.Modulate;
			_spotlight.Modulate = new Color(sc.R, sc.G, sc.B, alpha);
			if (_spotGlowSprite != null && IsInstanceValid(_spotGlowSprite))
			{
				var gc = _spotGlowSprite.Modulate;
				_spotGlowSprite.Modulate = new Color(gc.R, gc.G, gc.B, alpha);
			}

			if (finished)
			{
				_spotlight.QueueFree();
				_spotlight = null;
				if (_spotGlowSprite != null && IsInstanceValid(_spotGlowSprite))
				{
					_spotGlowSprite.QueueFree();
					_spotGlowSprite = null;
				}
			}
		}

		/// <summary>设置光束 shader fade（1 = 全亮，0 = 灭）。</summary>
		protected void SetBeamFade(float t)
		{
			if (_glowSprite?.Material is ShaderMaterial gm)
				gm.SetShaderParameter("fade", t);
			if (_beamSprite?.Material is ShaderMaterial bm)
				bm.SetShaderParameter("fade", t);
		}

		private T? ResolveNode<T>(NodePath path, string fallbackName) where T : Node
		{
			if (path != null && !path.IsEmpty)
				return GetNodeOrNull<T>(path);
			return GetNodeOrNull<T>(fallbackName);
		}
	}
}
