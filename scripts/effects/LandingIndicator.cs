using Godot;

namespace Kuros.Effects
{
    /// <summary>
    /// 落点攻击指示器。在目标位置显示缩圈警告，给玩家反应时间。
    ///
    /// 渲染优先级：
    ///   1. SpritePath 指定节点 / 自动查找子节点 AnimatedSprite2D 或 Sprite2D → 直接使用（支持序列帧动画）
    ///   2. 以上皆无 → _Draw() 程序化绘制（填充圆 + 圆环 + 十字准线）
    /// </summary>
    public partial class LandingIndicator : Node2D
    {
        [ExportCategory("Sprite Mode")]
        /// <summary>指向场景中 AnimatedSprite2D 或 Sprite2D 子节点的路径。留空则自动查找第一个。未找到则使用程序化绘制。</summary>
        [Export] public NodePath? SpritePath { get; set; }

        [ExportCategory("Animation")]
        [Export(PropertyHint.Range, "0.1,5,0.05")] public float WarningDuration { get; set; } = 0.8f;
        [Export(PropertyHint.Range, "0.5,10,0.1")] public float StartScale { get; set; } = 2.5f;
        [Export(PropertyHint.Range, "0.1,2,0.05")] public float EndScale { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0,1,0.05")] public float FadeStartRatio { get; set; } = 0.7f;
        [Export] public Color IndicatorColor { get; set; } = new Color(1f, 0.3f, 0.2f, 0.85f);

        [ExportCategory("Procedural Mode")]
        [Export] public Color FillColor { get; set; } = new Color(1f, 0.3f, 0.2f, 0.2f);
        [Export(PropertyHint.Range, "4,500,1")] public float StartRadius { get; set; } = 80f;
        [Export(PropertyHint.Range, "4,200,1")] public float EndRadius { get; set; } = 12f;
        [Export(PropertyHint.Range, "1,8,0.5")] public float LineWidth { get; set; } = 2.5f;

        private float _elapsed;
        private bool _started;
        private Node2D? _visualNode;
        private AnimatedSprite2D? _animatedSprite;
        private Tween? _tween;

        public override void _Ready()
        {
            // 1. 显式路径指定
            if (SpritePath != null && !SpritePath.IsEmpty)
                _visualNode = GetNodeOrNull<Node2D>(SpritePath);

            // 2. 自动查找子节点
            if (_visualNode == null)
            {
                foreach (var child in GetChildren())
                {
                    if (child is AnimatedSprite2D || child is Sprite2D)
                    {
                        _visualNode = child as Node2D;
                        break;
                    }
                }
            }

            _animatedSprite = _visualNode as AnimatedSprite2D;

            if (_visualNode != null)
                _visualNode.Modulate = IndicatorColor;
        }

        public void Start()
        {
            _started = true;
            _elapsed = 0f;

            if (_visualNode != null)
                _visualNode.Scale = Vector2.One * StartScale;

            _animatedSprite?.Play();
        }

        public override void _Process(double delta)
        {
            if (!_started) return;

            _elapsed += (float)delta;
            float t = Mathf.Clamp(_elapsed / WarningDuration, 0f, 1f);

            if (_visualNode != null)
            {
                _visualNode.Scale = Vector2.One * Mathf.Lerp(StartScale, EndScale, t);

                float alpha = IndicatorColor.A;
                if (t >= FadeStartRatio)
                {
                    float fadeT = (t - FadeStartRatio) / Mathf.Max(1f - FadeStartRatio, 0.001f);
                    alpha = Mathf.Lerp(IndicatorColor.A, 0f, fadeT);
                }
                _visualNode.Modulate = new Color(IndicatorColor.R, IndicatorColor.G, IndicatorColor.B, alpha);
            }
            else
            {
                QueueRedraw();
            }

            if (t >= 1f)
            {
                _started = false;
                _animatedSprite?.Stop();
                _tween?.Kill();
                _tween = CreateTween();
                _tween.TweenProperty(this, "modulate:a", 0f, 0.15f);
                _tween.TweenCallback(Callable.From(() => QueueFree()));
            }
        }

        public override void _Draw()
        {
            if (_visualNode != null) return;

            float t = Mathf.Clamp(_elapsed / WarningDuration, 0f, 1f);
            float radius = Mathf.Lerp(StartRadius, EndRadius, t);

            float alpha = 1f;
            if (t >= FadeStartRatio)
            {
                float fadeT = (t - FadeStartRatio) / Mathf.Max(1f - FadeStartRatio, 0.001f);
                alpha = 1f - fadeT;
            }

            Color outerColor = new Color(IndicatorColor.R, IndicatorColor.G, IndicatorColor.B, IndicatorColor.A * alpha);
            Color innerFill = new Color(FillColor.R, FillColor.G, FillColor.B, FillColor.A * alpha);

            DrawCircle(Vector2.Zero, radius, innerFill);
            DrawArc(Vector2.Zero, radius, 0f, Mathf.Tau, 32, outerColor, LineWidth);
            float crossSize = radius * 0.5f;
            DrawLine(new Vector2(-crossSize, 0), new Vector2(crossSize, 0), outerColor, LineWidth);
            DrawLine(new Vector2(0, -crossSize), new Vector2(0, crossSize), outerColor, LineWidth);
        }
    }
}
