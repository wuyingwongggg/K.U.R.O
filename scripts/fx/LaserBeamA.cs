using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	public partial class LaserBeamA : Node2D, IFacingDirectional, IAttackerProvider
	{
		[ExportCategory("Nodes")]
		/// <summary>光晕层节点路径（Sprite2D）。</summary>
		[Export] public NodePath GlowSpritePath { get; set; } = new("GlowSprite");
		/// <summary>核心光束层节点路径（Sprite2D）。</summary>
		[Export] public NodePath BeamSpritePath { get; set; } = new("BeamSprite");
		/// <summary>发光点节点路径（Sprite2D，独立生命周期）。</summary>
		[Export] public NodePath SpotlightPath { get; set; } = new("Spotlight");
		/// <summary>光束判定区节点路径（Area2D，判定带随光束生长）。</summary>
		[Export] public NodePath HitAreaPath { get; set; } = new("BeamHitArea");

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
		/// <summary>地面判定带垂直半高（像素）：玩家 HitArea 与此带重叠即命中。</summary>
		[Export(PropertyHint.Range, "10,500,1")] public float DetectionRadius = 150f;

		[ExportCategory("Knockback")]
		[Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;
		[Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
		[Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 0f;

		[ExportCategory("Targeting")]
		/// <summary>自动瞄准：射出前物理查询前方可攻击对象，光束向目标微倾斜。false = 保持 FacingRight 水平方向。</summary>
		[Export] public bool AutoAimAtPlayer = true;
		/// <summary>垂直倾斜上限（度）：水平基础方向固定，仅在 ±此值内跟随目标高度微调（只影响视觉，判定带保持水平）。</summary>
		[Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 5f;
		/// <summary>水平基础朝向：true = 向右（0°），false = 向左（180°）。由生成方（EnemyAttackTemplate）按敌人朝向设置。</summary>
		[Export] public bool FacingRight { get; set; } = true;

		private Sprite2D? _glowSprite;
		private Sprite2D? _beamSprite;
		private Sprite2D? _spotlight;
		private float _timer;
		private float _currentLength;
		private float _texWidth;
		private float _texHeight;
		private bool _pendingAutoAim;
		private bool _hasDamaged;
		private GameActor? _attacker;
		private Area2D? _hitArea;
		private CollisionShape2D? _hitShape;
		private Node2D? _visual;
		private float _emitElapsed;      // 射出后已过时间（延迟期间为负/0，生长/淡入基于此）
		private bool _emitted;           // 是否已射出（延迟结束置位，首次显示）

		public override void _Ready()
		{
			// 节点引用全部走导出路径（可重命名场景节点，无需改脚本）
			_glowSprite = GlowSpritePath != null && !GlowSpritePath.IsEmpty ? GetNodeOrNull<Sprite2D>(GlowSpritePath) : null;
			_beamSprite = BeamSpritePath != null && !BeamSpritePath.IsEmpty ? GetNodeOrNull<Sprite2D>(BeamSpritePath) : null;
			_spotlight = SpotlightPath != null && !SpotlightPath.IsEmpty ? GetNodeOrNull<Sprite2D>(SpotlightPath) : null;
			_visual = GetNodeOrNull<Node2D>("Visual");
			_hitArea = HitAreaPath != null && !HitAreaPath.IsEmpty ? GetNodeOrNull<Area2D>(HitAreaPath) : null;
			if (_hitArea != null)
			{
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

			// 根恒 0 旋转（旋转只作用于 Visual/判定带）：初始方向按 FacingRight
			float initAngle = FacingRight ? 0f : Mathf.Pi;
			if (_visual != null) _visual.Rotation = initAngle;
			if (_hitArea != null) _hitArea.Rotation = initAngle;

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
				if (FindAimTarget() is Vector2 aimTarget)
					AimHorizontalWithVerticalTilt(aimTarget);
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
			// 旋转只作用于视觉层与判定带（根恒 0 旋转，Visual 位置不随朝向翻转到 +200）：
			// 判定带跟随瞄准角度旋转，与视觉光束方向一致
			float angle = baseAngle + tilt;
			if (_visual != null) _visual.Rotation = angle;
			else Rotation = angle; // 兼容无 Visual 节点的旧结构
			if (_hitArea != null) _hitArea.Rotation = angle;
		}

		public void LookAtGlobal(Vector2 globalTarget)
		{
			Vector2 dir = (globalTarget - GlobalPosition).Normalized();
			if (dir == Vector2.Zero) return;
			if (_visual != null) _visual.Rotation = dir.Angle();
			else Rotation = dir.Angle();
		}

		private void UpdateBeam()
		{
			// 光束恒为 MaxLength 全长（不撞墙、不撞目标截断），仅保留生长动画
			float rawLength = MaxLength;

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

			// 判定带随光束生长同步扩展（Area2D 物理重叠由引擎维护）；带从发射点向一端伸展，方向由判定带旋转驱动
			if (_hitShape?.Shape is RectangleShape2D rs)
			{
				rs.Size = new Vector2(_currentLength, DetectionRadius * 2f);
				_hitShape.Position = new Vector2(_currentLength * 0.5f, 0f);
			}
		}

		private void TryDamagePlayer()
		{
			if (_hasDamaged) return;
			if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
			if (_hitArea == null) return;

			// 俯视角地面判定（Area2D 物理重叠，与子弹同构）：判定带 = 光束水平段 × DetectionRadius 垂直容差，
			// 随光束生长同步扩展（UpdateBeam），方向由判定带旋转（= 瞄准角度）驱动。
			// 击退沿地面水平分量（俯视角）
			float beamAngle = _hitArea?.Rotation ?? (FacingRight ? 0f : Mathf.Pi);
			Vector2 beamDir = new(Mathf.Cos(beamAngle), 0f);
			if (beamDir == Vector2.Zero) beamDir = new Vector2(FacingRight ? 1f : -1f, 0f);

			var damaged = new HashSet<ulong>();
			// Area 目标：只接受受击判定区（HitArea/TriggerArea），玩家攻击/交互 Area 探入光束不触发（同 Boomerang 过滤）
			foreach (var area in _hitArea.GetOverlappingAreas())
			{
				if (area.Name != "HitArea" && area.Name != "TriggerArea") continue;
				TryDamageReceiver(area, beamDir, damaged);
			}
			// Body 目标（DestructibleObject 等 StaticBody2D）
			foreach (var body in _hitArea.GetOverlappingBodies())
				TryDamageReceiver(body, beamDir, damaged);

			_hasDamaged = true;
		}

		private void TryDamageReceiver(Node collider, Vector2 beamDir, HashSet<ulong> damaged)
		{
			// 阵营过滤（ResolveDamageReceiver）→ 接收者解析（GameActor 或带 TakeDamage 的节点，如 WorldItem）+ 去重
			if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver)
				return;
			if (!damaged.Add(receiver.GetInstanceId())) return;

			bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, _attacker,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage);
			if (!dealt) return;

			// 击退只对 GameActor（WorldItem 无速度概念）
			if (receiver is GameActor actor)
			{
				float knockSpeed = KnockbackSpeed > 0f
					? KnockbackSpeed
					: (KnockbackDistance > 0f ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f) : 0f);
				if (knockSpeed > 0f) actor.ApplyKnockback(beamDir, knockSpeed);
			}
		}

		/// <summary>
		/// 瞄准目标：玩家组查找（同 DamageDispatcher.DealDamageFromArea 玩家发现先例）。
		/// 物理查询受带高/距离/方向限制，大部分时候检测不到玩家——瞄准必须全局可靠。
		/// 目标在光束背后（FacingRight 反侧）时由 AimHorizontalWithVerticalTilt 的 front 检查保持水平。
		/// </summary>
		private Vector2? FindAimTarget()
		{
			var player = GetTree().GetFirstNodeInGroup("player");
			return player is GameActor ga ? GetAimCenter(ga) : null;
		}

		/// <summary>取目标 HitArea CollisionShape2D 的世界坐标作为瞄准中心。</summary>
		private static Vector2 GetAimCenter(GameActor actor)
		{
			var hitArea = actor.GetNodeOrNull<Area2D>("HitArea")
				?? actor.FindChild("HitArea", recursive: true, owned: false) as Area2D;
			var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
			return hitShape?.GlobalPosition ?? hitArea?.GlobalPosition ?? actor.GlobalPosition;
		}

		/// <summary>
		/// 显式攻击来源（由生成方传入，如 EnemyAttackTemplate 生成时设置）。
		/// 优先于父节点解析：父节点下第一个敌人不一定是发射者，解析错误会导致 AllowSelfDamage 保护失效（打自己）。
		/// </summary>
		public GameActor? Attacker
		{
			get => _attacker;
			set => _attacker = value;
		}

		private void ResolveAttacker()
		{
			if (_attacker != null) return;
			var parent = GetParent();
			if (parent == null) return;
			foreach (var child in parent.GetChildren())
			{ if (child.IsInGroup("enemies") && child is GameActor ga) { _attacker = ga; break; } }
		}
	}
}
