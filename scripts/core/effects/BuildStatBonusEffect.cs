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

        private readonly Dictionary<string, float> _originals = new();
        private bool _captured;

        protected override void OnApply()
        {
            if (!_captured)
            {
                SaveOriginals();
                _captured = true;
            }
            ApplyDeltas();
        }

        protected override void OnStackRefreshed()
        {
            ApplyDeltas();
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

        private void ApplyDeltas()
        {
            if (Actor == null) return;
            foreach (var kvp in StatBonuses)
            {
                if (StatOp.Registry.TryGetValue(kvp.Key, out var op))
                    op.Apply(Actor, kvp.Value);
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
