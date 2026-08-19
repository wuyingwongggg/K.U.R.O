using Godot;
using System.Collections.Generic;

namespace Kuros.Effects
{
    /// <summary>
    /// 火墙容器：把 Segments 段实例化到父节点（保持平级渲染层级）。
    /// 段耗尽自毁（同 SelfDestroyWhenChildrenGone 语义）：所有生成的段销毁后，空壳容器自身 QueueFree——
    /// 段挂父节点而非子节点，故不能用子节点计数，改为跟踪段引用。
    /// </summary>
    [GlobalClass]
    public partial class FireWall : Node2D
    {
        [Export] public Godot.Collections.Array<SceneSpawnEntry> Segments { get; set; } = new();

        private readonly List<Node> _spawnedSegments = new();

        public override void _Ready()
        {
            CallDeferred(nameof(SpawnSegments));
        }

        public override void _Process(double delta)
        {
            // 段耗尽自毁：所有生成的段已销毁（含未生成任何段的空容器）→ 自身 QueueFree
            if (_spawnedSegments.Count == 0)
            {
                QueueFree();
                return;
            }

            foreach (var seg in _spawnedSegments)
            {
                if (IsInstanceValid(seg))
                {
                    return;
                }
            }

            QueueFree();
        }

        private void SpawnSegments()
        {
            var parent = GetParent();
            foreach (var seg in Segments)
            {
                if (seg?.Scene == null) continue;
                var instance = seg.Scene.Instantiate();
                parent.AddChild(instance);
                if (instance is Node2D n)
                {
                    n.GlobalPosition = GlobalPosition + seg.Position * Scale;
                    n.Scale *= Scale;
                }
                _spawnedSegments.Add(instance);
            }
        }
    }
}
