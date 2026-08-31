using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 区域伤害附带流血组件：订阅 SlowHitAreaEffect 的 DamageDealtTo（仅本区域造成的伤害），
    /// 对受伤目标施加 DotBleedEffect（被动挂载模式 BleedSelfOnApply——挂载即对自己流血）。
    /// 每个目标有最小触发间隔（防每 tick 刷流血）；DotBleed 同 EffectId 自动刷新时长。
    /// </summary>
    [GlobalClass]
    public partial class HitTriggerBleedZone : Node
    {
        /// <summary>订阅的区域（其 DamageDealtTo 事件即触发源）。</summary>
        [Export] public SlowHitAreaEffect? Source { get; set; }

        /// <summary>施加的流血效果场景（DotBleedEffect.tscn）。</summary>
        [Export] public PackedScene? DotBleedEffectScene { get; set; }

        /// <summary>每个目标的最小触发间隔（秒），防止每 tick 重复刷流血。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float BleedApplyInterval { get; set; } = 1.0f;

        private readonly Dictionary<GameActor, float> _cooldowns = new();
        private bool _subscribed;

        public override void _Ready()
        {
            base._Ready();
            Subscribe();
        }

        public override void _Process(double delta)
        {
            if (_cooldowns.Count == 0) return;
            var toRemove = new List<GameActor>();
            foreach (var kvp in _cooldowns)
            {
                float remain = kvp.Value - (float)delta;
                if (remain <= 0f)
                    toRemove.Add(kvp.Key);
                else
                    _cooldowns[kvp.Key] = remain;
            }
            foreach (var actor in toRemove)
                _cooldowns.Remove(actor);
        }

        public override void _ExitTree()
        {
            Unsubscribe();
            base._ExitTree();
        }

        private void Subscribe()
        {
            if (_subscribed || Source == null) return;
            Source.DamageDealtTo += OnAreaDamageDealt;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Source == null) return;
            Source.DamageDealtTo -= OnAreaDamageDealt;
            _subscribed = false;
        }

        private void OnAreaDamageDealt(GameActor target)
        {
            if (DotBleedEffectScene == null) return;
            if (!GodotObject.IsInstanceValid(target) || target.IsDeadOrDying) return;

            // 冷却中：不重复施加（DotBleed 同 EffectId 会刷新时长，冷却控制触发频率）
            if (_cooldowns.ContainsKey(target)) return;

            var bleed = DotBleedEffectScene.Instantiate<ActorEffect>();
            if (bleed == null) return;
            // 被动挂载：代码设置 BleedSelfOnApply（不污染 DotBleedEffect.tscn——它还被武器技能复用）
            if (bleed is DotBleedEffect dotBleed)
                dotBleed.BleedSelfOnApply = true;
            target.ApplyEffect(bleed); // → 挂载即对自己流血

            _cooldowns[target] = BleedApplyInterval;
        }
    }
}
