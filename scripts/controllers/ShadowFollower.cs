using Godot;

namespace Kuros.Controllers
{
    /// <summary>
    /// 读取 SpineBoneNode 的 X 轴位移并应用到 Shadow。
    /// Shadow 保持在根节点下，不跟随骨骼的 Y 偏移和旋转。
    /// </summary>
    public partial class ShadowFollower : Sprite2D
    {
        [Export] public NodePath? TargetBone { get; set; }

        private Node2D? _boneNode;
        private Node2D? _spineSprite;
        private float _initialBoneX;
        private float _defaultShadowX;
        private bool _baselineCaptured;

        public override void _Ready()
        {
            _defaultShadowX = Position.X;

            if (TargetBone != null && !TargetBone.IsEmpty)
            {
                _boneNode = GetNodeOrNull<Node2D>(TargetBone);
                _spineSprite = _boneNode?.GetParentOrNull<Node2D>();
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_boneNode == null || !GodotObject.IsInstanceValid(_boneNode))
                return;

            if (!_baselineCaptured)
            {
                _initialBoneX = _boneNode.Position.X;
                _baselineCaptured = true;
                return;
            }

            float spineScaleX = _spineSprite?.Scale.X ?? 1f;
            float deltaX = (_boneNode.Position.X - _initialBoneX) * spineScaleX;
            Position = new Vector2(_defaultShadowX + deltaX, Position.Y);
        }
    }
}
