using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 玩家激光束（浮游炮发射）：shader 光束视觉效果，结构与 laser_beamA/LaserPointerBeam 一致
    /// （GlowSprite/BeamSprite 用 laser_blaster_glow 材质 + Spotlight 发光点），
    /// 生长动画展开、到期 shader fade 淡出、发光点分离后独立存活。
    /// 伤害/击退逻辑保持原实现：沿光束路径命中所有敌人（浮游炮已瞄准，无需追踪）。
    /// </summary>
    public partial class LaserBeamPlayerWeapon : Node2D, IAttackerProvider
    {
        public GameActor? Attacker { get; set; }

        [ExportCategory("Beam")]
        [Export] public float MaxLength = 3000f;
        [Export] public float BeamWidth = 20f;
        [Export] public float GlowWidth = 60f;
        /// <summary>生长动画时长（秒）：从 0 生长到命中长度。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float GrowDuration = 0.12f;

        [ExportCategory("Timing")]
        [Export] public float Lifetime = 0.45f;
        [Export] public float FadeDuration = 0.15f;

        [ExportCategory("Spotlight")]
        /// <summary>发光点独立存活时长（秒）：光束结束后光点继续存在并自毁。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float SpotlightDuration { get; set; } = 0.5f;
        /// <summary>发光点淡入时长（秒）：射出时从透明渐显。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeIn { get; set; } = 0.15f;
        /// <summary>发光点淡出时长（秒）：分离后到点前渐隐再销毁。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float SpotlightFadeOut { get; set; } = 0.3f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 2;
        /// <summary>地面判定带垂直半高（像素）：目标 HitArea 与此带重叠即命中。</summary>
        [Export(PropertyHint.Range, "10,500,1")] public float DetectionRadius = 150f;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 50f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 1000f;

        private Sprite2D? _glowSprite;
        private Sprite2D? _beamSprite;
        private Sprite2D? _spotlight;
        private Node2D? _visual;
        private Area2D? _hitArea;
        private CollisionShape2D? _hitShape;
        private float _timer;
        private float _currentLength;
        private float _texWidth;
        private float _texHeight;
        private bool _hasDamaged;
        private bool _directionInitialized;

        public override void _Ready()
        {
            _glowSprite = GetNodeOrNull<Sprite2D>("Visual/GlowSprite");
            _beamSprite = GetNodeOrNull<Sprite2D>("Visual/BeamSprite");
            _spotlight = GetNodeOrNull<Sprite2D>("Visual/Spotlight");
            _visual = GetNodeOrNull<Node2D>("Visual");
            _hitArea = GetNodeOrNull<Area2D>("BeamHitArea");
            if (_hitArea != null)
            {
                _hitArea.CollisionLayer = 0;
                _hitShape = _hitArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (_hitShape?.Shape is RectangleShape2D rs)
                {
                    rs.Size = new Vector2(0f, DetectionRadius * 2f);
                    _hitShape.Position = Vector2.Zero;
                }
            }

            if (_glowSprite == null || _beamSprite == null)
            {
                GD.PushWarning("[LaserBeamPlayerWeapon] 缺少子节点，请检查场景结构。");
                QueueFree();
                return;
            }

            // 每个实例独立复制材质，防止多实例共享导致 fade 残留（同 LaserBeamA）
            if (_glowSprite.Material is ShaderMaterial gm)
            {
                var copy = (ShaderMaterial)gm.Duplicate();
                copy.SetShaderParameter("fade", 1.0f);
                _glowSprite.Material = copy;
            }
            if (_beamSprite.Material is ShaderMaterial bm)
            {
                var copy = (ShaderMaterial)bm.Duplicate();
                copy.SetShaderParameter("fade", 1.0f);
                _beamSprite.Material = copy;
            }

            _texWidth = _beamSprite.Texture?.GetWidth() ?? 2f;
            _texHeight = _beamSprite.Texture?.GetHeight() ?? 2f;
            if (_texWidth <= 0f) _texWidth = 2f;
            if (_texHeight <= 0f) _texHeight = 2f;

            _timer = Lifetime;
            _hasDamaged = false;
            _directionInitialized = false;

            // 发光点初始透明，射出后渐显
            if (_spotlight != null)
            {
                var sc = _spotlight.Modulate;
                _spotlight.Modulate = new Color(sc.R, sc.G, sc.B, 0f);
            }
        }

        public override void _Process(double delta)
        {
            // 方向迁移：生成方（浮游炮）通过 GlobalRotation 设置方向——首帧迁到视觉/判定带，根复位 0（同 LaserBeamA 结构）
            if (!_directionInitialized)
            {
                _directionInitialized = true;
                float angle = Rotation;
                if (_visual != null) _visual.Rotation = angle;
                if (_hitArea != null) _hitArea.Rotation = angle;
                Rotation = 0f;
            }

            _timer -= (float)delta;

            if (_timer <= 0f)
            {
                DetachSpotlight();
                QueueFree();
                return;
            }

            // 淡出：shader 通过 uniform fade 控制（仅光束层）。Spotlight 独立生命周期，不参与光束淡出
            if (_timer < FadeDuration && FadeDuration > 0f)
            {
                float t = _timer / FadeDuration;
                if (_glowSprite?.Material is ShaderMaterial gm)
                    gm.SetShaderParameter("fade", t);
                if (_beamSprite?.Material is ShaderMaterial bm)
                    bm.SetShaderParameter("fade", t);
            }

            // 发光点淡入（前 SpotlightFadeIn 秒 0 → 1）
            if (_spotlight != null && SpotlightFadeIn > 0f)
            {
                var sc = _spotlight.Modulate;
                float fadeInT = Mathf.Clamp((Lifetime - _timer) / SpotlightFadeIn, 0f, 1f);
                _spotlight.Modulate = new Color(sc.R, sc.G, sc.B, fadeInT);
            }

            UpdateBeam();

            // 生长完成后触发伤害，与视觉同步（同 LaserBeamA）
            if (!_hasDamaged && Lifetime - _timer >= GrowDuration)
            {
                TryDamageEnemies();
            }
        }

        private void TryDamageEnemies()
        {
            if (_hasDamaged) return;
            if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
            if (_hitArea == null) return;

            // 俯视角地面判定（Area2D 物理重叠，与 LaserBeamA 同构）：判定带 = 光束水平段 × DetectionRadius 垂直容差
            float beamAngle = _hitArea.Rotation;
            Vector2 beamDir = new(Mathf.Cos(beamAngle), Mathf.Sin(beamAngle));
            if (beamDir == Vector2.Zero) beamDir = Vector2.Right;

            var damaged = new HashSet<ulong>();
            // Area 目标：只接受受击判定区（HitArea），玩家攻击/交互 Area 探入光束不触发（同 LaserBeamA 过滤）
            foreach (var area in _hitArea.GetOverlappingAreas())
            {
                if (area.Name != "HitArea") continue;
                TryDamageReceiver(area, beamDir, damaged);
            }
            // Body 目标（敌人 CharacterBody2D 等）
            foreach (var body in _hitArea.GetOverlappingBodies())
                TryDamageReceiver(body, beamDir, damaged);

            _hasDamaged = true;
        }

        private void TryDamageReceiver(Node collider, Vector2 beamDir, HashSet<ulong> damaged)
        {
            // 阵营过滤（ResolveDamageReceiver）→ 接收者解析 + 去重（同一角色多判定区重叠只结算一次）
            if (DamageDispatcher.ResolveDamageReceiver(collider, TargetableFactions.Enemy) is not Node receiver)
                return;
            if (!damaged.Add(receiver.GetInstanceId())) return;

            bool dealt = DamageDispatcher.DealDamage(receiver, Damage, GlobalPosition, Attacker,
                DamageSource.AreaEffect, TargetableFactions.Enemy, false);
            if (!dealt) return;

            // 击退只对 GameActor
            if (receiver is GameActor actor)
            {
                float knockSpeed = KnockbackSpeed > 0f
                    ? KnockbackSpeed
                    : KnockbackDistance > 0f
                        ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)
                        : 0f;
                if (knockSpeed > 0f) actor.ApplyKnockback(beamDir, knockSpeed);
            }
        }

        /// <summary>光束恒为 MaxLength 全长（不截断），生长动画插值后按纹理尺寸缩放；判定带随生长同步扩展。</summary>
        private void UpdateBeam()
        {
            if (_glowSprite == null || _beamSprite == null) return;

            float grow = GrowDuration > 0f
                ? Mathf.Clamp((Lifetime - _timer) / GrowDuration, 0f, 1f)
                : 1f;
            _currentLength = Mathf.Lerp(0f, MaxLength, grow);

            _glowSprite.Scale = new Vector2(_currentLength / _texWidth, GlowWidth / _texHeight);
            _beamSprite.Scale = new Vector2(_currentLength / _texWidth, BeamWidth / _texHeight);

            // 判定带随光束生长同步扩展；带从发射点向一端伸展，方向由判定带旋转驱动
            if (_hitShape?.Shape is RectangleShape2D rs)
            {
                rs.Size = new Vector2(_currentLength, DetectionRadius * 2f);
                _hitShape.Position = new Vector2(_currentLength * 0.5f, 0f);
            }
        }

        /// <summary>
        /// 分离发光点：光束销毁时把 Spotlight 重新挂到当前父级，保持全局位置跟随移动，
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
    }
}
