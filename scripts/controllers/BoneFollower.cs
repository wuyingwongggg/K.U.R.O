using Godot;

namespace Kuros.Controllers
{
    /// <summary>
    /// 读取 SpineBoneNode 的 XY 位移并应用到本节点。
    /// 节点保持在原父节点下，不跟随骨骼旋转。
    /// </summary>
    public partial class BoneFollower : Node2D
    {
        [Export] public NodePath? TargetBone { get; set; }

        private Node2D? _boneNode;
        private Node2D? _spineSprite;
        private Vector2 _initialBonePos;
        private Vector2 _defaultPos;
        private bool _baselineCaptured;

        public override void _Ready()
        {
            _defaultPos = Position;

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
                _initialBonePos = _boneNode.Position;
                _baselineCaptured = true;
                return;
            }

            float scaleX = _spineSprite?.Scale.X ?? 1f;
            var deltaVec = _boneNode.Position - _initialBonePos;
            deltaVec.X *= scaleX;
            Position = _defaultPos + deltaVec;
        }
    }
}
