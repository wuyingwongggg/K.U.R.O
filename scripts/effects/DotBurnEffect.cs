using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 灼烧效果：施加到目标身上，每 TickInterval 秒造成基于施加者攻击力的持续伤害。
    /// 灼烧期间在目标身上显示 BurnVisual 火焰粒子并跟随目标。
    /// 重复施加时 EffectController 按 EffectId 刷新持续时间（覆盖）。
    /// </summary>
    [GlobalClass]
    public partial class DotBurnEffect : ActorEffect
    {
        [ExportGroup("Burn")]
        /// <summary>每次 Tick 造成的伤害 = 施加者攻击力 × 此百分比。</summary>
        [Export(PropertyHint.Range, "1,200,1")]
        public float DamagePercentPerSecond { get; set; } = 20f; // 每秒造成的伤害百分比

        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float TickInterval { get; set; } = 1f;

        /// <summary>施加者（伤害来源）。不设置时使用 ApplyEffect 的 Actor。</summary>
        public GameActor? Attacker { get; set; }

        private float _tickAccum;
        private Node2D? _burnVisual;

        public DotBurnEffect()
        {
            EffectId = "dot_burn";
            DisplayName = "灼烧";
            Description = "持续灼烧伤害，重复施加刷新持续时间。";
            IsBuff = false;
            Duration = 3f;
            MaxStacks = 1;
        }

        public override void _Ready()
        {
            base._Ready();
            _burnVisual = GetNodeOrNull<Node2D>("BurnVisual")
                ?? GetNodeOrNull<Node2D>("AnimatedSprite2D");
        }

        protected override void OnApply()
        {
            _tickAccum = 0f;
            ShowBurnVisual();
        }

        protected override void OnStackRefreshed()
        {
            _tickAccum = 0f;
            ShowBurnVisual();
        }

        protected override void OnTick(double delta)
        {
            // 火焰特效跟随目标 HitArea 中心
            if (_burnVisual != null && Actor != null)
            {
                _burnVisual.Visible = true;
                _burnVisual.GlobalPosition = GetHitCenterWorld(Actor);
            }

            _tickAccum += (float)delta;

            if (_tickAccum < TickInterval) return;
            _tickAccum -= TickInterval;

            int damage = Mathf.Max(1,
                Mathf.RoundToInt((Attacker?.AttackDamage ?? Actor?.AttackDamage ?? 10f)
                    * DamagePercentPerSecond / 100f * TickInterval));
            Actor.TakeDamage(damage, Vector2.Zero, Attacker ?? Actor, DamageSource.EffectBonus);
        }

        public override void OnRemoved()
        {
            HideBurnVisual();
            base.OnRemoved();
        }

        private void ShowBurnVisual()
        {
            if (_burnVisual == null) return;
            _burnVisual.Visible = true;
        }

        private void HideBurnVisual()
        {
            if (_burnVisual == null) return;
            _burnVisual.Visible = false;
        }

        private static Vector2 GetHitCenterWorld(GameActor target)
        {
            var hitArea = target.GetNodeOrNull<Area2D>("HitArea")
                ?? target.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? target.GlobalPosition;
        }
    }
}
