using System;
using System.Collections.Generic;
using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 世界物未拾取过期组件（方案 B）：挂在世界物（WorldItemEntity/RigidBodyWorldItemEntity）根下，
    /// 物品静止（settled）后开始累计——剩余不足 BlinkLeadDuration 时启动**消失预警呼吸**：
    /// alpha 在 [BreathMinAlpha, 1] 间平滑脉动且周期随剩余时间缩短（愈接近消失愈急促），
    /// 最后一帧全灭并销毁父节点。
    ///
    /// 透明通道：自定义 canvas shader（如 sparkling）下 item Modulate.a 的透明控制不可靠——
    /// 呼吸启动时**仅剥离 sparkling.gdshader 材质节点**（该节点回默认管线），随后由 Modulate.a 驱动呼吸；
    /// 其它自定义材质（描边等）不受影响。
    /// </summary>
    [GlobalClass]
    public partial class WorldItemExpiry : Node
    {
        /// <summary>未拾取存活时长（秒）。0 = 禁用。物品静止后开始计时。</summary>
        [Export(PropertyHint.Range, "0,300,1")] public float UnpickedLifetime { get; set; } = 0f;

        /// <summary>消失前闪烁预警时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.5,10,0.5")] public float BlinkLeadDuration { get; set; } = 8f;

        /// <summary>闪烁周期区间：开始周期 → 结束周期（越接近消失越急促）。</summary>
        [Export] public float BlinkStartPeriod = 1.4f;
        [Export] public float BlinkEndPeriod = 0.25f;

        /// <summary>呼吸波谷的最低透明度（0~1）：不完全消失——物品始终隐约可见；
        /// 只有最后一帧才真正全灭并销毁。0 = 每周期跌到全透明闪断。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float BreathMinAlpha = 0.3f;

        private double _settledSeconds;
        private double _blinkElapsed;
        private double _phase;
        private bool _blinking;
        private bool _materialsStripped;

        private CanvasItem? _owner;

        public override void _Ready()
        {
            _owner = GetParent() as CanvasItem;
        }

        public override void _Process(double delta)
        {
            if (UnpickedLifetime <= 0f || _owner == null || !GodotObject.IsInstanceValid(_owner)) return;

            bool settled = IsOwnerSettled(_owner);
            if (!settled)
            {
                // 未静止（飞行/回弹/隐藏中）：不累计
                return;
            }

            _settledSeconds += delta;
            double remaining = UnpickedLifetime - _settledSeconds;
            if (remaining <= 0.0)
            {
                _owner.QueueFree();
                return;
            }

            if (!_blinking && remaining <= BlinkLeadDuration)
            {
                _blinking = true;
                StripSparklingMaterials(_owner); // 仅剥离 sparkling 材质 → 该节点回默认管线，Modulate.a 生效
            }

            if (!_blinking) return;

            _blinkElapsed += delta;
            double t = Mathf.Clamp(_blinkElapsed / BlinkLeadDuration, 0.0, 1.0);
            double period = Mathf.Lerp(BlinkStartPeriod, BlinkEndPeriod, t);
            _phase += delta / period;

            // 柔和呼吸：alpha 在 [BreathMinAlpha, 1] 平滑脉动；最后一帧才真正全灭
            double alpha = t >= 1.0
                ? 0.0
                : BreathMinAlpha + (1.0 - BreathMinAlpha) * (0.5 + 0.5 * Math.Cos(2.0 * Math.PI * _phase));
            _owner.Modulate = new Color(_owner.Modulate.R, _owner.Modulate.G, _owner.Modulate.B, (float)alpha);

            if (t >= 1.0)
                _owner.QueueFree();
        }

        /// <summary>递归子树（含自身）：仅剥离 sparkling.gdshader 材质节点——
        /// 该节点回默认管线使 Modulate.a 生效；其它自定义材质（描边等）不受影响。
        /// 物品销毁前不再恢复（预警期火花停用）。</summary>
        private static void StripSparklingMaterials(CanvasItem root)
        {
            var stack = new Stack<CanvasItem>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var item = stack.Pop();
                if (item.Material is ShaderMaterial sm
                    && sm.Shader != null
                    && sm.Shader.ResourcePath.EndsWith("sparkling.gdshader"))
                {
                    item.Material = null;
                }
                foreach (var child in item.GetChildren())
                {
                    if (child is CanvasItem ci)
                        stack.Push(ci);
                }
            }
        }

        private static bool IsOwnerSettled(CanvasItem owner)
        {
            return owner switch
            {
                Kuros.Items.World.WorldItemEntity w => w.IsSettledForExpiry,
                Kuros.Items.World.RigidBodyWorldItemEntity r => r.IsSettledForExpiry,
                _ => true
            };
        }
    }
}
