using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
	/// <summary>
	/// 激光束（敌人通用）：视觉/计时继承 <see cref="LaserBeamVisualBase"/>（光束 Grow→Beam→Fade、
	/// 光点独立生命周期），本类负责：朝向初始化 + 自动瞄准（AutoAimAtPlayer）、
	/// 伤害/击退（TargetableFactions 阵营过滤）。
	/// </summary>
	public partial class LaserBeamA : LaserBeamVisualBase, IFacingDirectional, IAttackerProvider
	{
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
		/// <summary>自动瞄准：射出前物理查询前方可攻击对象，光束向目标微倾斜。false = 保持 FacingRight 水平方向。</summary>
		[Export] public bool AutoAimAtPlayer = true;
		/// <summary>垂直倾斜上限（度）：水平基础方向固定，仅在 ±此值内跟随目标高度微调（只影响视觉，判定带保持水平）。</summary>
		[Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 5f;
		/// <summary>水平基础朝向：true = 向右（0°），false = 向左（180°）。由生成方（EnemyAttackTemplate）按敌人朝向设置。</summary>
		[Export] public bool FacingRight { get; set; } = true;

		private GameActor? _attacker;

		/// <summary>已伤害目标（跨帧去重）：光束持续阶段每帧检测，每目标每光束最多一次伤害。</summary>
		private readonly HashSet<ulong> _damaged = new();

		public override void _Ready()
		{
			base._Ready();
			ResolveAttacker();
		}

		/// <summary>
		/// 首帧方向初始化：按 FacingRight 设基础朝向；启用自动瞄准时对目标微倾斜。
		/// </summary>
		protected override void InitializeDirection()
		{
			// 根恒 0 旋转（旋转只作用于 Visual/判定带）：初始方向按 FacingRight
			float initAngle = FacingRight ? 0f : Mathf.Pi;
			if (_visual != null) _visual.Rotation = initAngle;
			else Rotation = initAngle; // 兼容无 Visual 节点的旧结构
			if (_hitArea != null) _hitArea.Rotation = initAngle;

			if (AutoAimAtPlayer && FindAimTarget() is Vector2 aimTarget)
				AimHorizontalWithVerticalTilt(aimTarget);
		}

		protected override void OnBeamGrown()
		{
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
			// 旋转只作用于视觉层与判定带（根恒 0 旋转）：判定带跟随瞄准角度旋转，与视觉光束方向一致
			float angle = baseAngle + tilt;
			if (_visual != null) _visual.Rotation = angle;
			else Rotation = angle;
			if (_hitArea != null) _hitArea.Rotation = angle;
		}

		public void LookAtGlobal(Vector2 globalTarget)
		{
			Vector2 dir = (globalTarget - GlobalPosition).Normalized();
			if (dir == Vector2.Zero) return;
			if (_visual != null) _visual.Rotation = dir.Angle();
			else Rotation = dir.Angle();
		}

		/// <summary>
		/// 伤害检测（Grow 完成瞬间 + Beam 持续阶段每帧）：跨帧去重——每目标每光束最多一次伤害，
		/// 光束持续期间走进光束的目标也能命中（原只在生长完成瞬间检测一次）。
		/// </summary>
		private void TryDamagePlayer()
		{
			if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
			if (_hitArea == null) return;

			// 俯视角地面判定（Area2D 物理重叠）：判定带 = 光束水平段 × DetectionRadius 垂直容差，
			// 随光束生长同步扩展（基类 UpdateBeam），方向由判定带旋转（= 瞄准角度）驱动。
			// 击退沿地面水平分量（俯视角）
			float beamAngle = _hitArea?.Rotation ?? (FacingRight ? 0f : Mathf.Pi);
			Vector2 beamDir = new(Mathf.Cos(beamAngle), 0f);
			if (beamDir == Vector2.Zero) beamDir = new Vector2(FacingRight ? 1f : -1f, 0f);

			// Area 目标：只接受受击判定区（HitArea/TriggerArea），玩家攻击/交互 Area 探入光束不触发
			foreach (var area in _hitArea.GetOverlappingAreas())
			{
				if (area.Name != "HitArea" && area.Name != "TriggerArea") continue;
				TryDamageReceiver(area, beamDir, _damaged);
			}
			// Body 目标（DestructibleObject 等 StaticBody2D）
			foreach (var body in _hitArea.GetOverlappingBodies())
				TryDamageReceiver(body, beamDir, _damaged);
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			// Beam 持续阶段（生长完成后、淡出结束前）每帧检测——走进光束的目标也能造成伤害
			if (_beamPhaseElapsed >= GrowDuration)
				TryDamagePlayer();
		}

		private void TryDamageReceiver(Node collider, Vector2 beamDir, HashSet<ulong> damaged)
		{
			// 阵营过滤（ResolveDamageReceiver）→ 接收者解析（GameActor 或带 TakeDamage 的节点）+ 去重
			if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions) is not Node receiver)
				return;
			if (!damaged.Add(receiver.GetInstanceId())) return;

			bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, _attacker,
				DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, null, beamDir);
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
