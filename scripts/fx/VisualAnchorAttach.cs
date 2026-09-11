using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 视觉锚点挂载工具(EFFECT_STANDARD 第十节配套):
    /// 把一次性视觉特效挂到目标身上——挂 <c>VisualEffectPosition</c> 容器(无则目标根),
    /// **根坐标保持原点**(排序键=脚底,后绘 → 画在角色之上),锚点偏移由内部子节点承载。
    /// 不要把根节点摆在锚点坐标:目标根是 y_sort 容器时,根 Y 即排序键,会被排到角色后(遮挡)。
    /// 也不要用 AddChild 重挂承载偏移:重挂默认保留全局变换,会反向补偿抵消偏移。
    /// </summary>
    public static class VisualAnchorAttach
    {
        public static void Attach(Node2D fx, GameActor owner)
        {
            if (fx == null || owner == null
                || !GodotObject.IsInstanceValid(fx) || !GodotObject.IsInstanceValid(owner))
                return;

            var container = owner.GetNodeOrNull<Node2D>("VisualEffectPosition") ?? (Node2D)owner;
            container.AddChild(fx);
            fx.Position = Vector2.Zero;

            Vector2 anchorLocal = container.ToLocal(owner.GetVisualAnchorWorld());
            foreach (Node child in fx.GetChildren())
            {
                if (child is Node2D childNode)
                    childNode.Position += anchorLocal;
            }
        }
    }
}
