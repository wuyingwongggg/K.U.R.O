using Godot;
using Godot.Collections;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 构筑效果属性修正。OnApply 保存原始值，OnRemoved 恢复，
    /// OnStackRefreshed 叠加一层增量。
    /// 新增属性只需在 ApplyDelta / RevertDelta 中各加一行。
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
            _originals["speed"] = Actor.Speed;
            _originals["attack_damage"] = Actor.AttackDamage;
            _originals["max_health"] = Actor.MaxHealth;
        }

        private void ApplyDeltas()
        {
            if (Actor == null) return;
            foreach (var kvp in StatBonuses)
            {
                ApplyDelta(kvp.Key, kvp.Value);
            }
        }

        private void ApplyDelta(string key, float value)
        {
            switch (key)
            {
                case "attack_damage":
                    Actor.AttackDamage += value;
                    break;
                case "speed":
                    Actor.Speed += value;
                    break;
                case "max_health":
                    int rounded = Mathf.RoundToInt(value);
                    Actor.MaxHealth += rounded;
                    Actor.RestoreHealth(Actor.CurrentHealth + rounded);
                    break;
            }
        }

        private void RevertOriginals()
        {
            if (Actor == null) return;
            foreach (var kvp in _originals)
            {
                RevertDelta(kvp.Key, kvp.Value);
            }
        }

        private void RevertDelta(string key, float original)
        {
            switch (key)
            {
                case "attack_damage":
                    Actor.AttackDamage = original;
                    break;
                case "speed":
                    Actor.Speed = original;
                    break;
                case "max_health":
                    Actor.MaxHealth = Mathf.RoundToInt(original);
                    break;
            }
        }
    }
}
