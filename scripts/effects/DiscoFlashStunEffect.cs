using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    [GlobalClass]
    public partial class DiscoFlashStunEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,100,1")]
        public float TriggerChance { get; set; } = 20f;

        [Export(PropertyHint.Range, "50,5000,10")]
        public float StunRadius { get; set; } = 800f;

        [Export(PropertyHint.Range, "0.5,10,0.5")]
        public float StunDuration { get; set; } = 2.0f;

        [Export(PropertyHint.Range, "0.05,1,0.05")]
        public float FlashDuration { get; set; } = 0.3f;

        [Export]
        public Color FlashColor { get; set; } = new Color(1f, 1f, 1f, 0.85f);

        [Export]
        public PackedScene? FlashRayScene { get; set; }

        [Export]
        public Vector2 FlashRaySpawnOffset { get; set; } = Vector2.Zero;

        [Export]
        public bool ShowStunRadius { get; set; } = true;

        private bool _subscribed;
        private ColorRect? _flashRect;
        private string _idPrefix = "";

        public DiscoFlashStunEffect()
        {
            EffectId = "disco_flash_stun";
            DisplayName = "闪光眩晕";
            Description = "攻击命中时有概率触发全屏闪光并眩晕范围敌人。";
            IsBuff = true;
            Duration = 0f;
        }

        protected override void OnApply()
        {
            base.OnApply();
            _idPrefix = $"disco_stun_{GetInstanceId()}";

            _flashRect = GetNodeOrNull<ColorRect>("FlashCanvas/FlashRect");
            if (_flashRect != null)
            {
                _flashRect.Visible = false;
                _flashRect.Color = new Color(FlashColor.R, FlashColor.G, FlashColor.B, 0f);
            }

            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor || target == null) return;
            if (damage <= 0) return;

            if (GD.Randf() * 100f > TriggerChance) return;

            int stunnedCount = TriggerFlashStun(attacker.GlobalPosition, attacker.FacingRight);
            GD.Print($"[DiscoFlashStunEffect] 闪光眩晕触发！概率 {TriggerChance}%，半径 {StunRadius}，眩晕 {stunnedCount} 个敌人");
        }

        private int TriggerFlashStun(Vector2 playerPosition, bool facingRight)
        {
            if (_flashRect != null && IsInstanceValid(_flashRect))
                PlayFlash();

            var offset = FlashRaySpawnOffset;
            if (!facingRight)
                offset.X = -offset.X;
            var center = playerPosition + offset;

            SpawnFlashRays(center, facingRight);
            return StunNearbyEnemies(center);
        }

        private void SpawnFlashRays(Vector2 center, bool facingRight)
        {
            if (FlashRayScene == null) return;

            var tree = GetTree();
            if (tree == null) return;

            if (FlashRayScene.Instantiate() is not DiscoFlashRays rays) return;

            rays.GlobalPosition = center;
            rays.FlipX = !facingRight;
            tree.CurrentScene.AddChild(rays);
        }

        private void PlayFlash()
        {
            _flashRect!.Visible = true;
            _flashRect.Color = new Color(FlashColor.R, FlashColor.G, FlashColor.B, 0f);

            var tween = _flashRect.CreateTween();
            tween.TweenProperty(_flashRect, "color:a", FlashColor.A, FlashDuration * 0.25f);
            tween.TweenProperty(_flashRect, "color:a", 0f, FlashDuration * 0.75f);
            tween.TweenCallback(Callable.From(() =>
            {
                if (IsInstanceValid(_flashRect))
                    _flashRect.Visible = false;
            }));
        }

        private int StunNearbyEnemies(Vector2 center)
        {
            if (Actor == null) return 0;

            if (ShowStunRadius)
                SpawnRadiusIndicator(center);

            var tree = Actor.GetTree();
            if (tree == null) return 0;

            int stunnedCount = 0;

            float radiusSq = StunRadius * StunRadius;

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy) continue;
                if (!IsInstanceValid(enemy) || enemy.IsDeadOrDying) continue;
                if (enemy.ActiveImmunities.HasFlag(ImmunityFlags.Stun)) continue;

                // 防止 Refresh 重置 FreezeEffect 计时器导致眩晕延长
                if (enemy.EffectController?.GetEffect<FreezeEffect>() != null)
                    continue;

                var hitCenter = GetEnemyHitCenter(enemy);
                if (center.DistanceSquaredTo(hitCenter) > radiusSq) continue;

                var freeze = new FreezeEffect
                {
                    Duration = this.StunDuration,
                    EffectId = $"{_idPrefix}_{enemy.GetInstanceId()}",
                    ResumePreviousState = false
                };
                enemy.ApplyEffect(freeze);
                stunnedCount++;
            }

            return stunnedCount;
        }

        private static Vector2 GetEnemyHitCenter(GameActor enemy)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition ?? hitArea?.GlobalPosition ?? enemy.GlobalPosition;
        }

        private void SpawnRadiusIndicator(Vector2 center)
        {
            var tree = GetTree();
            if (tree == null) return;

            var indicator = new Node2D { GlobalPosition = center, ZIndex = 10 };
            indicator.Draw += () =>
            {
                indicator.DrawArc(Vector2.Zero, StunRadius, 0, Mathf.Tau, 64,
                    new Color(1f, 0.1f, 0.1f, 0.6f), 3f);
            };

            var tween = indicator.CreateTween();
            tween.TweenProperty(indicator, "modulate:a", 0f, StunDuration);
            tween.TweenCallback(Callable.From(() =>
            {
                if (IsInstanceValid(indicator))
                    indicator.QueueFree();
            }));

            tree.CurrentScene.AddChild(indicator);
            indicator.QueueRedraw();
        }
    }
}
