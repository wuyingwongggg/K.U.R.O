using Godot;
using Kuros.Core;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// SpikeAttackEffect 组合容器：挂载多个子效果（减速区域 + 流血触发等）。
    /// 实现 IAttackerProvider 并把投掷者（Attacker）转发给所有子效果——
    /// 投掷系统对根注入攻击来源，子效果（如 SlowHitAreaEffect 的区域伤害）需要它做伤害归属。
    /// 生命周期：跟随主效果（SlowHitArea）——其销毁时容器连同其余节点（BleedTrigger）一起销毁。
    /// </summary>
    [GlobalClass]
    public partial class SpikeAttackEffect : Node2D, IAttackerProvider
    {
        private GameActor? _attacker;
        private Node? _mainEffect; // 主效果（SlowHitArea）——容器生命周期跟随它

        public override void _Ready()
        {
            _mainEffect = GetNodeOrNull("SlowHitArea");
        }

        public override void _Process(double delta)
        {
            // 主效果已销毁 → 容器（连带 BleedTrigger 等附属节点）一起销毁
            if (_mainEffect == null || !GodotObject.IsInstanceValid(_mainEffect))
                QueueFree();
        }

        public GameActor? Attacker
        {
            get => _attacker;
            set
            {
                _attacker = value;
                if (value == null) return;
                // 转发给所有子 IAttackerProvider（SlowHitArea 等区域/伤害效果）
                foreach (Node child in GetChildren())
                {
                    if (child is IAttackerProvider provider)
                        provider.Attacker = value;
                }
            }
        }
    }
}
