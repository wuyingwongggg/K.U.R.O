using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyLockdownAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new();

        private Area2D? _detectionArea;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            if (!DetectionAreaPath.IsEmpty)
                _detectionArea = GetNodeOrNull<Area2D>(DetectionAreaPath)
                    ?? Enemy?.GetNodeOrNull<Area2D>(DetectionAreaPath);
        }

        public override bool IsPlayerInDetectionRange()
        {
            if (_detectionArea == null) return true;
            return _detectionArea.OverlapsBody(Player);
        }
    }
}
