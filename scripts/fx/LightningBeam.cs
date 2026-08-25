using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
	/// <summary>
	/// 纯视觉闪电光束效果（伤害判定由宿主的 AttackArea 负责，本项目统一解耦模式）。
	/// </summary>
	public partial class LightningBeam : Node2D, IFacingDirectional
	{
		[ExportCategory("Beam")]
		[Export] public float MaxLength = 3000f;
		[Export(PropertyHint.Range, "0,5,0.05")] public float GrowDuration = 0.1f;
		[Export(PropertyHint.Range, "0,3000,10")] public float MinLength = 0f;

		[ExportCategory("Plasma")]
		/// <summary>随机电弧模式：以节点为中心向 360° 随机角度生成 ArcCount 条闪电。</summary>
		[Export] public bool RandomArcMode = false;
		[Export(PropertyHint.Range, "1,64,1")] public int ArcCount = 1;

		[ExportCategory("Spin")]
		/// <summary>每脉冲以生成原点为中心旋转（时钟指针式）。角速度范围（度/秒，可负值反向），均为 0 = 不旋转。</summary>
		[Export(PropertyHint.Range, "-1080,1080,10")] public float MinAngularVelocity = 0f;
		[Export(PropertyHint.Range, "-1080,1080,10")] public float MaxAngularVelocity = 0f;

		[ExportCategory("Timing")]
		/// <summary>单条闪电实际展示时长范围：完整生命周期 = GrowDuration + lifetime + FadeDuration（Min == Max 时固定）。</summary>
		[Export(PropertyHint.Range, "0.05,10,0.05")] public float MinLifetime = 0.45f;
		[Export(PropertyHint.Range, "0.05,10,0.05")] public float MaxLifetime = 0.45f;
		/// <summary>脉冲末尾缩回时长：最后 FadeDuration 秒内射线缩回原点（替代淡出）。</summary>
		[Export] public float FadeDuration = 0.15f;
		/// <summary>总存在时间：0 = 永久存在（脉冲不断），> 0 = 持续 N 秒后销毁自身。</summary>
		[Export(PropertyHint.Range, "0,60,0.5")] public float Duration = 0f;

		[ExportCategory("Targeting")]
		[Export] public bool AutoAimAtPlayer = true;
		[Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 5f;
		[Export] public bool FacingRight { get; set; } = true;

		private RayCast2D? _ray;
		private Sprite2D? _lightningSprite;
		private float _timer;
		private float _elapsed;
		private float _currentLength;
		private float _angularVelocity;
		private float _pulseLifetime;
		private float _pulseTotal;
		private float _texWidth;
		private float _texHeight;
		private bool _pendingAutoAim;

		private struct ArcData
		{
			public Sprite2D Sprite;
			public float Direction;
			public float TargetLength;
			public float CurrentLength;
		}

		private readonly System.Collections.Generic.List<ArcData> _arcs = new();

		public override void _Ready()
		{
			_lightningSprite = GetNodeOrNull<Sprite2D>("LightningSprite");

			if (RandomArcMode)
			{
				if (_lightningSprite == null)
				{
					GD.PushWarning("[LightningBeam] 缺少 LightningSprite");
					QueueFree();
					return;
				}
				_texWidth = Mathf.Max(_lightningSprite.Texture?.GetWidth() ?? 2f, 2f);
				_texHeight = Mathf.Max(_lightningSprite.Texture?.GetHeight() ?? 2f, 2f);
				_lightningSprite.Visible = false; // 仅作为模板，随机电弧为子节点

				// 预创建电弧 Sprite 池：每脉冲只更新参数（方向/长度/seed），
				// 不再创建/销毁节点与复制材质——消除长时间存在时的周期性 GC/分配峰值
				var templateMat = _lightningSprite.Material as ShaderMaterial;
				for (int i = 0; i < ArcCount; i++)
				{
					var sprite = new Sprite2D
					{
						Texture = _lightningSprite.Texture,
						Scale = new Vector2(1f, _lightningSprite.Scale.Y),
						ZIndex = _lightningSprite.ZIndex,
					};
					if (templateMat != null)
					{
						var mat = (ShaderMaterial)templateMat.Duplicate();
						mat.SetShaderParameter("seed", GD.Randf());
						sprite.Material = mat;
					}
					AddChild(sprite);
					_arcs.Add(new ArcData { Sprite = sprite, Direction = 0f, TargetLength = 0f, CurrentLength = 0f });
				}
				SpawnRandomArcs(); // 首次就位
			}
			else
			{
				_ray = GetNodeOrNull<RayCast2D>("RayCast2D");
				if (_ray == null || _lightningSprite == null)
				{
					GD.PushWarning("[LightningBeam] 缺少子节点");
					QueueFree();
					return;
				}

				if (_lightningSprite.Material is ShaderMaterial sm)
					_lightningSprite.Material = (ShaderMaterial)sm.Duplicate();

				_texWidth = Mathf.Max(_lightningSprite.Texture?.GetWidth() ?? 2f, 2f);
				_texHeight = Mathf.Max(_lightningSprite.Texture?.GetHeight() ?? 2f, 2f);

				_lightningSprite.Scale = new Vector2(MinLength / _texWidth, _lightningSprite.Scale.Y);

				_ray.TargetPosition = new Vector2(MaxLength, 0f);
				_ray.Enabled = true;
			}

			_pulseLifetime = RollPulseLifetime();
			_pulseTotal = GrowDuration + _pulseLifetime + FadeDuration;
			// 相位随机：错开多个实例的脉冲重建/射线峰值，避免周期性同步卡顿
			_timer = _pulseTotal * GD.Randf();
			_angularVelocity = RollAngularVelocity();
			_pendingAutoAim = AutoAimAtPlayer;
		}

		/// <summary>
		/// 随机电弧更新（复用预创建池）：以节点（中心点）为原点，向 360° 随机角度更新 ArcCount 条闪电，
		/// 每条长度在 [MinLength, MaxLength] 间随机（撞墙截断），
		/// 每条形状由独立随机 seed 驱动（需要 shader 的 seed uniform）。
		/// </summary>
		public void SpawnRandomArcs()
		{
			float texW = Mathf.Max(_texWidth, 2f);

			for (int i = 0; i < _arcs.Count; i++)
			{
				float dir = GD.Randf() * Mathf.Tau;
				float targetLen = Mathf.Lerp(MinLength, MaxLength, GD.Randf());
				float wallLen = ProbeWallDistance(dir, MaxLength);
				if (wallLen < targetLen) targetLen = wallLen;

				var arc = _arcs[i];
				arc.Direction = dir;
				arc.TargetLength = targetLen;
				arc.CurrentLength = 0f;
				// 电弧一端固定在节点（原点），沿 dir 方向朝外延伸到 len：
				// 精灵中心沿 dir 前移 len/2（父空间坐标，必须乘方向向量），
				// 使闪电（本地 x=0 处）覆盖 原点..原点+dir*len
				arc.Sprite.Position = new Vector2(Mathf.Cos(dir), Mathf.Sin(dir)) * (targetLen * 0.5f);
				arc.Sprite.Rotation = dir;
				arc.Sprite.Scale = new Vector2(Mathf.Max(targetLen, 1f) / texW, arc.Sprite.Scale.Y);
				if (arc.Sprite.Material is ShaderMaterial mat)
					mat.SetShaderParameter("seed", GD.Randf());
				_arcs[i] = arc;
			}
		}

		public override void _Process(double delta)
		{
			if (_pendingAutoAim && _ray != null)
			{
				_pendingAutoAim = false;
				var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
				if (player != null)
				{
					var hitArea = player.GetNodeOrNull<Area2D>("HitArea")
						?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
					var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
					Vector2 aimTarget = hitShape?.GlobalPosition ?? hitArea?.GlobalPosition ?? player.GlobalPosition;
					AimHorizontalWithVerticalTilt(aimTarget);
				}
				UpdateBeam();
			}

			_elapsed += (float)delta;
			// Duration = 0 为永久存在；> 0 时总时长结束销毁自身
			if (Duration > 0f && _elapsed >= Duration) { QueueFree(); return; }

			_timer -= (float)delta;

			// 当前脉冲结束（缩回完成）→ 立即开始下一脉冲：重新生成 → 生长 → 缩回
			if (_timer <= 0f)
			{
				StartPulse();
				_timer = Mathf.Max(_pulseTotal, 0.05f);
			}

			// 每脉冲以生成原点为中心旋转（时钟指针式）
			if (_angularVelocity != 0f)
				Rotation += Mathf.DegToRad(_angularVelocity) * (float)delta;

			if (_arcs.Count > 0)
				UpdateArcs();
			else
				UpdateBeam();
		}

		/// <summary>
		/// 开始下一脉冲：电弧模式复用池更新随机电弧；
		/// 光束模式重新生长。角速度与脉冲时长随脉冲重置。
		/// </summary>
		private void StartPulse()
		{
			_angularVelocity = RollAngularVelocity();
			_pulseLifetime = RollPulseLifetime();
			_pulseTotal = GrowDuration + _pulseLifetime + FadeDuration;
			if (_arcs.Count > 0)
				SpawnRandomArcs();
		}

		/// <summary>在 [MinAngularVelocity, MaxAngularVelocity] 间随机本脉冲角速度（度/秒，可负值反向）。</summary>
		private float RollAngularVelocity()
		{
			return MinAngularVelocity + GD.Randf() * (MaxAngularVelocity - MinAngularVelocity);
		}

		/// <summary>在 [MinLifetime, MaxLifetime] 间随机单条闪电的实际展示时长（秒，不含生长/淡出）。</summary>
		private float RollPulseLifetime()
		{
			return MinLifetime + GD.Randf() * (MaxLifetime - MinLifetime);
		}

		/// <summary>
		/// 沿 dir 方向发射射线探测墙壁（避开角色），返回可延伸的最大长度。
		/// 碰撞层复用场景中 RayCast2D 的配置。
		/// </summary>
		private float ProbeWallDistance(float dir, float maxLen)
		{
			var space = GetWorld2D().DirectSpaceState;
			float worldAngle = Rotation + dir;
			var query = new PhysicsRayQueryParameters2D
			{
				From = GlobalPosition,
				To = GlobalPosition + new Vector2(Mathf.Cos(worldAngle), Mathf.Sin(worldAngle)) * maxLen,
				CollideWithBodies = true,
				CollideWithAreas = false,
				CollisionMask = _ray?.CollisionMask ?? 4,
			};
			var result = space.IntersectRay(query);
			if (result.Count == 0 || !result.TryGetValue("collider", out var collider))
				return maxLen;
			if (collider.As<GodotObject>() is GameActor)
				return maxLen;
			return result.TryGetValue("position", out var pos)
				? GlobalPosition.DistanceTo(pos.AsVector2())
				: maxLen;
		}

		/// <summary>
		/// 本脉冲长度缩放系数：前 GrowDuration 秒从 0 生长到 1，
		/// 末尾 FadeDuration 秒从 1 缩回 0（射线缩回原点）。
		/// </summary>
		private float PulseLengthFactor()
		{
			float elapsed = _pulseTotal - _timer;
			float growT = GrowDuration > 0f ? Mathf.Clamp(elapsed / GrowDuration, 0f, 1f) : 1f;
			float retractT = FadeDuration > 0f ? Mathf.Clamp((_pulseTotal - elapsed) / FadeDuration, 0f, 1f) : 1f;
			return Mathf.Min(growT, retractT);
		}

		/// <summary>
		/// 随机电弧长度随时间变化：生长到目标长度，末尾缩回原点。
		/// </summary>
		private void UpdateArcs()
		{
			float factor = PulseLengthFactor();
			for (int i = 0; i < _arcs.Count; i++)
			{
				var arc = _arcs[i];
				arc.CurrentLength = arc.TargetLength * factor;
				arc.Sprite.Position = new Vector2(Mathf.Cos(arc.Direction), Mathf.Sin(arc.Direction)) * (arc.CurrentLength * 0.5f);
				arc.Sprite.Scale = new Vector2(Mathf.Max(arc.CurrentLength, 1f) / _texWidth, arc.Sprite.Scale.Y);
				_arcs[i] = arc;
			}
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

			_currentLength = Mathf.Lerp(MinLength, rawLength, PulseLengthFactor());

			_lightningSprite.Position = Vector2.Zero;
			_lightningSprite.Scale = new Vector2(_currentLength / _texWidth, _lightningSprite.Scale.Y);
		}

	}
}
