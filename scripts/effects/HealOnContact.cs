using Godot;
using Kuros.Core;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>
    /// 接触治疗：挂在 Area2D 下，任意进入碰撞体的玩家恢复生命后自毁父节点。
    /// 可复用于血包、飞行治疗道具、掉落物等任意场景。
    /// </summary>
    [GlobalClass]
    public partial class HealOnContact : Node
    {
        /// <summary>治疗量：按目标最大血量的百分比（1-100）。</summary>
        [Export(PropertyHint.Range, "1,100,1")]
        public int HealAmount { get; set; } = 5;

        [Export]
        public string TargetGroup { get; set; } = "player";

        private Area2D? _area;
        private Node2D? _owner;

        public override void _Ready()
        {
            _area = GetParentOrNull<Area2D>();
            if (_area == null)
            {
                GD.PushWarning("[HealOnContact] 必须挂在 Area2D 子节点下");
                return;
            }

            _area.BodyEntered += OnBodyEntered;
            _owner = _area.GetParentOrNull<Node2D>();
        }

        public override void _ExitTree()
        {
            if (_area != null)
                _area.BodyEntered -= OnBodyEntered;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!body.IsInGroup(TargetGroup)) return;
            if (body is not GameActor actor) return;

            // 按最大血量百分比计算实际治疗量（至少 1 点）
            int heal = Mathf.Max(1, Mathf.RoundToInt(actor.MaxHealth * HealAmount / 100f));

            actor.RestoreHealth(actor.CurrentHealth + heal);
            FloatingDamageTextManager.Instance.ShowFloatingHealing(heal, _owner?.GlobalPosition ?? actor.GlobalPosition);
            _owner?.QueueFree();
        }
    }
}
