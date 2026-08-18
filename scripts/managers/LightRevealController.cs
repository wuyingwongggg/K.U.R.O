using Godot;

namespace Kuros.Managers
{
    /// <summary>
    /// 逐盏亮灯控制器（暗层 + 光圈坐标窗口版）：
    /// - 暗层：单个 Sprite2D 黑色贴图覆盖整个房间，挂 dark_mask shader 模拟关灯状态。
    /// - 光圈：N 个节点（任意 Node2D/Sprite2D）只提供世界坐标——暗层 shader 把光圈位置处的
    ///   圆形区域 alpha 归零（完全露出场景），其余保持暗影。
    ///   玩家进入触发区后，光圈半径依次从 0 扩散到目标值 → 房间被一盏一盏真正照亮。
    /// </summary>
    [GlobalClass]
    public partial class LightRevealController : Node2D
    {
        [Export] public NodePath DarkLayerPath { get; set; } = new();
        /// <summary>
        /// 光圈分组（嵌套数组）：每组一个路径数组——同组所有灯**同时**亮起（共享同一延迟），
        /// 组与组之间间隔 FadeIntervalSeconds。节点只取坐标，不渲染自身。
        /// </summary>
        [Export] public Godot.Collections.Array<Godot.Collections.Array<NodePath>> SpotGroups { get; set; } = new();
        [Export] public NodePath TriggerAreaPath { get; set; } = new();
        /// <summary>暗层遮罩 shader（空 = 自动加载 dark_mask.gdshader）。</summary>
        [Export] public Shader? DarkMaskShader { get; set; }
        /// <summary>阴影色（圆外暗影；圆内 alpha 归零露出场景）。</summary>
        [Export] public Color DarkColor { get; set; } = new(0f, 0f, 0f, 0.85f);
        /// <summary>灯光照亮半径（px，圆内完全透明露出场景）。</summary>
        [Export(PropertyHint.Range, "50,3000,50")] public float SpotRadius { get; set; } = 500f;
        /// <summary>圆边缘柔化宽度（px）。</summary>
        [Export(PropertyHint.Range, "0,500,10")] public float Softness { get; set; } = 80f;
        /// <summary>单盏灯的扩散时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,3,0.05")] public float FadeDurationSeconds { get; set; } = 1.0f;
        /// <summary>相邻两盏灯的扩散间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0,2,0.05")] public float FadeIntervalSeconds { get; set; } = 0.8f;

        private const string DefaultMaskShaderPath = "res://shaders/materials/dark_mask.gdshader";
        private const int MaxLights = 16;
        private bool _started;
        private Sprite2D? _darkLayer;
        private ShaderMaterial? _maskMaterial;
        private readonly Godot.Collections.Array<Node2D> _spots = new();
        private readonly Godot.Collections.Array<int> _spotGroups = new();
        private readonly float[] _radii = new float[MaxLights];

        public override void _Ready()
        {
            if (DarkLayerPath != null && !DarkLayerPath.IsEmpty)
            {
                _darkLayer = GetNodeOrNull<CanvasItem>(DarkLayerPath) as Sprite2D;
            }
            if (_darkLayer == null)
            {
                GD.PrintErr("[LightReveal] 暗层 Sprite2D 未找到（需设置黑色贴图纹理）");
                return;
            }

            _darkLayer.Modulate = new Color(1f, 1f, 1f, 1f); // 颜色由 shader 统一控制

            var shader = DarkMaskShader ?? GD.Load<Shader>(DefaultMaskShaderPath);
            if (shader == null)
            {
                GD.PrintErr($"[LightReveal] 遮罩 shader 加载失败: {DefaultMaskShaderPath}");
                return;
            }

            _maskMaterial = new ShaderMaterial { Shader = shader };
            _darkLayer.Material = _maskMaterial;

            int index = 0;
            for (int groupIndex = 0; groupIndex < SpotGroups.Count; groupIndex++)
            {
                var group = SpotGroups[groupIndex];
                if (group == null) continue;
                foreach (var path in group)
                {
                    if (path == null || path.IsEmpty || index >= MaxLights) continue;
                    if (GetNodeOrNull<CanvasItem>(path) is Node2D spot)
                    {
                        _spots.Add(spot);
                        _spotGroups.Add(groupIndex);
                        _radii[index] = 0f;
                        index++;
                    }
                }
            }

            BindTrigger();
        }

        private void BindTrigger()
        {
            Area2D? trigger = null;
            if (TriggerAreaPath != null && !TriggerAreaPath.IsEmpty)
            {
                trigger = GetNodeOrNull<Area2D>(TriggerAreaPath);
            }
            if (trigger == null)
            {
                trigger = GetParent()?.GetNodeOrNull<Area2D>("LightTrigger");
            }
            if (trigger != null)
            {
                trigger.BodyEntered += OnTriggerBodyEntered;
            }
        }

        public override void _Process(double delta)
        {
            if (_maskMaterial == null || _darkLayer == null || _spots.Count == 0)
            {
                return;
            }

            // 每帧同步光圈世界坐标（转暗层局部空间）与当前半径到 shader
            var positions = new Godot.Collections.Array<Vector2>();
            var radii = new Godot.Collections.Array<float>();
            for (int i = 0; i < _spots.Count; i++)
            {
                positions.Add(_darkLayer.ToLocal(_spots[i].GlobalPosition));
                radii.Add(_radii[i]);
            }
            _maskMaterial.SetShaderParameter("light_count", _spots.Count);
            _maskMaterial.SetShaderParameter("light_positions", positions);
            _maskMaterial.SetShaderParameter("light_radii", radii);
            _maskMaterial.SetShaderParameter("dark_color", DarkColor);
            _maskMaterial.SetShaderParameter("softness", Softness);
        }

        private void OnTriggerBodyEntered(Node2D body)
        {
            if (_started) return;
            if (!body.IsInGroup("player")) return;
            StartReveal();
        }

        /// <summary>开始逐排亮灯：同组所有灯共享同一延迟（同时亮），组与组之间间隔 FadeIntervalSeconds。</summary>
        public void StartReveal()
        {
            if (_started) return;
            _started = true;

            for (int i = 0; i < _spots.Count; i++)
            {
                int index = i; // 捕获局部副本
                float delay = _spotGroups[i] * FadeIntervalSeconds;
                var tween = CreateTween();
                tween.TweenInterval(delay);
                tween.TweenMethod(Callable.From<float>(value => _radii[index] = value), 0f, SpotRadius, FadeDurationSeconds)
                    .SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.Out);
            }
        }
    }
}
