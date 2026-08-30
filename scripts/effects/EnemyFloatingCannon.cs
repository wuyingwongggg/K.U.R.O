using System;
using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// 敌人浮游炮（世界效果，遵循 EFFECT_STANDARD.md）：
    /// 生成后悬停在施放者（Attacker）身旁，按 TargetCollisionMask 通过 IntersectShape
    /// 检测范围内最近的目标，旋转瞄准并周期发射激光（LaserEntry 条目，场景 + 属性重载）。
    /// 生命周期：入场 scanline 动画 → 持续跟随/瞄准/开火 → 退场动画 → 自销毁（Duration 自管理，_ExitTree 兜底）。
    /// 攻击者归属由生成方（EnemyAttackTemplate.SpawnSingleEffect）经 IAttackerProvider 注入。
    /// </summary>
    public partial class EnemyFloatingCannon : Node2D, IAttackerProvider
    {
        public GameActor? Attacker { get; set; }

        /// <summary>发射的激光特效条目（AttackEffectEntry：场景 + 属性重载，数据驱动）。
        /// 每次开火按条目实例化并应用重载。
        /// 外层生成炮台的 AttackEffectEntry 也可通过 PropertyOverrides 直接换掉本字段。</summary>
        [Export] public AttackEffectEntry? LaserEntry { get; set; }

        /// <summary>开火间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float FireInterval { get; set; } = 1.0f;

        /// <summary>目标检测范围（像素）：超过此距离的目标不参与瞄准。</summary>
        [Export(PropertyHint.Range, "100,3000,10")] public float DetectionRange { get; set; } = 1600f;

        /// <summary>目标扫描节流（秒）：目标发现按此间隔刷新（比每帧查更省）。</summary>
        [Export(PropertyHint.Range, "0.05,1,0.05")] public float ScanInterval { get; set; } = 0.2f;

        [ExportGroup("Follow")]
        /// <summary>相对施放者的跟随偏移（X 按朝向取符号，Y 固定）。</summary>
        [Export] public Vector2 SpawnOffset { get; set; } = new Vector2(250f, -350f);
        /// <summary>跟随平滑度（指数收敛速率）：越大越快贴向目标点。</summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float FollowSmoothing { get; set; } = 6f;
        /// <summary>上下浮动幅度（像素），0 关闭。</summary>
        [Export(PropertyHint.Range, "0,60,1")] public float HoverAmplitude { get; set; } = 12f;
        /// <summary>上下浮动频率（次/秒）。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float HoverFrequency { get; set; } = 1f;

        [ExportGroup("Lifecycle")]
        /// <summary>效果总时长（秒）。0 = 永不自动销毁（需外部清理）。</summary>
        [Export(PropertyHint.Range, "0,60,0.5")] public float Duration { get; set; } = 8f;
        /// <summary>入场扫描线动画时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float SpawnAnimDuration { get; set; } = 0.4f;
        /// <summary>退场扫描线动画时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float DespawnAnimDuration { get; set; } = 0.3f;

        [ExportGroup("Targeting")]
        /// <summary>目标检测碰撞掩码（EffectStandard 导出配置，不写死组名/层号）。
        /// 只影响玩家 = 4（Layer 3）；玩家+可破坏物 = 5（Layer 3 | Layer 1）。</summary>
        [Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask { get; set; } = 4u;

        private Marker2D? _laserSpawnPoint;      // 激光发射点（场景内 Marker2D，激光在此生成）
        private ShaderMaterial? _outlineMat;     // 轮廓图层材质（scanline 着色器）
        private ShaderMaterial? _spriteMat;      // 主体图层材质（scanline 着色器）
        private Sprite2D? _outline;              // 轮廓 Sprite2D（瞄准旋转时按方向翻转用）
        private CircleShape2D? _scanShape;       // IntersectShape 查询形状（DetectionRange 圆）
        private GameActor? _currentTarget;       // 当前锁定的目标（扫描节流刷新）
        private float _fireTimer;                // 开火计时器
        private float _scanTimer;                // 目标扫描节流计时器
        private float _hoverClock;               // 浮动相位时钟
        private float _lifeElapsed;              // 已存活时长
        private bool _despawning;                // 退场中标记（防重复触发退场）

        public override void _Ready()
        {
            _laserSpawnPoint = GetNodeOrNull<Marker2D>("Marker2D");
            var outlineNode = GetNodeOrNull<Sprite2D>("outline");
            _outline = outlineNode;
            _outlineMat = outlineNode?.Material as ShaderMaterial;
            _spriteMat = outlineNode?.GetNodeOrNull<Sprite2D>("Sprite2D")?.Material as ShaderMaterial;
            _scanShape = new CircleShape2D { Radius = Mathf.Max(DetectionRange, 100f) };
            _fireTimer = FireInterval;
            _scanTimer = 0f;
            _lifeElapsed = 0f;
            _despawning = false;

            // 入场：scanline 正扫一遍后禁用（隐藏扫描线，进入正常显示）
            PlayScanlineAnim(false, SpawnAnimDuration, DisableScanline);
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;
            if (_despawning) return;

            _lifeElapsed += dt;
            _hoverClock += dt;

            FollowAttacker(dt);

            // 剩余时长不足退场动画时提前进入退场（有离场演出再消失）
            if (Duration > 0f)
            {
                float remaining = Duration - _lifeElapsed;
                if (remaining <= DespawnAnimDuration)
                {
                    StartDespawn();
                    return;
                }
            }

            // 目标扫描（节流）：IntersectShape 按 TargetCollisionMask 找最近目标
            _scanTimer -= dt;
            if (_scanTimer <= 0f)
            {
                _scanTimer = Mathf.Max(0.05f, ScanInterval);
                _currentTarget = FindNearestTarget();
            }

            // 有目标时旋转瞄准
            if (_currentTarget != null && IsInstanceValid(_currentTarget))
                RotateToward(GetAimCenter(_currentTarget));

            // 定时开火（有目标时才发射）
            _fireTimer -= dt;
            if (_fireTimer <= 0f && _currentTarget != null && IsInstanceValid(_currentTarget))
            {
                _fireTimer = Mathf.Max(0.1f, FireInterval);
                FireLaser();
            }
        }

        public override void _ExitTree()
        {
            // 兜底：未走退场动画被移除时直接清理
            base._ExitTree();
        }

        /// <summary>开始退场：反向扫描线动画结束后自销毁（防重复触发）。</summary>
        private void StartDespawn()
        {
            if (_despawning) return;
            _despawning = true;
            PlayScanlineAnim(true, DespawnAnimDuration, QueueFree);
        }

        /// <summary>
        /// 目标发现（EffectStandard：IntersectShape，不用 GetNodesInGroup）：
        /// 以自身为圆心、DetectionRange 为半径查询 TargetCollisionMask，
        /// 取最近的存活 GameActor（排除施放者自身归属）。
        /// </summary>
        private GameActor? FindNearestTarget()
        {
            if (_scanShape == null) return null;
            var world = GetWorld2D();
            if (world == null) return null;

            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = _scanShape,
                Transform = new Transform2D(0f, GlobalPosition),
                CollisionMask = TargetCollisionMask,
                CollideWithBodies = true,
                CollideWithAreas = true
            };

            GameActor? nearest = null;
            float nearestDistSq = float.MaxValue;
            foreach (var result in world.DirectSpaceState.IntersectShape(query))
            {
                if (!result.TryGetValue("collider", out var colliderVar)) continue;
                var collider = colliderVar.As<GodotObject>();
                var actor = (collider as GameActor)
                    ?? ((collider as Node)?.Owner as GameActor);
                if (actor == null || !IsInstanceValid(actor) || actor.IsDeadOrDying) continue;
                if (DamageDispatcher.BelongsToActor(actor, Attacker)) continue;

                float distSq = GlobalPosition.DistanceSquaredTo(actor.GlobalPosition);
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = actor;
                }
            }

            return nearest;
        }

        /// <summary>发射激光：按 LaserEntry（场景 + 属性重载）实例化；攻击者归属继承本炮台。</summary>
        private void FireLaser()
        {
            var scene = LaserEntry?.Scene;
            if (scene == null || _laserSpawnPoint == null) return;

            var laser = scene.Instantiate<Node2D>();
            if (laser == null) return;

            LaserEntry?.ApplyOverrides(laser);

            if (laser is IAttackerProvider provider)
                provider.Attacker = Attacker;

            GetTree()?.CurrentScene?.AddChild(laser);
            laser.GlobalPosition = _laserSpawnPoint.GlobalPosition;
            // 注意：不设置根 GlobalRotation。
            // 候选激光场景（laser_beam_ultimate / laser_beamA）均为自瞄准模型：
            // 视觉/判定分离依赖"根恒 0 旋转"（视觉在根上方固定偏移、判定点固定在根处），
            // 旋转根会让视觉围着判定点甩圈并叠加双重旋转——与 BunnySwordFloatingCannon
            // 用的 LaserBeamPlayerWeapon 不同（后者会把根旋转迁移进视觉层，接受外部旋转）。
        }

        /// <summary>指数平滑跟随施放者：目标点 = 施放者位置 + SpawnOffset（X 按朝向取符号）+ 正弦浮动。</summary>
        private void FollowAttacker(float delta)
        {
            if (Attacker == null || !IsInstanceValid(Attacker)) return;

            float sideSign = Attacker.FacingRight ? 1f : -1f;
            float hover = Mathf.Sin(_hoverClock * Mathf.Tau * HoverFrequency) * HoverAmplitude;
            var target = Attacker.GlobalPosition + new Vector2(SpawnOffset.X * sideSign, SpawnOffset.Y + hover);

            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, FollowSmoothing) * delta);
            GlobalPosition = GlobalPosition.Lerp(target, blend);
        }

        /// <summary>旋转炮台指向目标 + 按目标方向翻转轮廓（朝左时 Y 翻转保持纹理方向）。</summary>
        private void RotateToward(Vector2 target)
        {
            var direction = target - GlobalPosition;
            Rotation = direction.Angle();

            if (_outline != null)
            {
                var scale = _outline.Scale;
                scale.X = Mathf.Abs(scale.X);
                scale.Y = direction.X < 0 ? -Mathf.Abs(scale.Y) : Mathf.Abs(scale.Y);
                _outline.Scale = scale;
            }
        }

        /// <summary>瞄准中心：优先取 HitArea 的碰撞形状中心（与伤害判定一致，不随视觉锚点偏移）。</summary>
        private static Vector2 GetAimCenter(Node2D actor)
        {
            var hitArea = actor.GetNodeOrNull<Area2D>("HitArea")
                ?? actor.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? actor.GlobalPosition;
        }

        /// <summary>播放 scanline 着色器动画：参数 0 → 1 扫过（reverse=false 入场 / true 退场），结束后回调。</summary>
        private void PlayScanlineAnim(bool reverse, float duration, Action onDone)
        {
            if (_outlineMat == null && _spriteMat == null) { onDone(); return; }
            var tree = GetTree();
            if (tree == null) { onDone(); return; }

            SetReverseScan(reverse);
            SetScanlinePos(0f);

            var tween = tree.CreateTween();
            tween.TweenMethod(Callable.From<float>(pos =>
            {
                if (_outlineMat != null && IsInstanceValid(_outlineMat))
                    _outlineMat.SetShaderParameter("scanline_pos", pos);
                if (_spriteMat != null && IsInstanceValid(_spriteMat))
                    _spriteMat.SetShaderParameter("scanline_pos", pos);
            }), 0f, 1f, duration);
            tween.TweenCallback(Callable.From(onDone));
        }

        private void SetScanlinePos(float pos)
        {
            _outlineMat?.SetShaderParameter("scanline_pos", pos);
            _spriteMat?.SetShaderParameter("scanline_pos", pos);
        }

        private void SetReverseScan(bool reverse)
        {
            _outlineMat?.SetShaderParameter("reverse_scan", reverse);
            _spriteMat?.SetShaderParameter("reverse_scan", reverse);
        }

        /// <summary>禁用扫描线（恢复正常显示）。</summary>
        private void DisableScanline()
        {
            SetReverseScan(false);
            SetScanlinePos(-1f);
        }
    }
}
