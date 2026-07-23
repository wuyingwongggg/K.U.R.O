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

        [Export(PropertyHint.Range, "0,0.5,0.01")]
        public float TiltStrength { get; set; } = 0.08f;

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
        private Vector2 _pivotCenter;

        public override void _Ready()
        {
            var hoverTarget = CardInner ?? this;
            hoverTarget.MouseEntered += () => _isHovered = true;
            hoverTarget.MouseExited += () => { _isHovered = false; ResetTilt(); };
            ResolveExports();
            _shaderMat = CardBg?.Material as ShaderMaterial;
        }

        public override void _Process(double delta)
        {
            if (!_isHovered || CardInner == null) return;

            var pos = CardInner.GetLocalMousePosition();
            var size = CardInner.Size;
            if (size.X <= 0 || size.Y <= 0) return;

            _pivotCenter = size / 2f;
            CardInner.PivotOffset = _pivotCenter;
            pos /= size;
            UpdateTilt(pos);
            UpdateShader(1f, pos);
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                EmitSignal(SignalName.Confirmed, CardIndex);
        }

        private void UpdateTilt(Vector2 uv)
        {
            if (CardInner == null) return;
            var centered = uv - new Vector2(0.5f, 0.5f);
            CardInner.Rotation = centered.X * TiltStrength;
            CardInner.Scale = new Vector2(
                1f - Mathf.Abs(centered.Y) * TiltStrength * 1.5f,
                1f + Mathf.Abs(centered.Y) * TiltStrength * 0.5f
            );
        }

        private void ResetTilt()
        {
            if (CardInner != null)
            {
                CardInner.Rotation = 0f;
                CardInner.Scale = Vector2.One;
            }
            UpdateShader(0f, Vector2.Zero);
        }

        private void UpdateShader(float hovering, Vector2 pos)
        {
            _shaderMat?.SetShaderParameter("hovering", hovering);
            _shaderMat?.SetShaderParameter("mouse_screen_pos", pos);
        }

        private void UpdateSelectionHighlight()
        {
            if (RarityGlow != null)
                RarityGlow.Visible = _isSelected;
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
