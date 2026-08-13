using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 淡入淡出后销毁：自身即 Sprite2D 特效，Ready 后自动 淡入 → 保持 → 淡出 → 销毁。
    /// 默认目标 = 自身（只影响本 Sprite2D）；TargetPath 可指向其他 CanvasItem。
    /// 每帧手动驱动 modulate:a（不依赖 Tween，属性路径无解析风险），保留原 RGB 颜色。
    /// </summary>
    [GlobalClass]
    public partial class FadeInOutDestroy : Sprite2D
    {
        /// <summary>淡入时长（秒），0 = 立即显示。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float FadeInDuration = 0.2f;
        /// <summary>全亮保持时长（秒）。</summary>
        [Export(PropertyHint.Range, "0,10,0.1")] public float HoldDuration = 0.2f;
        /// <summary>淡出时长（秒）。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float FadeOutDuration = 0.3f;
        /// <summary>淡入淡出的目标 CanvasItem。留空 = 自身（只影响本 Sprite2D，不波及父级/兄弟节点）。</summary>
        [Export] public NodePath? TargetPath { get; set; }

        private CanvasItem? _target;
        private float _elapsed;
        private float _totalDuration;

        public override void _Ready()
        {
            _target = TargetPath != null && !TargetPath.IsEmpty
                ? GetNodeOrNull<CanvasItem>(TargetPath)
                : this; // 默认目标 = 自身

            if (_target == null)
            {
                GD.PushWarning($"{Name}: 未找到淡入淡出目标 CanvasItem");
                QueueFree();
                return;
            }

            // 初始全透明，随后由 _Process 每帧推进
            SetAlpha(0f);
            _totalDuration = Mathf.Max(FadeInDuration, 0f)
                + Mathf.Max(HoldDuration, 0f)
                + Mathf.Max(FadeOutDuration, 0f);
        }

        public override void _Process(double delta)
        {
            if (_target == null) return;
            _elapsed += (float)delta;

            float a;
            if (_elapsed < FadeInDuration)
                a = FadeInDuration > 0f ? _elapsed / FadeInDuration : 1f;            // 淡入
            else if (_elapsed < FadeInDuration + HoldDuration)
                a = 1f;                                                              // 保持
            else
            {
                float outT = _elapsed - FadeInDuration - HoldDuration;               // 淡出
                a = FadeOutDuration > 0f ? 1f - outT / FadeOutDuration : 0f;
            }
            SetAlpha(Mathf.Clamp(a, 0f, 1f));

            if (_elapsed >= _totalDuration)
                DestroyTarget();
        }

        private void SetAlpha(float alpha)
        {
            var c = _target.Modulate;
            _target.Modulate = new Color(c.R, c.G, c.B, alpha);
        }

        private void DestroyTarget()
        {
            if (_target != this && _target != null && IsInstanceValid(_target))
                _target.QueueFree(); // TargetPath 指向外部目标时销毁它
            QueueFree();             // 自身（默认目标）总是销毁
        }
    }
}
