namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 滑槽机械臂的通用状态（Idle / Walk / MagnetRetreat 共用）：
    /// 移动与相位全部由根脚本 EnemyF1RogueAIMagnet 驱动，状态本身不做任何事——
    /// 它只为动画控制器/调试面板提供"当前处于哪个相位"的名字，并给以后加
    /// 专门行为（如磁吸、被击落）留出扩展位。
    /// 注意：不要改回 EnemyWalkState/EnemyIdleState——它们的"追玩家 + 进攻击范围就攻击"
    /// 语义会和相位循环打架。
    /// </summary>
    public partial class MagnetRailState : EnemyState
    {
    }
}
