using Godot;

namespace Kuros.Fx
{
    /// <summary>
    /// 需要跟随"产生它的锚点节点"的特效（攻击预警等）：生成方（如 EnemyAttackTemplate.SpawnSingleEffect）
    /// 生成时注入实际使用的生成锚点（marker 或敌人根），特效每帧跟随该节点。
    /// 生成方只依赖此通用接口，不感知具体特效类型。
    /// </summary>
    public interface IFollowAnchor
    {
        Node2D? FollowAnchor { get; set; }
    }
}
