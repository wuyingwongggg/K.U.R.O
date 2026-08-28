using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Fx;
using System;

namespace Kuros.Effects
{
    /// <summary>
    /// 兔子剑浮游炮（ActorEffect）：施加后从角色脱离为独立悬浮炮台，
    /// 自动检测范围内最近的敌人、旋转瞄准并周期发射激光（LaserEffect 场景）。
    /// 生命周期：入场 scanline 动画 → 持续瞄准开火 → 结束前退场动画 → 销毁。
    /// </summary>
    [GlobalClass]
    public partial class BunnySwardFloatingCannon : ActorEffect
    {
        /// <summary>发射的激光特效场景（LaserBeamPlayerWeapon，会自动继承攻击者归属）。</summary>
        [Export] public PackedScene? LaserEffect { get; set; }

        /// <summary>开火间隔（秒）：每间隔此时间向当前目标发射一发激光。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float FireInterval { get; set; } = 1.0f;

        /// <summary>敌人检测范围（像素）：超过此距离的敌人不参与瞄准。</summary>
        [Export(PropertyHint.Range, "100,2000,10")]
        public float DetectionRange { get; set; } = 2000f;

        [ExportGroup("Follow")]
        /// <summary>相对玩家的跟随偏移（X 按朝向取符号，Y 固定）——生成位置即跟随位置，无独立 front 点。</summary>
        [Export] public Vector2 SpawnOffset { get; set; } = new Vector2(300f, -100f);
        /// <summary>跟随平滑度（指数收敛速率，同 P2/雨伞护盾）：越大越快贴向目标点。</summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float FollowSmoothing { get; set; } = 8.5f;
        /// <summary>上下浮动幅度（像素，同 P2 FloatAmplitude）：0 关闭浮动。</summary>
        [Export(PropertyHint.Range, "0,60,1")] public float HoverAmplitude { get; set; } = 10f;
        /// <summary>上下浮动频率（次/秒，同 P2 FloatFrequency）。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float HoverFrequency { get; set; } = 1f;

        /// <summary>入场扫描线动画时长（秒）：施加时 scanline 从 0 → 1 扫过并隐藏。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")]
        public float SpawnAnimDuration { get; set; } = 0.4f;

        /// <summary>退场扫描线动画时长（秒）：到期/移除前 scanline 反向扫过并销毁。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")]
        public float DespawnAnimDuration { get; set; } = 0.3f;

        private Marker2D? _laserSpawnPoint;      // 激光发射点（场景内 Marker2D，激光在此生成并继承旋转）
        private float _beamHitOffsetY;           // 激光判定带相对根的 y 偏移（运行时从激光场景读取，瞄准偏移的唯一来源）
        private float _fireTimer;                // 开火计时器（累加 delta，达到 FireInterval 发射）
        private ShaderMaterial? _outlineMat;     // 轮廓 Sprite2D 的材质（控制 scanline 着色器参数）
        private ShaderMaterial? _spriteMat;      // 内部 Sprite2D 的材质（同上，两个图层同步动画）
        private bool _despawning;                // 退场中标记：防止退场动画被重复触发/重复销毁
        private float _lifeElapsed;              // 效果已存活时长（秒），用于提前触发退场（结束前 DespawnAnimDuration 秒）
        private float _hoverClock;               // 浮动相位时钟

        /// <summary>施加时：定位到角色旁、初始化引用、播放入场扫描线动画。</summary>
        protected override void OnApply()
        {
            base.OnApply();
            _laserSpawnPoint = GetNode<Marker2D>("Marker2D");
            _beamHitOffsetY = ResolveLaserHitOffset();
            _fireTimer = 0f;
            _despawning = false;
            _lifeElapsed = 0f;

            if (Actor != null)
            {
                // 从角色身上脱离：重挂到角色的父级（成为独立的场景物体），初始位置在跟随点（玩家 + SpawnOffset）
                Reparent(Actor.GetParent());
                Set("global_position", ComputeFollowPosition());
            }

            // 缓存两个图层的材质（轮廓 + 主体），用于 scanline 着色器动画
            var outline = GetNode<Sprite2D>("outline");
            _outlineMat = outline.Material as ShaderMaterial;
            _spriteMat = outline.GetNode<Sprite2D>("Sprite2D").Material as ShaderMaterial;

            // 入场：scanline 正扫一遍后禁用（隐藏扫描线，进入正常显示状态）
            PlayScanlineAnim(false, SpawnAnimDuration, () =>
            {
                DisableScanline();
            });
        }

