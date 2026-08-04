using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 放电核心弹跳攻击效果。
    ///
    /// 行为：
    ///   - 运动逻辑：沿当前方向周期性抛物线弹跳：y = H·|sin(πx/L)|·decayⁿ，高度逐次衰减。
    ///   - 弹跳途中接触其他非角色（墙/障碍）后：弹跳方向镜面反射，并从接触点沿新方向继续弹跳。
    ///   - 使用 Duration 控制生命周期，到期后销毁自身。
    /// </summary>
    public partial class ECoreAttackEffect : Node2D, IFacingDirectional
    {
        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,3000,10")] public float Speed = 600f;
        /// <summary>抛物线弹跳高度（像素）。</summary>
        [Export(PropertyHint.Range, "10,500,5")] public float BounceHeight = 150f;
        /// <summary>抛物线弹跳波长（像素，完成一次弹跳的水平距离）。</summary>
        [Export(PropertyHint.Range, "50,2000,10")] public float BounceLength = 400f;
        /// <summary>每次弹跳的高度衰减系数（1 = 不衰减，0.8 = 每次 ×0.8）。</summary>
        [Export(PropertyHint.Range, "0.2,1,0.05")] public float HeightDecayPerBounce = 0.8f;
        /// <summary>反弹冷却（秒），防止同一面连续反弹抖动。</summary>
        [Export(PropertyHint.Range, "0.05,0.5,0.01")] public float BounceCooldown = 0.1f;
        /// <summary>自转角速度（度/秒），0 = 不旋转。</summary>
        [Export(PropertyHint.Range, "0,3600,10")] public float RotationSpeed = 720f;
        /// <summary>只有该节点自转（null = 不旋转）。</summary>
        [Export] public Node2D? RotateNode = null;

        [ExportCategory("Direction")]
        [Export] public bool FacingRight { get; set; } = true;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 2.0f;

        [ExportCategory("PeriodicSpawn")]
        /// <summary>每 SpawnInterval 秒在本特效当前位置生成一次的子特效场景（null = 关闭）。</summary>
        [Export] public PackedScene? PeriodicSpawnScene = null;
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float SpawnInterval = 1.0f;

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _bounceOrigin;
        private float _bounceDistance;
        private float _elapsed;
        private Vector2 _travelDir = Vector2.Right;
        private float _bounceCooldownRemaining;
        private bool _initialized;
        private bool _facingRight = true;
        private float _spawnAccum;

        public override void _Ready()
        {
            _facingRight = ResolveFacingRight();
            _travelDir = _facingRight ? Vector2.Right : Vector2.Left;
        }

        /// <summary>
        /// 解析初始弹跳方向：优先从父节点查找 "player" 组 GameActor（生成方是玩家投掷物），
        /// 使用其 FacingRight；找不到时回退 FacingRight 属性。
        /// </summary>
        private bool ResolveFacingRight()
        {
            var parent = GetParent();
            if (parent != null)
            {
                foreach (var child in parent.GetChildren())
                {
                    if (child.IsInGroup("player") && child is GameActor ga)
                        return ga.FacingRight;
                }
            }
            return FacingRight;
        }

        public override void _PhysicsProcess(double delta)
        {
            // 首帧初始化起点：生成方（RigidBodyWorldItemEntity）在 AddChild 之后才设置
            // GlobalPosition，_Ready 时快照会是 (0,0)，必须延迟到首帧
            if (!_initialized)
            {
                _initialized = true;
                _bounceOrigin = GlobalPosition;
            }

            if (_elapsed >= Duration)
            {
                QueueFree();
                return;
            }
            _elapsed += (float)delta;
            _bounceCooldownRemaining -= (float)delta;

            // 只有 RotateNode 自转（根节点不动，攻击判定/残影不跟着转），
            // 旋转方向跟随实时移动方向（反弹后自动反转，滚动感）
            if (RotateNode != null && RotationSpeed != 0f && _travelDir.X != 0f)
                RotateNode.Rotation += Mathf.DegToRad(RotationSpeed) * (float)delta * Mathf.Sign(_travelDir.X);

            TickBounce((float)delta);

            if (PeriodicSpawnScene != null && SpawnInterval > 0f)
            {
                _spawnAccum += (float)delta;
                while (_spawnAccum >= SpawnInterval)
                {
                    _spawnAccum -= SpawnInterval;
                    SpawnPeriodicEffect();
                }
            }
        }

        /// <summary>
        /// 生成子特效作为本特效的子节点（本地原点），跟随本特效一起移动/销毁，
        /// 朝向同步本特效朝向。
        /// </summary>
        private void SpawnPeriodicEffect()
        {
            if (PeriodicSpawnScene == null) return;

            var node = PeriodicSpawnScene.Instantiate();
            if (node is not Node2D node2D) { node?.QueueFree(); return; }

            AddChild(node2D);
            if (node2D is IFacingDirectional facing)
                facing.FacingRight = _facingRight;
        }

        /// <summary>
        /// 沿 _travelDir 方向周期性弹跳：y = H·|sin(πx/L)|·decayⁿ。
        /// 前方检测到非角色物理体时：方向镜面反射，从接触点沿新方向继续弹跳。
        /// </summary>
        private void TickBounce(float dt)
        {
            _bounceDistance += Speed * dt;

            float len = Mathf.Max(BounceLength, 0.01f);
            int bounce = Mathf.FloorToInt(_bounceDistance / len);
            float localX = _bounceDistance - bounce * len;
            float decay = Mathf.Pow(HeightDecayPerBounce, bounce);
            // 相位偏移版 |sin| 曲线（周期边界峰顶、中间谷底）整体下移：
            // 生成点（localX=0，峰顶）落在原点高度 0，谷底穿到起点下方
            float height = BounceHeight * decay * (
                Mathf.Abs(Mathf.Sin(Mathf.Pi * (localX + len * 0.5f) / len)) - 1f
            );

            GlobalPosition = _bounceOrigin + _travelDir * _bounceDistance + new Vector2(0f, -height);

            if (ProbeCollision(GlobalPosition, _travelDir.X * Speed) && _bounceCooldownRemaining <= 0f)
            {
                _bounceCooldownRemaining = BounceCooldown;
                // 反射弹跳方向，从当前接触点沿新方向继续弹跳
                _travelDir = _travelDir - 2f * _travelDir.Dot(_lastHitNormal) * _lastHitNormal;
                if (_travelDir == Vector2.Zero)
                    _travelDir = -_lastHitNormal;
                _bounceOrigin = GlobalPosition;
                _bounceDistance = 0f;
            }
        }

        // ── 碰撞检测 ──────────────────────────────────────────────

        private Vector2 _lastHitNormal = Vector2.Zero;

        /// <summary>
        /// 沿移动方向发射短射线检测前方非角色物理体（墙/障碍）。
        /// 命中则记录法线并返回 true。
        /// </summary>
        private bool ProbeCollision(Vector2 from, float horizontalVelocity)
        {
            if (horizontalVelocity == 0f) return false;

            float step = Mathf.Abs(horizontalVelocity) * 0.05f + 20f;
            var space = GetWorld2D().DirectSpaceState;
            var query = new PhysicsRayQueryParameters2D
            {
                From = from,
                To = from + new Vector2(Mathf.Sign(horizontalVelocity) * step, 0f),
                CollideWithBodies = true,
                CollideWithAreas = false
            };

            var result = space.IntersectRay(query);
            if (result.Count == 0 || !result.TryGetValue("collider", out var collider))
                return false;

            var body = collider.As<GodotObject>();
            if (body is GameActor) return false;

            if (result.TryGetValue("normal", out var normal))
                _lastHitNormal = normal.AsVector2();
            return _lastHitNormal != Vector2.Zero;
        }
    }
}
