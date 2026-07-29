using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
        /// <summary>
    /// 区域检测攻击：将攻击触发条件从默认的距离判定改为指定 Area2D 的 Body 重叠检测。
    /// 在场景中放置独立的 DetectionArea，玩家进入该区域才允许此攻击触发。
    /// </summary>
    public partial class EnemyAreaDetectionAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        /// <summary>触发攻击的检测区域路径。未设置则回退到默认距离判定。</summary>
        [Export] public NodePath DetectionAreaPath = new();

        private Area2D? _detectionArea;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            if (!DetectionAreaPath.IsEmpty)
                _detectionArea = GetNodeOrNull<Area2D>(DetectionAreaPath)
                    ?? Enemy?.GetNodeOrNull<Area2D>(DetectionAreaPath);
        }

        /// <summary>
        /// 覆写检测逻辑：用 DetectionArea 是否与玩家 body 重叠替代默认的距离判定。
        /// 未配置 DetectionArea 时返回 true（不限制触发范围）。
        /// </summary>
        public override bool IsPlayerInDetectionRange()
        {
            if (_detectionArea == null) return true;
            return _detectionArea.OverlapsBody(Player);
        }
    }
}