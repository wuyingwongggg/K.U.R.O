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
    public partial class LaserBeamPlayerWeapon : Node2D
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

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 50f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 1000f;

        private RayCast2D? _ray;
        private Sprite2D? _glowSprite;
        private Sprite2D? _beamSprite;
        private Sprite2D? _spotlight;
        private float _timer;
        private float _currentLength;
        private float _texWidth;
        private float _texHeight;
        private bool _hasDamaged;
        private readonly HashSet<GameActor> _damagedEnemies = new();

        public override void _Ready()
        {
            _ray = GetNodeOrNull<RayCast2D>("RayCast2D");
            _glowSprite = GetNodeOrNull<Sprite2D>("GlowSprite");
            _beamSprite = GetNodeOrNull<Sprite2D>("BeamSprite");
            _spotlight = GetNodeOrNull<Sprite2D>("Spotlight");

            if (_ray == null || _glowSprite == null || _beamSprite == null)
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

            _ray.TargetPosition = new Vector2(MaxLength, 0f);
            _ray.Enabled = true;

            _timer = Lifetime;
            _hasDamaged = false;

            // 发光点初始透明，射出后渐显
            if (_spotlight != null)
            {
                var sc = _spotlight.Modulate;
                _spotlight.Modulate = new Color(sc.R, sc.G, sc.B, 0f);
            }
        }

        public override void _Process(double delta)
        {
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

            _hasDamaged = true;

            var tree = GetTree();
            if (tree == null) return;

            Vector2 beamDir = new Vector2(Mathf.Cos(Rotation), Mathf.Sin(Rotation));

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || !IsInstanceValid(enemy) || enemy.IsDeadOrDying)
                    continue;
                if (_damagedEnemies.Contains(enemy))
                    continue;

                if (TryDamageEnemy(enemy, beamDir))
                    _damagedEnemies.Add(enemy);
            }
        }

        /// <summary>
        /// 与 LaserBeam.TryDamagePlayer 相同几何距离检测法，命中了则造成伤害和击退。
        /// </summary>
        private bool TryDamageEnemy(GameActor enemy, Vector2 beamDir)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            Vector2 targetCenter = hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;

            Vector2 toTarget = targetCenter - GlobalPosition;

            float along = toTarget.Dot(beamDir);
            if (along < 0f || along > _currentLength) return false;

            float perp = Mathf.Abs(toTarget.X * beamDir.Y - toTarget.Y * beamDir.X);

            float detectionRadius = 150f;
            if (hitShape?.Shape is CapsuleShape2D cap)
            {
                float worldScale = Mathf.Abs(hitShape.GlobalTransform.Scale.X);
                detectionRadius = cap.Radius * worldScale;
            }

            if (perp > detectionRadius) return false;

            bool alreadyInvincible = enemy is Kuros.Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;

            if (Damage > 0)
                enemy.TakeDamage(Damage, GlobalPosition, Attacker, DamageSource.AreaEffect);

            if (!alreadyInvincible)
            {
                float knockSpeed = KnockbackSpeed > 0f
                    ? KnockbackSpeed
                    : KnockbackDistance > 0f
                        ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)
                        : 0f;
                if (knockSpeed > 0f
                    && !enemy.ActiveImmunities.HasFlag(Kuros.Core.ImmunityFlags.ForcedMovement))
                    enemy.Velocity = beamDir * knockSpeed;
            }

            return true;
        }

        /// <summary>光束长度：射线命中点距离（撞墙即止）或 MaxLength，生长动画插值后按纹理尺寸缩放。</summary>
        private void UpdateBeam()
        {
            if (_ray == null || _glowSprite == null || _beamSprite == null) return;

            _ray.ForceRaycastUpdate();

            float rawLength = _ray.IsColliding()
                ? ToLocal(_ray.GetCollisionPoint()).Length()
                : MaxLength;

            float grow = GrowDuration > 0f
                ? Mathf.Clamp((Lifetime - _timer) / GrowDuration, 0f, 1f)
                : 1f;
            _currentLength = Mathf.Lerp(0f, rawLength, grow);

            _glowSprite.Scale = new Vector2(_currentLength / _texWidth, GlowWidth / _texHeight);
            _beamSprite.Scale = new Vector2(_currentLength / _texWidth, BeamWidth / _texHeight);
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
