using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 流血效果：攻击命中目标后附加持续伤害，每 TickInterval 秒造成 DamagePercentPerSecond% 当前生命值的伤害，
    /// 持续 BleedDuration 秒。同一目标重复命中刷新持续时间。
    /// 目标级去重：流血状态由目标身上的实例持有（BleedSelfOnApply），目标身上永远只有 1 个 DotBleedEffect——
    /// 再次命中按 EffectId 去重刷新（不重复施加）；玩家身上只挂"命中监听"实例（本实例本身不持有流血状态），
    /// 冲刺替换/换武器移除监听实例不会中断目标身上的流血。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class DotBleedEffect : ActorEffect
    {
        [ExportGroup("Bleed")]
        /// <summary>每次 Tick 造成的伤害，按目标当前生命值的百分比（%）。</summary>
        [Export(PropertyHint.Range, "0.5,50,0.5")]
        public float DamagePercentPerSecond { get; set; } = 5f;

        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float TickInterval { get; set; } = 1f;

        [Export(PropertyHint.Range, "1,30,1")]
        public float BleedDuration { get; set; } = 3f;

        [ExportGroup("Visual")]
        [Export] public Vector2 VisualOffset { get; set; } = Vector2.Zero;

        /// <summary>
        /// 被动挂载模式：效果被施加时立即对自己施加流血（不等待"宿主攻击命中"）——
        /// 供区域组件等被动路径使用（如 SpikeAttackEffect 的区域伤害附带流血）。
        /// </summary>
        [Export] public bool BleedSelfOnApply = false;

        /// <summary>
        /// 流血伤害归属的攻击者。目标级挂载时 Actor=目标自身，需显式传入真正攻击者（命中玩家），
        /// 否则流血击杀归因错误（目标自己杀死自己）。
        /// </summary>
        public GameActor? BleedAttacker { get; set; }

        private bool _subscribed;
        private Node2D? _bleedVisualTemplate;
        private readonly Dictionary<GameActor, BleedState> _bleeds = new();

        public DotBleedEffect()
        {
            EffectId = "dot_bleed";
            DisplayName = "流血";
            Description = "攻击命中后对目标造成基于当前生命值的持续流血伤害。";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        public override void _Ready()
        {
            base._Ready();
            _bleedVisualTemplate = GetNodeOrNull<Node2D>("BleedVisual");
        }

        protected override void OnApply()
        {
            base.OnApply();
            if (BleedSelfOnApply)
            {
                // 被动挂载模式（目标级）：立即对自己施加流血，不订阅"宿主攻击命中"监听；
                // 实例生命周期跟随流血时长（到期由 EffectController 自动移除 → OnRemoved 清理）
                if (Actor != null)
                {
                    Duration = BleedDuration;
                    ApplyBleed(Actor);
                }
                return;
            }

            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
        }

        protected override void OnStackRefreshed()
        {
            base.OnStackRefreshed();
            if (BleedSelfOnApply && Actor != null)
            {
                // 被动模式：重复施加（同 EffectId 刷新）→ 刷新流血时长（ApplyBleed 内部对已有状态重启 Timer）
                ApplyBleed(Actor);
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            // 实例退役（目标死亡/流血到期/场景卸载）时清理全部流血状态（计时器/视觉）。
            // 目标级挂载后移除只发生在目标死亡或真实清理——玩家身上的监听实例不持有流血状态，此处为空操作
            ClearAllBleeds();
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor) return;
            if (damage <= 0) return;
            if (target.IsDeathSequenceActive || target.IsDead) return;

            // 目标级去重：流血状态由目标身上的实例持有——
            // 目标已有同 EffectId 实例 → 最新配置覆盖 + 刷新流血计时（不重复施加，天然无共存）；
            // 没有 → 在目标身上挂载新实例（BleedSelfOnApply 挂载即对自己流血）。
            // 玩家身上的本实例只负责命中监听、不持有流血状态——冲刺替换/换武器移除监听实例不会中断目标流血
            var controller = target.EffectController;
            if (controller == null) return;

            if (controller.GetEffect(EffectId) is DotBleedEffect existing)
            {
                existing.DamagePercentPerSecond = DamagePercentPerSecond;
                existing.TickInterval = TickInterval;
                existing.BleedDuration = BleedDuration;
                existing.VisualOffset = VisualOffset;
                existing.Duration = BleedDuration;
                existing.BleedAttacker = attacker;
                existing.Refresh(); // _elapsed 归零 + OnStackRefreshed → ApplyBleed 重启流血计时
                return;
            }

            var bleed = Duplicate() as DotBleedEffect;
            if (bleed == null) return;
            bleed.BleedSelfOnApply = true;
            bleed.BleedAttacker = attacker;
            target.ApplyEffect(bleed);
        }

        /// <summary>对目标施加/刷新流血状态（已有状态则重启计时）。
        /// 目标级去重下供命中监听实例与区域组件调用；伤害归属用 BleedAttacker（真正攻击者），回退 Actor。</summary>
        public void ApplyBleed(GameActor target)
        {
            if (_bleeds.TryGetValue(target, out var existing))
            {
                existing.ExpiryTimer.Start(BleedDuration);
                return;
            }

            var tickTimer = new Timer { OneShot = false, WaitTime = TickInterval, Autostart = true };
            var expiryTimer = new Timer { OneShot = true, WaitTime = BleedDuration, Autostart = true };
            Node2D? visual = null;

            if (_bleedVisualTemplate != null)
            {
                visual = _bleedVisualTemplate.Duplicate() as Node2D;
                if (visual != null)
                {
                    visual.Visible = true;
                    // 视觉锚点：VisualEffectArea 优先（高个子敌人血滴不落在脚底），回退 HitArea/原点
                    visual.Position = target.ToLocal(target.GetVisualAnchorWorld()) + VisualOffset;
                    if (visual is GpuParticles2D particles)
                        particles.Emitting = true;
                    target.AddChild(visual);
                }
            }

            var capturedTarget = target;
            tickTimer.Timeout += () =>
            {
                if (!IsInstanceValid(capturedTarget) || capturedTarget.IsDeathSequenceActive || capturedTarget.IsDead)
                {
                    CleanupBleed(capturedTarget);
                    return;
                }

                int bleedDamage = Mathf.Max(1,
                    Mathf.RoundToInt(capturedTarget.CurrentHealth * DamagePercentPerSecond / 100f * TickInterval));
                capturedTarget.TakeDamage(bleedDamage, Vector2.Zero, BleedAttacker ?? Actor, DamageSource.EffectBonus);

                if (capturedTarget.IsDeathSequenceActive || capturedTarget.IsDead)
                    CleanupBleed(capturedTarget);
            };

            expiryTimer.Timeout += () => CleanupBleed(capturedTarget);
            target.TreeExiting += () => CleanupBleed(capturedTarget);
            target.DamageTaken += _ =>
            {
                if (capturedTarget.IsDeathSequenceActive || capturedTarget.IsDead)
                    CleanupBleed(capturedTarget);
            };

            target.AddChild(tickTimer);
            target.AddChild(expiryTimer);

            _bleeds[target] = new BleedState { TickTimer = tickTimer, ExpiryTimer = expiryTimer, Visual = visual };

            // 立即造成首次伤害
            int initialDamage = Mathf.Max(1,
                Mathf.RoundToInt(target.CurrentHealth * DamagePercentPerSecond / 100f * TickInterval));
            target.TakeDamage(initialDamage, Vector2.Zero, BleedAttacker ?? Actor, DamageSource.EffectBonus);
        }

        private void CleanupBleed(GameActor target)
        {
            if (!_bleeds.Remove(target, out var state)) return;

            if (IsInstanceValid(state.TickTimer))
                state.TickTimer.QueueFree();
            if (IsInstanceValid(state.ExpiryTimer))
                state.ExpiryTimer.QueueFree();
            KillVisual(state.Visual);
        }

        private void ClearAllBleeds()
        {
            foreach (var target in _bleeds.Keys)
            {
                if (_bleeds.TryGetValue(target, out var state))
                {
                    if (IsInstanceValid(state.TickTimer))
                        state.TickTimer.QueueFree();
                    if (IsInstanceValid(state.ExpiryTimer))
                        state.ExpiryTimer.QueueFree();
                    KillVisual(state.Visual);
                }
            }
            _bleeds.Clear();
        }

        private static void KillVisual(Node2D? visual)
        {
            if (!IsInstanceValid(visual)) return;
            ClearAllParticles(visual);
            visual.QueueFree();
        }

        private static void ClearAllParticles(Node node)
        {
            if (node is GpuParticles2D p)
            {
                p.Emitting = false;
                p.Amount = 0;
            }
            foreach (var child in node.GetChildren())
                ClearAllParticles(child);
        }

        private sealed class BleedState
        {
            public Timer TickTimer = null!;
            public Timer ExpiryTimer = null!;
            public Node2D? Visual;
        }
    }
}
