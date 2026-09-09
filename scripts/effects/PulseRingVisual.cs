using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 一次性伪3D 脉冲扩散视觉:扩散环自中心扩张到 Radius 后淡出自毁。
    /// 生成方(ThrowPulseBurstEffect 等)在 AddChild 前设置 Radius/Duration 即可;
    /// 环色/透视角度由场景 ShaderMaterial 参数控制,此处只驱动 progress。
    /// </summary>
    [GlobalClass]
    public partial class PulseRingVisual : Node2D
    {
        [Export(PropertyHint.Range, "10,1000,1")] public float Radius { get; set; } = 300f;
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float Duration { get; set; } = 0.45f;

        private Sprite2D _ring = null!;
        private float _age;

        public override void _Ready()
        {
            _ring = GetNode<Sprite2D>("Ring");

            // 同一 PackedScene 多次实例化时子资源默认共享——复制材质,
            // 否则多家具同时脉冲会互相覆盖 progress/颜色,环显示错乱
            if (_ring.Material is ShaderMaterial)
                _ring.Material = (Material)_ring.Material.Duplicate();

            var tex = _ring.Texture;
            Vector2 baseSize = tex != null ? tex.GetSize() : Vector2.One;
            float diameter = Mathf.Max(Radius * 2f, 1f);
            _ring.Scale = new Vector2(diameter / baseSize.X, diameter / baseSize.Y);

            if (_ring.Material is ShaderMaterial mat)
                mat.SetShaderParameter("progress", 0f);
        }

        public override void _Process(double delta)
        {
            _age += (float)delta;
            if (_age >= Duration)
            {
                QueueFree();
                return;
            }

            float t = _age / Duration;
            if (_ring.Material is ShaderMaterial mat)
                mat.SetShaderParameter("progress", t);
        }
    }
}
