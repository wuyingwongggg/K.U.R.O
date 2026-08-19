using Godot;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class BuildCard : Control
    {
        [Signal] public delegate void ConfirmedEventHandler(int index);

        [Export] public int CardIndex { get; set; }
        [Export] public Control? CardInner { get; set; }
        [Export] public TextureRect? CardBg { get; set; }
        [Export] public TextureRect? RarityGlow { get; set; }
        [Export] public TextureRect? Icon { get; set; }
        [Export] public Label? KeyLabel { get; set; }
        [Export] public Label? NameLabel { get; set; }
        [Export] public Label? BuildClassLabel { get; set; }
        [Export] public RichTextLabel? DescLabel { get; set; }
        [Export] public Label? ProgressLabel { get; set; }

        [Export(PropertyHint.Range, "5,30,1")]
        public float TiltStrength { get; set; } = 15f;

        [Export(PropertyHint.Range, "1,1.3,0.01")]
        public float HoverScale { get; set; } = 1.05f;

        [ExportGroup("Rarity Visuals")]
        [Export] public Texture2D? CardBgCommon { get; set; }
        [Export] public Texture2D? CardBgRare { get; set; }
        [Export] public Texture2D? CardBgEpic { get; set; }
        [Export] public Texture2D? CardBgCore { get; set; }
        [Export] public Color GlowCommon { get; set; } = new(0.2f, 0.4f, 1f, 0.6f);
        [Export] public Color GlowRare { get; set; } = new(0.6f, 0.2f, 1f, 0.6f);
        [Export] public Color GlowEpic { get; set; } = new(1f, 0.85f, 0.2f, 0.7f);
        [Export] public Color GlowCore { get; set; } = new(1f, 0.2f, 0.2f, 0.8f);

        public void ApplyRarityVisuals(BuildRarity rarity)
        {
            if (CardBg != null)
            {
                CardBg.Texture = rarity switch
                {
                    BuildRarity.Common => CardBgCommon,
                    BuildRarity.Rare => CardBgRare,
                    BuildRarity.Epic => CardBgEpic,
                    BuildRarity.Core => CardBgCore,
                    _ => CardBgCommon,
                };
            }
            if (RarityGlow != null)
            {
                var color = rarity switch
                {
                    BuildRarity.Common => GlowCommon,
                    BuildRarity.Rare => GlowRare,
                    BuildRarity.Epic => GlowEpic,
                    BuildRarity.Core => GlowCore,
                    _ => GlowCommon,
                };
                RarityGlow.Modulate = color;
                var transparent = new Color(color.R, color.G, color.B, 0f);
                _glowShaderMat?.SetShaderParameter("starting_colour", color);
                _glowShaderMat?.SetShaderParameter("ending_colour", transparent);
            }
        }

        [Export]
        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (IsNodeReady())
                    ApplyEnabledState();
            }
        }
        private bool _enabled = true;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                UpdateSelectionHighlight();
            }
        }

        private bool _isHovered;
        private ShaderMaterial? _shaderMat;
        private ShaderMaterial? _glowShaderMat;
        private TextureRect? _displayRect;
        private SubViewport? _subViewport;
        private static readonly Vector2 ReferenceSize = new(500, 340);
        private float _currentRotY;
        private float _currentRotX;
        private float _currentHoverScale = 1f;

        [Export(PropertyHint.Range, "1,179,1")] public float MaxTiltFov { get; set; } = 30f;

        public void ApplyCardScale()
        {
            float ratio = Mathf.Min(Size.X / ReferenceSize.X, 1f);
            NameLabel?.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(30 * ratio));
            KeyLabel?.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(20 * ratio));
            BuildClassLabel?.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * ratio));
            DescLabel?.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * ratio));
            ProgressLabel?.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(10 * ratio));
        }

        public void SyncViewportSize()
        {
            if (_subViewport == null) return;
            _subViewport.Size = new Vector2I(Mathf.RoundToInt(Size.X), Mathf.RoundToInt(Size.Y));
        }


        public override void _Ready()
        {
            // 预热中文字体：导出版里字体按需异步加载，文本测量可能发生在字形就绪前，
            // 导致换行按错误宽度计算（编辑器进程早已缓存字体，无此问题）。
            GD.Load<FontFile>("res://assets/fonts/NotoSansSC-VF.ttf");

            ResolveExports();

            // 导出版（spine 模板）的文本服务器对无空格中文断词异常：整段文字被当作
            // 一个不可分割的"词"排成一行。中文描述改用按字符任意断行，视觉上与逐词断行一致。
            if (DescLabel != null)
                DescLabel.AutowrapMode = TextServer.AutowrapMode.Arbitrary;

            // Steal shader from CardBg, clear from all children (rendered raw into viewport)
            _shaderMat = CardBg?.Material as ShaderMaterial;
            if (_shaderMat != null)
            {
                _shaderMat = (ShaderMaterial)_shaderMat.Duplicate(true);
                CardBg!.Material = null;
            }

            // Create SubViewport and move CardInner into it
            _subViewport = new SubViewport
            {
                Name = "CardViewport",
                TransparentBg = true,
                Size = new Vector2I(Mathf.RoundToInt(Size.X), Mathf.RoundToInt(Size.Y)),
                // 导出版里系统字体回退的中文字形是异步光栅化的：
                // 默认 UpdateWhenVisible 模式下视口纹理可能在字形就绪前渲染一次就不再更新，
                // 导致文字缺失被"冻结"。改为 Always 每帧重渲染，字形就绪后文字即完整。
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            AddChild(_subViewport);

            // 卡片尺寸变化时同步视口尺寸与字号（导出版最大化窗口/DPI 差异会改变最终尺寸）
            Resized += () =>
            {
                SyncViewportSize();
                ApplyCardScale();
            };

            if (CardInner != null)
            {
                RemoveChild(CardInner);
                _subViewport.AddChild(CardInner);
                CardInner.SetAnchorsPreset(LayoutPreset.FullRect);
            }

            // DisplayRect shows the rendered viewport texture with shader
            _displayRect = new TextureRect
            {
                Name = "DisplayRect",
                MouseFilter = MouseFilterEnum.Stop,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                Texture = _subViewport.GetTexture(),
                Material = _shaderMat,
            };
            _displayRect.SetAnchorsPreset(LayoutPreset.FullRect);
            _displayRect.MouseEntered += () => { _isHovered = true; ZIndex = 1; UpdateSelectionHighlight(); };
            _displayRect.MouseExited += () => { _isHovered = false; ZIndex = 0; UpdateSelectionHighlight(); };
            _displayRect.GuiInput += OnDisplayRectGuiInput;
            AddChild(_displayRect);

            _glowShaderMat = RarityGlow?.Material as ShaderMaterial;

            ApplyEnabledState();
        }

        public override void _Process(double delta)
        {
            if (!_enabled || _displayRect == null) return;

            float targetRotY = 0f, targetRotX = 0f, targetScale = 1f;

            if (_isHovered)
            {
                var pos = _displayRect.GetLocalMousePosition();
                var size = _displayRect.Size;
                if (size.X > 0 && size.Y > 0)
                {
                    float u = Mathf.Clamp(pos.X / size.X, 0f, 1f);
                    float v = Mathf.Clamp(pos.Y / size.Y, 0f, 1f);
                    targetRotY = (u - 0.5f) * 2f * TiltStrength;
                    targetRotX = (0.5f - v) * 2f * TiltStrength;
                }
                targetScale = ReferenceSize.X * HoverScale / Size.X;
            }

            float lerpSpeed = 0.15f;
            _currentRotY = Mathf.Lerp(_currentRotY, targetRotY, lerpSpeed);
            _currentRotX = Mathf.Lerp(_currentRotX, targetRotX, lerpSpeed);
            _currentHoverScale = Mathf.Lerp(_currentHoverScale, targetScale, lerpSpeed);

            _shaderMat?.SetShaderParameter("rot_y_deg", _currentRotY);
            _shaderMat?.SetShaderParameter("rot_x_deg", _currentRotX);

            float tiltMag = Mathf.Clamp(
                Mathf.Sqrt(_currentRotY * _currentRotY + _currentRotX * _currentRotX) / TiltStrength, 0f, 1f);
            float fov = Mathf.Lerp(170f, MaxTiltFov, tiltMag);
            _shaderMat?.SetShaderParameter("fov", fov);

            _displayRect.PivotOffset = _displayRect.Size / 2f;
            _displayRect.Scale = new Vector2(_currentHoverScale, _currentHoverScale);

            bool atRest = !_isHovered
                && Mathf.Abs(_currentRotY) < 0.05f
                && Mathf.Abs(_currentRotX) < 0.05f
                && Mathf.Abs(_currentHoverScale - 1f) < 0.001f;
            if (atRest)
            {
                _currentRotY = 0f;
                _currentRotX = 0f;
                _currentHoverScale = 1f;
                _displayRect.Scale = Vector2.One;
            }
        }

        private void OnDisplayRectGuiInput(InputEvent @event)
        {
            if (!_enabled) return;
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                EmitSignal(SignalName.Confirmed, CardIndex);
        }

        private void UpdateSelectionHighlight()
        {
            if (RarityGlow != null)
                RarityGlow.Visible = _isSelected || _isHovered;
        }

        private void ApplyEnabledState()
        {
            Visible = _enabled;
            if (!_enabled)
            {
                _isHovered = false;
                ZIndex = 0;
                _currentRotY = 0f;
                _currentRotX = 0f;
                _currentHoverScale = 1f;
                if (_displayRect != null)
                    _displayRect.Scale = Vector2.One;
                _shaderMat?.SetShaderParameter("rot_y_deg", 0f);
                _shaderMat?.SetShaderParameter("rot_x_deg", 0f);
            }
        }

        private void ResolveExports()
        {
            CardInner ??= GetNodeOrNull<Control>("CardInner");
            CardBg ??= GetNodeOrNull<TextureRect>("CardInner/CardBg");
            RarityGlow ??= GetNodeOrNull<TextureRect>("CardInner/RarityGlow");
            Icon ??= GetNodeOrNull<TextureRect>("CardInner/Icon");
            KeyLabel ??= GetNodeOrNull<Label>("CardInner/KeyLabel");
            NameLabel ??= GetNodeOrNull<Label>("CardInner/NameLabel");
            BuildClassLabel ??= GetNodeOrNull<Label>("CardInner/BuildClassLabel");
            DescLabel ??= GetNodeOrNull<RichTextLabel>("CardInner/DescLabel");
            ProgressLabel ??= GetNodeOrNull<Label>("CardInner/ProgressLabel");
        }
    }
}
