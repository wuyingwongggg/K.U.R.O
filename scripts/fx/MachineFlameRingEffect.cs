using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Effects;

namespace Kuros.Fx
{
    /// <summary>
    /// 甜甜圈型火焰环：生成后从 0 持续扩散到 MaxRadius，期间接触到的敌人被施加灼烧
    /// （DotBurnEffect，根据施加者基础攻击力等比造成伤害，重复施加刷新时长）。
    /// 由 MachineThermalSunderEffect 在玩家位置快照处实例化并设置 MaxRadius/Attacker。
    /// </summary>
    [GlobalClass]
    public partial class MachineFlameRingEffect : Node2D
    {
        [Export(PropertyHint.Range, "10,800,10")] public float MaxRadius = 300f;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float ExpandDuration = 1.0f;
        [Export(PropertyHint.Range, "0.5,10,0.5")] public float Lifetime = 3f;
        [Export(PropertyHint.Range, "1,20,1")] public float RingWidth = 6f;
        [Export] public Color RingColor = new(1f, 0.45f, 0.1f, 0.9f);
        [Export] public Array<AttackEffectEntry> BurnEffectEntries { get; set; } = new();
        [Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask = 1;
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy;

        /// <summary>灼烧来源（玩家），由生成方设置。</summary>
        public GameActor? Attacker { get; set; }

        private Area2D? _contactArea;
        private CircleShape2D? _contactShape;
        private float _elapsed;
        private float _currentRadius;

        public override void _Ready()
        {
            // 接触区域：圆形半径跟随扩散进度
            _contactArea = new Area2D
            {
                CollisionLayer = 0,
                CollisionMask = TargetCollisionMask,
                Monitoring = true,
                Monitorable = false,
            };
            _contactShape = new CircleShape2D { Radius = 0f };
            _contactArea.AddChild(new CollisionShape2D { Shape = _contactShape });
            AddChild(_contactArea);
            _contactArea.BodyEntered += OnBodyEntered;
            _contactArea.AreaEntered += OnAreaEntered;
            QueueRedraw();
        }

        public override void _PhysicsProcess(double delta)
        {
            _elapsed += (float)delta;
            if (_elapsed >= Lifetime) { QueueFree(); return; }

            // 持续扩散：0 → MaxRadius
            float t = Mathf.Clamp(_elapsed / ExpandDuration, 0f, 1f);
            _currentRadius = MaxRadius * t;
            if (_contactShape != null)
                _contactShape.Radius = _currentRadius;
            QueueRedraw();
        }

        public override void _Draw()
        {
            // 甜甜圈：外环描边 + 内部淡光晕
            DrawArc(Vector2.Zero, _currentRadius, 0f, Mathf.Tau, 64, RingColor, RingWidth);
            var glow = new Color(RingColor.R, RingColor.G, RingColor.B, RingColor.A * 0.15f);
            DrawCircle(Vector2.Zero, Mathf.Max(_currentRadius - RingWidth, 0f), glow);
        }

        private void OnBodyEntered(Node body)
        {
            if (body is not GameActor actor) return;
            if (DamageDispatcher.BelongsToActor(body, Attacker)) return;
            ApplyBurn(actor);
        }

        private void OnAreaEntered(Area2D area)
        {
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor == null) return;
            if (DamageDispatcher.BelongsToActor(actor, Attacker)) return;
            ApplyBurn(actor);
        }

        /// <summary>对进入火焰环的敌人施加灼烧（重复施加由 DotBurnEffect 的 EffectId 刷新覆盖）。</summary>
        private void ApplyBurn(GameActor target)
        {
            if (target.IsDeathSequenceActive || target.IsDead) return;

            foreach (var entry in BurnEffectEntries)
            {
                if (entry?.Scene == null) continue;
                if (entry.InstantiateEffect() is not DotBurnEffect burn) continue;
                burn.Attacker = Attacker;
                target.ApplyEffect(burn);
            }
        }
    }
}