        /// <summary>堆叠刷新：不重置生命周期——保持原有的剩余时长（玩家重复攻击不续期）。</summary>
        protected override void OnStackRefreshed()
        {
            // 玩家再次攻击时不应刷新 Duration——保持原有生命周期
        }

        /// <summary>到期：播放退场扫描线动画后销毁。</summary>
        protected override void OnExpire()
        {
            if (_despawning) return;
            _despawning = true;

            PlayScanlineAnim(true, DespawnAnimDuration, () =>
            {
                base.OnExpire();
                QueueFree();
            });
        }

        /// <summary>被外部移除：同样先播退场动画再销毁（与到期共用退场逻辑，防止重复触发）。</summary>
        public override void OnRemoved()
        {
            if (!_despawning)
            {
                PlayScanlineAnim(true, DespawnAnimDuration, () =>
                {
                    base.OnRemoved();
                    QueueFree();
                });
                _despawning = true;
            }
            else
            {
                base.OnRemoved();
            }
        }

        /// <summary>每帧：平滑跟随玩家（玩家 + SpawnOffset，同雨伞护盾）+ 旋转瞄准最近敌人 + 计时开火；
        /// 剩余时长不足退场动画时提前进入退场。</summary>
        protected override void OnTick(double delta)
        {
            if (_despawning) return; // 退场中不再处理

            _lifeElapsed += (float)delta;

            FollowPlayer((float)delta);

            // 结束前提前 DespawnAnimDuration 秒播退场动画（让激光炮有离场演出再消失）
            var remaining = Duration - _lifeElapsed;
            if (Duration > 0 && remaining <= DespawnAnimDuration)
            {
                _despawning = true;
                PlayScanlineAnim(true, DespawnAnimDuration, () =>
                {
                    Controller?.RemoveEffect(this);
                });
                return;
            }

            // 找最近敌人并转向瞄准（朝目标上方 _beamHitOffsetY 偏移：
            // 激光从炮口射出命中目标上方，判定带（相对根偏移同量）正好落在目标中心——与偏移值无关）
            var nearestEnemy = FindNearestEnemy();
            if (nearestEnemy != null)
            {
                RotateToward(GetEnemyAimCenter(nearestEnemy) + new Vector2(0f, -_beamHitOffsetY));
            }

            // 定时开火（有目标时才发射）
            _fireTimer += (float)delta;
            if (_fireTimer >= FireInterval && nearestEnemy != null)
            {
                _fireTimer -= FireInterval;
                FireLaser();
            }
        }

        /// <summary>播放 scanline 着色器动画：参数 0 → 1 扫过（reverse=false 入场 / true 退场），结束后回调。</summary>
        private void PlayScanlineAnim(bool reverse, float duration, Action onDone)
        {
            if (_outlineMat == null || _spriteMat == null) return;
            var tree = GetTree();
            if (tree == null) return;

            SetScanlinePos(0f);
            SetReverseScan(reverse);

            var tween = tree.CreateTween();
            tween.SetParallel(true);
            tween.TweenMethod(Callable.From<float>(pos =>
            {
                // 双图层同步驱动 scanline_pos（材质可能被销毁，需有效性检查）
                if (_outlineMat != null && GodotObject.IsInstanceValid(_outlineMat))
                    _outlineMat.SetShaderParameter("scanline_pos", pos);
                if (_spriteMat != null && GodotObject.IsInstanceValid(_spriteMat))
                    _spriteMat.SetShaderParameter("scanline_pos", pos);
            }), 0f, 1f, duration);
            tween.SetParallel(false);
            tween.TweenCallback(Callable.From(onDone));
        }

        /// <summary>直接设置 scanline 位置（0~1；-1 = 禁用）。</summary>
        private void SetScanlinePos(float pos)
        {
            _outlineMat?.SetShaderParameter("scanline_pos", pos);
            _spriteMat?.SetShaderParameter("scanline_pos", pos);
        }

        /// <summary>设置扫描线方向（false = 正扫入场，true = 反向退场）。</summary>
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

        private Sprite2D? _outline; // 轮廓 Sprite2D 缓存（RotateToward 翻转用，延迟解析）

