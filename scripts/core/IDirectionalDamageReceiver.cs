using Godot;

namespace Kuros.Core
{
    /// <summary>
    /// 目标侧方向性伤害接收：攻击方向不接收时返回 false——攻击方视为未命中（不结算伤害），
    /// bullet 类攻击特效直接穿过。由 DamageDispatcher.DealDamage 在结算前检查。
    /// 方向优先用攻击方向向量（velocity——命中点可能已深入目标内部，位置差会丢失来源侧信息）；
    /// 无方向向量时回退攻击来源点 origin 与目标位置的主方向差。
    /// </summary>
    public interface IDirectionalDamageReceiver
    {
        /// <summary>攻击方向向量（如子弹速度方向）：主分量判定左右/上下。</summary>
        bool AcceptsAttackFromDirection(Vector2 direction);

        /// <summary>攻击来源点 origin（世界坐标）：回退用主方向差判定。</summary>
        bool AcceptsAttackFrom(Vector2 origin);
    }
}
