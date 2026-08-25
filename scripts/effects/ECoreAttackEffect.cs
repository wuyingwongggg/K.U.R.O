using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Events;

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
    public partial class ECoreAttackEffect : Node2D, IFacingDirectional, IAttackerProvider
    {
        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,3000,10")] public float Speed = 600f;
        /// <summary>抛物线弹跳高度（像素）。</summary>
        [Export(PropertyHint.Range, "10,500,5")] public float BounceHeight = 150f;
        /// <summary>抛物线弹跳波长（像素，完成一次弹跳的水平距离）。</summary>
        [Export(PropertyHint.Range, "50,2000,10")] public float BounceLength = 400f;
        /// <summary>每次弹跳的高度衰减系数（1 = 不衰减，0.8 = 每次 ×0.8）。</summary>
        [Export(PropertyHint.Range, "0.2,1,0.05")] public float HeightDecayPerBounce = 0.8f;
        /// <summary>true = 生成瞬间在底部（地面接触点，尖角反弹）向上弹起（标准弹跳：底部→顶点→底部）；false = 生成瞬间在顶部（旧行为：顶点→谷底→顶点）。</summary>
        [Export] public bool BounceUpwardFirst { get; set; } = true;
        /// <summary>反弹冷却（秒），防止同一面连续反弹抖动。</summary>
        [Export(PropertyHint.Range, "0.05,0.5,0.01")] public float BounceCooldown = 0.1f;
        /// <summary>反弹检测形状尺寸（矩形，与 AttackArea 同类的物理查询；应比 AttackArea 小，贴近视觉大小）。</summary>
        [Export] public Vector2 BounceDetectSize = new Vector2(80, 60);
        /// <summary>反弹检测形状中心在移动方向前方的偏移（像素）。</summary>
        [Export(PropertyHint.Range, "0,200,5")] public float BounceDetectAhead = 40f;
        /// <summary>绘制反弹检测范围（调试用，_Draw 每帧）。</summary>
        [Export] public bool ShowBounceDetectDebug { get; set; } = false;
        /// <summary>生成时/反弹时从墙内推出的最大距离（像素），超出仍未脱离则转入"墙内不反弹"保护（穿过墙）。</summary>
        [Export] public float BounceDepenetrateMaxDistance { get; set; } = 1000f;
        /// <summary>自转角速度（度/秒），0 = 不旋转。</summary>
        [Export(PropertyHint.Range, "0,3600,10")] public float RotationSpeed = 720f;
        /// <summary>只有该节点自转（null = 不旋转）。</summary>
        [Export] public Node2D? RotateNode = null;

        [ExportCategory("Direction")]
        [Export] public bool FacingRight { get; set; } = true;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 2.0f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy;
        [Export] public bool AllowSelfDamage { get; set; } = false;
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 10;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float DamageInterval = 0.5f;

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _bounceOrigin;
        private float _initialBaselineY;
        private float _bounceDistance;
        private float _elapsed;
        private Vector2 _travelDir = Vector2.Right;
        private float _bounceCooldownRemaining;
        private bool _initialized;
        private bool _facingRight = true;
        private Area2D? _attackArea;
        private Node2D? _visual; // 视觉层（弹跳高度只作用于它；判定 AttackArea 留根固定判定层）
        private RectangleShape2D? _bounceShape; // 反弹检测形状（比 AttackArea 小）
        private GameActor? _attacker;
        private readonly Dictionary<GameActor, float> _actorTimers = new();
        private readonly Dictionary<GameActor, int> _actorRefs = new();

        public override void _Ready()
        {
            _facingRight = ResolveFacingRight();
            _travelDir = _facingRight ? Vector2.Right : Vector2.Left;

            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            if (_attackArea != null)
            {
                _attackArea.BodyEntered += OnBodyEntered;
                _attackArea.BodyExited += OnBodyExited;
                _attackArea.AreaEntered += OnAreaEntered;
                _attackArea.AreaExited += OnAreaExited;
            }

            _visual = GetNodeOrNull<Node2D>("Visual");
            _bounceShape = new RectangleShape2D { Size = BounceDetectSize };

            ResolveAttacker();
        }

        public override void _ExitTree()
        {
            if (_attackArea != null)
            {
                _attackArea.BodyEntered -= OnBodyEntered;
                _attackArea.BodyExited -= OnBodyExited;
                _attackArea.AreaEntered -= OnAreaEntered;
                _attackArea.AreaExited -= OnAreaExited;
            }
            _actorTimers.Clear();
            _actorRefs.Clear();
            base._ExitTree();
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
                _initialBaselineY = GlobalPosition.Y;

                // 生成时若处于墙/障碍内，沿初始方向推出墙外（否则会在碰撞体内高频来回反弹）
                DepenetrateFromWalls();
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

            TickDamage((float)delta);

            if (ShowBounceDetectDebug)
                QueueRedraw();
        }

        // ── 区域计时伤害 ──────────────────────────────────────────

        private void TickDamage(float dt)
        {
            if (_actorTimers.Count == 0) return;

            var dead = new List<GameActor>();
            foreach (var (actor, timer) in _actorTimers)
            {
                if (!GodotObject.IsInstanceValid(actor) || actor.IsDeadOrDying)
                {
                    dead.Add(actor);
                    continue;
                }

                float accumulated = timer + dt;
                if (accumulated >= DamageInterval)
                {
                    _actorTimers[actor] = 0f;
                    DealDamageToActor(actor);
                }
                else
                {
                    _actorTimers[actor] = accumulated;
                }
            }

            foreach (var a in dead)
            {
                _actorTimers.Remove(a);
                _actorRefs.Remove(a);
            }
        }

        private void DealDamageToActor(GameActor actor)
        {
            // 全局最小伤害间隔：目标刚受过伤（含快速进出区域、被其他来源命中）时跳过，
            // 保证两次结算至少间隔 DamageInterval，堵住反复进出刷伤害的漏洞
            if (actor.GetSecondsSinceLastDamageTaken() < DamageInterval) return;

            // 用 AreaEffect 而非 DirectAttack：电核跳伤害是持续区域伤害，若标记为直接攻击，
            // 会触发玩家当前装备武器的全部 on-hit 效果（法棍/眩晕/击退等）——换装后电核残留的
            // 每跳都会被误认成"当前武器的攻击"，导致跨武器串效果。
            DamageDispatcher.DealDamage(actor, Damage, GlobalPosition, _attacker,
                DamageSource.AreaEffect, TargetableFactions, AllowSelfDamage, null);
        }

        // ── 碰撞回调 ──────────────────────────────────────────────

        private void OnBodyEntered(Node body)
        {
            if (body is not GameActor actor)
            {
                // WorldItem（DestructibleObject 等 TakeDamage 节点）：一次性结算（不计时）；
                // 墙/障碍无 TakeDamage 接口时 DealDamage 返回 false，无副作用
                if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;
                DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
                    DamageSource.AreaEffect, TargetableFactions, AllowSelfDamage, null);
                return;
            }
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;
            AddActorRef(actor);
        }

        private void OnBodyExited(Node body)
        {
            if (body is GameActor actor)
                RemoveActorRef(actor);
        }

        private void OnAreaEntered(Area2D area)
        {
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor == null) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(actor, _attacker)) return;
            AddActorRef(actor);
        }

        private void OnAreaExited(Area2D area)
        {
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor != null)
                RemoveActorRef(actor);
        }

        private void AddActorRef(GameActor actor)
        {
            if (_actorRefs.TryGetValue(actor, out int count))
            {
                _actorRefs[actor] = count + 1;
                return;
            }

            _actorRefs[actor] = 1;
            _actorTimers[actor] = 0f;
            DealDamageToActor(actor);
        }

        private void RemoveActorRef(GameActor actor)
        {
            if (!_actorRefs.TryGetValue(actor, out int count)) return;
            if (count > 1)
            {
                _actorRefs[actor] = count - 1;
                return;
            }

            _actorRefs.Remove(actor);
            _actorTimers.Remove(actor);
        }

        /// <summary>
        /// 显式攻击来源（由投掷方传入，同 IAttackerProvider 项目模式——投掷系统 RigidBodyWorldItemEntity 自动设置）。
        /// 优先于父节点解析：父节点下第一个玩家不一定是投掷者。
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
            {
                if (child.IsInGroup("player") && child is GameActor ga)
                {
                    _attacker = ga;
                    break;
                }
            }
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
            // 向上版：标准 |sin| 弹跳曲线——起点（localX=0）在底部接触点（尖角，反弹瞬间带向上速度），
            // 中间为平滑顶点；向下版（旧行为）：相位偏移曲线——起点在顶部（平滑峰顶，速度 0），中间为谷底
            float height = BounceHeight * decay * (
                BounceUpwardFirst
                    ? Mathf.Abs(Mathf.Sin(Mathf.Pi * localX / len))
                    : (Mathf.Abs(Mathf.Sin(Mathf.Pi * (localX + len * 0.5f) / len)) - 1f)
            );

            // 视觉/判定分离：根（含 AttackArea）固定在判定层（基准线）水平移动，
            // 弹跳高度只作用于 Visual 层（视觉/残影/闪电跟随弹跳）；无 Visual 时回退旧行为（整体弹跳）
            GlobalPosition = new Vector2(
                _bounceOrigin.X + _travelDir.X * _bounceDistance,
                _bounceOrigin.Y);
            if (_visual != null)
                _visual.Position = new Vector2(_visual.Position.X, -height);
            else
                GlobalPosition += new Vector2(0f, -height);

            // 墙内保护：当前位置仍在墙/障碍内时不反弹（球继续穿行直至离开墙），
            // 防止厚墙/两墙夹击时 depenetrate 推不出导致的高频来回反弹
            if (IsOverlappingWall(GlobalPosition))
                return;

            if (ProbeCollision(GlobalPosition, _travelDir.X * Speed) && _bounceCooldownRemaining <= 0f)
            {
                _bounceCooldownRemaining = BounceCooldown;
                // 每次反弹统一走仅 X 轴方法：水平方向反向，Y 基准线回正到生成时高度
                BounceXAxisOnly();
            }
        }

        /// <summary>
        /// 仅 X 轴反弹：水平方向反向，Y 基准线回正到生成时的高度，
        /// 从当前位置重新开始弹跳（起始点不高过生成点、不累积下移）。
        /// </summary>
        private void BounceXAxisOnly()
        {
            _travelDir.X = -_travelDir.X;

            // 反弹后若仍与墙重叠（半嵌在墙内），沿新方向推出墙外——防高频来回反弹抖动
            DepenetrateFromWalls();

            _bounceOrigin = new Vector2(GlobalPosition.X, _initialBaselineY);
            _bounceDistance = 0f;
        }

        // ── 反弹碰撞检测 ──────────────────────────────────────────

        /// <summary>
        /// 反弹检测：与 AttackArea 同类的物理查询（形状查询），
        /// 检测形状（BounceDetectSize，比 AttackArea 小）置于移动方向前方 BounceDetectAhead 处，
        /// 命中非角色物理体（墙/障碍）即触发反弹。不依赖视觉层位置。
        /// </summary>
        private bool ProbeCollision(Vector2 from, float horizontalVelocity)
        {
            if (horizontalVelocity == 0f || _bounceShape == null) return false;

            float dir = Mathf.Sign(horizontalVelocity);
            var space = GetWorld2D().DirectSpaceState;
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = _bounceShape,
                Transform = new Transform2D(0f, from + new Vector2(dir * BounceDetectAhead, 0f)),
                CollideWithBodies = true,
                CollideWithAreas = false
            };

            foreach (var result in space.IntersectShape(query, 4))
            {
                if (!result.TryGetValue("collider", out var collider)) continue;
                var body = collider.As<GodotObject>();
                if (body is GameActor) continue; // 敌人/玩家交给伤害流程，不触发反弹
                if (!GodotObject.IsInstanceValid(body)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 检测当前位置是否与墙/障碍（非角色物理体）重叠（用检测形状做位置查询）。
        /// </summary>
        private bool IsOverlappingWall(Vector2 pos)
        {
            if (_bounceShape == null) return false;

            var space = GetWorld2D().DirectSpaceState;
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = _bounceShape,
                Transform = new Transform2D(0f, pos),
                CollideWithBodies = true,
                CollideWithAreas = false
            };

            foreach (var result in space.IntersectShape(query, 4))
            {
                if (!result.TryGetValue("collider", out var collider)) continue;
                var body = collider.As<GodotObject>();
                if (body is GameActor) continue;
                if (!GodotObject.IsInstanceValid(body)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 沿当前移动方向把球推出墙外（depenetrate）：大步长移动，
        /// 直到不再与墙重叠或累计移动达到 BounceDepenetrateMaxDistance。
        /// 超出上限仍未脱离（厚墙/两侧夹住）时，由反弹检测处的"墙内不反弹"保护接管（穿过墙）。
        /// </summary>
        private void DepenetrateFromWalls()
        {
            Vector2 dir = _travelDir.X != 0f ? _travelDir : Vector2.Right;
            float step = Mathf.Max(BounceDetectSize.X, BounceDetectSize.Y) * 0.5f + 24f;
            float moved = 0f;
            while (moved < BounceDepenetrateMaxDistance && IsOverlappingWall(GlobalPosition))
            {
                GlobalPosition += dir * step;
                moved += step;
            }
        }

        public override void _Draw()
        {
            if (!ShowBounceDetectDebug || _bounceShape == null) return;

            // 检测形状在世界坐标（移动方向前方），转换到局部坐标绘制
            float dir = _travelDir.X != 0f ? Mathf.Sign(_travelDir.X) : 1f;
            Vector2 center = GlobalPosition + new Vector2(dir * BounceDetectAhead, 0f);
            Vector2 localCenter = ToLocal(center);
            var rect = new Rect2(localCenter - BounceDetectSize * 0.5f, BounceDetectSize);
            DrawRect(rect, new Color(1f, 0.2f, 0.2f, 0.25f), filled: true);
            DrawRect(rect, new Color(1f, 0.2f, 0.2f, 1f), filled: false, width: 1.5f);
        }
    }
}
