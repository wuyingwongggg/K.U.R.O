using Godot;
using Godot.Collections;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 构筑效果属性修正。OnApply 保存原始值，OnRemoved 恢复。
    /// 新增属性只需在 StatOp.Registry 中加一行。
    /// </summary>
    public partial class BuildStatBonusEffect : ActorEffect
    {
        [Export]
        public Dictionary<string, float> StatBonuses { get; set; } = new();

        /// <summary>每次 Refresh 时的倍率增量（0.25 = 第2次选+25%, 第3次选+50%）。</summary>
        [Export(PropertyHint.Range, "0,2,0.01")] public float StackMultiplier { get; set; } = 0.25f;

        private readonly Dictionary<string, float> _originals = new();
        private bool _captured;
        private int _refreshCount;

        protected override void OnApply()
        {
            if (!_captured)
            {
                SaveOriginals();
                _captured = true;
            }
            _refreshCount = 0;
            ApplyDeltas(1f);
        }

        protected override void OnStackRefreshed()
        {
            _refreshCount++;
            float scale = 1f + _refreshCount * StackMultiplier;
            ApplyDeltas(scale);
        }

        public override void OnRemoved()
        {
            RevertOriginals();
            base.OnRemoved();
        }

        private void SaveOriginals()
        {
            if (Actor == null) return;
            foreach (var kvp in StatOp.Registry)
                _originals[kvp.Key] = kvp.Value.GetOriginal(Actor);
        }

        private void ApplyDeltas(float scale)
        {
            if (Actor == null) return;
            foreach (var kvp in StatBonuses)
            {
                if (StatOp.Registry.TryGetValue(kvp.Key, out var op))
                    op.Apply(Actor, kvp.Value * scale);
            }
        }

        private void RevertOriginals()
        {
            if (Actor == null) return;
            foreach (var kvp in _originals)
            {
                if (StatOp.Registry.TryGetValue(kvp.Key, out var op))
                    op.Revert(Actor, kvp.Value);
            }
        }
    }
}
