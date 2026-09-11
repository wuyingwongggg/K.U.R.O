using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Effects;

namespace Kuros.Fx
{
    /// <summary>
    /// 甜甜圈型火焰环：生成后从 0 持续扩散到 MaxRadius，期间接触到的敌人
    /// 首次接触触发一次直接伤害与击退（每个敌人仅一次），后续伤害由灼烧决定
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
        /// <summary>随环扩散缩放的视觉 Sprite（挂火焰 shader 的圆环）。</summary>
        [Export] public NodePath VisualSpritePath = new("Sprite2D");
        /// <summary>视觉 Sprite 缩放为 1 时环的视觉半径（px）。调至与材质中环的尺寸匹配。</summary>
        [Export(PropertyHint.Range, "10,800,10")] public float VisualBaseRadius = 100f;
        /// <summary>伤害判定区域形状（水平胶囊体）：Y 相对 X 的压缩比例。1 = 圆形，&lt;1 = 上下短（匹配透视椭圆视觉）。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float DamageAreaAspectY = 0.8f;
        /// <summary>调试：绘制程序化环形描边（对比 shader 环与判定范围）。默认关闭。</summary>
        [Export] public bool ShowDebugRing = false;

        /// <summary>接触瞬间的直接伤害（每个敌人仅首次接触触发一次，0 = 关闭）。后续伤害由灼烧决定。</summary>
        [Export(PropertyHint.Range, "0,500,1")] public float ContactDamage = 10f;
        /// <summary>接触击退距离（px，参考 KnockbackOnAttackEffect 默认 150）。0 = 无击退。</summary>
        [Export(PropertyHint.Range, "0,500,10")] public float KnockbackDistance = 150f;
        /// <summary>接触击退持续时间（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float KnockbackDuration = 0.3f;

        /// <summary>灼烧来源（玩家），由生成方设置。</summary>
        public GameActor? Attacker { get; set; }

        private Area2D? _contactArea;
        private CapsuleShape2D? _contactShape;
        private Sprite2D? _visualSprite;
        private Vector2 _baseSpriteScale = Vector2.One;
        private float _elapsed;
        private float _currentRadius;
        private readonly HashSet<GameActor> _hitActors = new(); // 已触发接触伤害/击退的敌人（每敌人仅一次）

        public override void _Ready()
        {
            // 接触区域：水平胶囊体（左右长、上下短，匹配透视下的椭圆视觉），尺寸跟随扩散进度
            _contactArea = new Area2D
            {
                CollisionLayer = 0,
                CollisionMask = TargetCollisionMask,
                Monitoring = true,
                Monitorable = false,
            };
            _contactShape = new CapsuleShape2D { Radius = 0f, Height = 0f };
            // CapsuleShape2D 默认沿 Y 轴，旋转 90° 变水平（长轴 = 左右）
            _contactArea.AddChild(new CollisionShape2D { Shape = _contactShape, Rotation = Mathf.Pi / 2f });
            AddChild(_contactArea);
            _contactArea.BodyEntered += OnBodyEntered;
            _contactArea.AreaEntered += OnAreaEntered;

            // 视觉 Sprite：记录基准缩放（保留场景里的压扁比例），随扩散进度整体放大
            if (!VisualSpritePath.IsEmpty)
            {
                _visualSprite = GetNodeOrNull<Sprite2D>(VisualSpritePath);
                if (_visualSprite != null)
                    _baseSpriteScale = _visualSprite.Scale;
            }

            QueueRedraw();
        }

        public override void _PhysicsProcess(double delta)
        {
            _elapsed += (float)delta;
            if (_elapsed >= Lifetime) { QueueFree(); return; }

            // 持续扩散：0 → MaxRadius。
            // 水平胶囊：Radius = 上下半宽（短轴），Height = 总长（Godot 4 中 CapsuleShape2D.Height
            // 是含两端半球的整体长度，直接 = 左右直径 2r）。
            // 总长 = Height = 2r，总高 = 2×Radius = 2r×aspect
            float t = Mathf.Clamp(_elapsed / ExpandDuration, 0f, 1f);
            _currentRadius = MaxRadius * t;
            if (_contactShape != null)
            {
                _contactShape.Radius = _currentRadius * DamageAreaAspectY;
                _contactShape.Height = 2f * _currentRadius;
            }

            // 视觉环同步扩张：Scale = 基准缩放 × 当前半径 / 基准半径
            if (_visualSprite != null && VisualBaseRadius > 0f)
            {
                float f = Mathf.Max(_currentRadius / VisualBaseRadius, 0.001f);
                _visualSprite.Scale = _baseSpriteScale * f;
            }

            QueueRedraw();
        }

        public override void _Draw()
        {
            if (!ShowDebugRing) return;
            // 调试：绘制水平胶囊判定区域（中部矩形 + 两端半圆，与灼烧判定范围一致）
            float radius = _currentRadius * DamageAreaAspectY;
            float halfMid = Mathf.Max(_currentRadius - radius, 0f); // 中间直段半长
            var glow = new Color(RingColor.R, RingColor.G, RingColor.B, RingColor.A * 0.15f);
            DrawRect(new Rect2(-halfMid, -radius, halfMid * 2f, radius * 2f), glow, true);
            DrawCircle(new Vector2(-halfMid, 0f), radius, glow);
            DrawCircle(new Vector2(halfMid, 0f), radius, glow);
            DrawRect(new Rect2(-halfMid, -radius, halfMid * 2f, radius * 2f), RingColor, false, 2f);
            DrawArc(new Vector2(-halfMid, 0f), radius, 0f, Mathf.Tau, 32, RingColor, 2f);
            DrawArc(new Vector2(halfMid, 0f), radius, 0f, Mathf.Tau, 32, RingColor, 2f);
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

        /// <summary>对进入火焰环的敌人：首次接触触发一次直接伤害与击退，随后施加灼烧
        /// （重复施加由 DotBurnEffect 的 EffectId 刷新覆盖）。</summary>
        private void ApplyBurn(GameActor target)
        {
            if (target.IsDeathSequenceActive || target.IsDead) return;

            // 首次接触：直接伤害 + 击退（每个敌人仅触发一次，离开再进入不重复）
            if (_hitActors.Add(target))
                DealContactDamage(target);

            foreach (var entry in BurnEffectEntries)
            {
                if (entry?.Scene == null) continue;
                if (entry.InstantiateEffect() is not DotBurnEffect burn) continue;
                burn.Attacker = Attacker;
                target.ApplyEffect(burn);
            }
        }

        /// <summary>接触瞬间的直接伤害（AreaEffect 来源）与击退（同 KnockbackOnAttackEffect：
        /// 线性减速总位移 = v0×T/2，故初速度 = 2×距离/时长；ForcedMovement 免疫时跳过）。</summary>
        private void DealContactDamage(GameActor target)
        {
            if (Attacker == null) return;

            if (ContactDamage > 0f)
            {
                DamageDispatcher.DealDamage(target, ContactDamage, GlobalPosition, Attacker,
                    DamageSource.AreaEffect, TargetableFactions, false);
            }

            if (KnockbackDistance <= 0f) return;
            if (target.ActiveImmunities.HasFlag(ImmunityFlags.ForcedMovement)) return;

            Vector2 dir = target.GlobalPosition - GlobalPosition;
            if (dir == Vector2.Zero) dir = Vector2.Right;

            // 位移请求（与武器击退同通道——受击动画随位移锁定）
            target.ApplyKnockbackDisplacement(dir.Normalized(), KnockbackDistance, KnockbackDuration);
        }
    }
}