        /// <summary>旋转炮台指向目标 + 按目标方向翻转轮廓（朝左时 Y 翻转保持纹理方向）。</summary>
        private void RotateToward(Vector2 target)
        {
            var direction = target - GetGlobalPos();
            Set("rotation", direction.Angle());

            if (_outline == null && HasNode("outline"))
                _outline = GetNode<Sprite2D>("outline");
            if (_outline != null)
            {
                var scale = _outline.Scale;
                scale.X = Mathf.Abs(scale.X);
                scale.Y = direction.X < 0 ? -Mathf.Abs(scale.Y) : Mathf.Abs(scale.Y);
                _outline.Scale = scale;
            }
        }

        /// <summary>读取激光判定带相对根的 y 偏移（单一来源：激光场景 BeamHitArea 位置），瞄准偏移与判定带位置自动一致。</summary>
        private float ResolveLaserHitOffset()
        {
            if (LaserEffect == null) return 0f;
            var probe = LaserEffect.Instantiate<Node2D>();
            var offset = probe?.GetNodeOrNull<Area2D>("BeamHitArea")?.Position.Y ?? 0f;
            probe?.QueueFree();
            return offset;
        }

        /// <summary>在检测范围内查找最近的存活敌人（enemies 组，跳过无效/已死亡）。</summary>
        private GameActor? FindNearestEnemy()
        {
            var tree = GetTree();
            if (tree == null) return null;

            GameActor? nearest = null;
            float nearestDistSq = float.MaxValue;
            var myPos = GetGlobalPos();
            float rangeSq = DetectionRange * DetectionRange;

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || !IsInstanceValid(enemy) || enemy.IsDeadOrDying)
                    continue;

                float distSq = myPos.DistanceSquaredTo(enemy.GlobalPosition);
                if (distSq > rangeSq)
                    continue;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = enemy;
                }
            }

            return nearest;
        }

        /// <summary>发射激光：在发射点实例化 LaserEffect，继承发射点位置/旋转，攻击者归属设为角色。</summary>
        private void FireLaser()
        {
            if (LaserEffect == null || _laserSpawnPoint == null) return;

            var laser = LaserEffect.Instantiate<Node2D>();
            if (laser == null) return;

            // 传递攻击者归属：激光伤害算在角色头上
            if (laser is LaserBeamPlayerWeapon weaponLaser)
                weaponLaser.Attacker = Actor;

            // 挂到场景（静态）：判定带（BeamHitArea）是地面判定层，跟随挂载对象移动会破坏
            // 视觉/判定分离（视觉在炮口上方、判定固定地面）
            GetTree()?.CurrentScene?.AddChild(laser);
            laser.GlobalPosition = _laserSpawnPoint.GlobalPosition;
            laser.GlobalRotation = _laserSpawnPoint.GlobalRotation;
        }

        /// <summary>敌人瞄准中心：优先取 HitArea 的碰撞形状中心（命中判定更准），依次回退。</summary>
        private static Vector2 GetEnemyAimCenter(Node2D enemy)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;
        }

        /// <summary>读取自身全局位置（通过 Godot 属性访问，兼容 Reparent 后的位置语义）。</summary>
        private Vector2 GetGlobalPos()
        {
            return Get("global_position").AsVector2();
        }

        /// <summary>指数平滑跟随玩家（同 P2：blend = 1 - exp(-smoothing * dt)），目标 = 玩家 + SpawnOffset（X 按朝向取符号）+ 正弦浮动。</summary>
        private void FollowPlayer(float delta)
        {
            if (Actor == null || !IsInstanceValid(Actor)) return;

            _hoverClock += delta;
            var target = ComputeFollowPosition();
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, FollowSmoothing) * delta);
            Set("global_position", GetGlobalPos().Lerp(target, blend));
        }

        /// <summary>跟随目标点：玩家前方（SpawnOffset.X 按朝向取符号）+ 上下浮动（同 P2/雨伞护盾）。</summary>
        private Vector2 ComputeFollowPosition()
        {
            float sideSign = Actor!.FacingRight ? 1f : -1f;
            float hover = Mathf.Sin(_hoverClock * Mathf.Tau * HoverFrequency) * HoverAmplitude;
            return Actor.GlobalPosition + new Vector2(SpawnOffset.X * sideSign, SpawnOffset.Y + hover);
        }
    }
}
