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
		[Export] public bool FollowBoneX { get; set; } = true;
		[Export] public bool FollowBoneY { get; set; } = false;

		private Node2D? _boneNode;
		private Node2D? _spineSprite;
		private Vector2 _initialBonePos;
		private Vector2 _defaultShadowPos;
		private bool _baselineCaptured;

		public override void _Ready()
		{
			_defaultShadowPos = Position;

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

			Vector2 boneDelta = (_boneNode.Position - _initialBonePos) * (_spineSprite?.Scale ?? Vector2.One);
			float x = FollowBoneX ? _defaultShadowPos.X + boneDelta.X : _defaultShadowPos.X;
			float y = FollowBoneY ? _defaultShadowPos.Y + boneDelta.Y : _defaultShadowPos.Y;
			Position = new Vector2(x, y);
		}
	}
}
