using Godot;
using Kuros.Systems.FSM;

namespace Kuros.Controllers
{
    public partial class EnableShapeOnFrozen : Node
    {
        [Export] public NodePath? StateMachinePath;
        [Export] public NodePath? TargetShapePath;

        private StateMachine? _stateMachine;
        private CollisionShape2D? _target;

        public override void _Ready()
        {
            if (StateMachinePath != null && !StateMachinePath.IsEmpty)
                _stateMachine = GetNodeOrNull<StateMachine>(StateMachinePath);
            else
                _stateMachine = GetParent()?.GetNodeOrNull<StateMachine>("StateMachine");

            if (TargetShapePath != null && !TargetShapePath.IsEmpty)
                _target = GetNodeOrNull<CollisionShape2D>(TargetShapePath);
        }

        public override void _Process(double delta)
        {
            if (_stateMachine == null || _target == null) return;
            bool isFrozen = _stateMachine.CurrentState?.Name == "Frozen";
            _target.Disabled = !isFrozen;
        }
    }
}
