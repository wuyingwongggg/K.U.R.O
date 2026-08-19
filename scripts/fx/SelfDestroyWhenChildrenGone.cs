using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 子节点耗尽自毁：所有子节点销毁（GetChildCount == 0）后自身 QueueFree。
    /// 用于"纯容器"特效根节点——子特效各自自毁后，空壳根不应残留在场景树
    /// （残留会导致全局组检测（如 AttackEffectEntry.UniqueGroup）永远命中）。
    /// </summary>
    [GlobalClass]
    public partial class SelfDestroyWhenChildrenGone : Node2D
    {
        public override void _Process(double delta)
        {
            if (GetChildCount() == 0)
            {
                QueueFree();
            }
        }
    }
}
