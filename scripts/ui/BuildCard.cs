using Godot;

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
        [Export] public Label? DescLabel { get; set; }
        [Export] public Label? ProgressLabel { get; set; }

        [Export(PropertyHint.Range, "5,30,1")]
        public float TiltStrength { get; set; } = 15f;

        [Export(PropertyHint.Range, "1,1.3,0.01")]
        public float HoverScale { get; set; } = 1.05f;

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
        private static readonly Vector2 ReferenceSize = new(500, 340);
        private float _currentRotY;
        private float _currentRotX;
        private float _currentHoverScale = 1f;

        public void ApplyCardScale()
        {
            float ratio = Mathf.Min(Size.X / ReferenceSize.X, 1f);
            if (NameLabel != null)
                NameLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(30 * ratio));
            if (KeyLabel != null)
                KeyLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(20 * ratio));
            if (BuildClassLabel != null)
                BuildClassLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * ratio));
            if (DescLabel != null)
                DescLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(12 * ratio));
            if (ProgressLabel != null)
                ProgressLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(10 * ratio));
        }

        public override void _Ready()
        {
            var hoverTarget = CardInner ?? this;
            hoverTarget.MouseEntered += () => { _isHovered = true; ZIndex = 1; };
            hoverTarget.MouseExited += () => { _isHovered = false; ZIndex = 0; };
            ResolveExports();
            _shaderMat = CardBg?.Material as ShaderMaterial;
            if (_shaderMat != null)
            {
                _shaderMat = (ShaderMaterial)_shaderMat.Duplicate(true);
                CardBg!.Material = _shaderMat;
                if (RarityGlow != null) RarityGlow.Material = _shaderMat;
                if (Icon != null) Icon.Material = _shaderMat;
            }
            ApplyEnabledState();
        }

        public override void _Process(double delta)
        {
            if (!_enabled || CardInner == null) return;

            float targetRotY = 0f, targetRotX = 0f, targetScale = 1f;

            if (_isHovered)
            {
                var pos = CardInner.GetLocalMousePosition();
                var size = CardInner.Size;
                if (size.X > 0 && size.Y > 0)
                {
                    float u = Mathf.Clamp(pos.X / size.X, 0f, 1f);
                    float v = Mathf.Clamp(pos.Y / size.Y, 0f, 1f);
                    targetRotY = (u - 0.5f) * 2f * TiltStrength;
                    targetRotX = (0.5f - v) * 2f * TiltStrength;
                }
                targetScale = HoverScale;
            }

            float lerpSpeed = 0.15f;
            _currentRotY = Mathf.Lerp(_currentRotY, targetRotY, lerpSpeed);
            _currentRotX = Mathf.Lerp(_currentRotX, targetRotX, lerpSpeed);
            _currentHoverScale = Mathf.Lerp(_currentHoverScale, targetScale, lerpSpeed);

            _shaderMat?.SetShaderParameter("rot_y_deg", _currentRotY);
            _shaderMat?.SetShaderParameter("rot_x_deg", _currentRotX);

            if (CardInner != null)
            {
                CardInner.PivotOffset = CardInner.Size / 2f;
                CardInner.Scale = new Vector2(_currentHoverScale, _currentHoverScale);
                CardInner.Position = new Vector2(_currentRotY * 0.4f, _currentRotX * 0.4f);
            }

            bool atRest = !_isHovered
                && Mathf.Abs(_currentRotY) < 0.05f
                && Mathf.Abs(_currentRotX) < 0.05f
                && Mathf.Abs(_currentHoverScale - 1f) < 0.001f;
            if (atRest)
            {
                _currentRotY = 0f;
                _currentRotX = 0f;
                _currentHoverScale = 1f;
                if (CardInner != null)
                {
                    CardInner.Position = Vector2.Zero;
                    CardInner.Scale = Vector2.One;
                }
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (!_enabled) return;
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                EmitSignal(SignalName.Confirmed, CardIndex);
        }

        private void UpdateSelectionHighlight()
        {
            if (RarityGlow != null)
                RarityGlow.Visible = _isSelected;
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
                if (CardInner != null)
                {
                    CardInner.Position = Vector2.Zero;
                    CardInner.Scale = Vector2.One;
                }
                _shaderMat?.SetShaderParameter("rot_y_deg", 0f);
                _shaderMat?.SetShaderParameter("rot_x_deg", 0f);
            }
        }

        private void ResolveExports()
        {
            CardInner ??= GetNodeOrNull<Control>("CardInner");
            CardBg ??= GetNodeOrNull<TextureRect>("CardBg");
            RarityGlow ??= GetNodeOrNull<TextureRect>("RarityGlow");
            Icon ??= GetNodeOrNull<TextureRect>("Icon");
            KeyLabel ??= GetNodeOrNull<Label>("KeyLabel");
            NameLabel ??= GetNodeOrNull<Label>("NameLabel");
            BuildClassLabel ??= GetNodeOrNull<Label>("BuildClassLabel");
            DescLabel ??= GetNodeOrNull<Label>("DescLabel");
            ProgressLabel ??= GetNodeOrNull<Label>("ProgressLabel");
        }
    }
}
