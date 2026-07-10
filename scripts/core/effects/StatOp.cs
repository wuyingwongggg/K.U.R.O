using System;
using System.Collections.Generic;
using Godot;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 属性操作委托注册表。
    ///
    /// 目的：消除 BuildStatBonusEffect 中的硬编码 switch/if-else。
    /// 新增属性只需在 Registry 中添加一行 Entry，无需修改 BuildStatBonusEffect。
    ///
    /// 使用方式：
    ///   StatOp.Registry["attack_damage"].Apply(actor, 10f);   // 攻击力 +10
    ///   StatOp.Registry["attack_damage"].Revert(actor, 5f);   // 还原攻击力到 5
    ///   float orig = StatOp.Registry["attack_damage"].GetOriginal(actor); // 获取原始值
    ///
    /// 工作流：
    ///   1. BuildStatBonusEffect.OnApply  → 遍历 Registry，对每个 key 调用 GetOriginal 保存原始值
    ///   2. BuildStatBonusEffect 应用增量 → 调用 Apply(actor, delta)
    ///   3. BuildStatBonusEffect.OnRemoved → 调用 Revert(actor, original) 还原
    /// </summary>
    internal static class StatOp
    {
        /// <summary>对属性施加增量。actor 是目标角色，value 是增量值。</summary>
        public delegate void ApplyDelegate(GameActor actor, float value);

        /// <summary>将属性还原到原始值。actor 是目标角色，value 是原始值。</summary>
        public delegate void RevertDelegate(GameActor actor, float value);

        /// <summary>获取属性的当前值作为原始值保存。</summary>
        public delegate float GetOriginalDelegate(GameActor actor);

        /// <summary>
        /// 单个属性的三个操作绑定。
        /// Apply：施加增量
        /// Revert：还原原始值
        /// GetOriginal：保存原始值
        /// </summary>
        internal class Entry
        {
            public ApplyDelegate Apply = null!;
            public RevertDelegate Revert = null!;
            public GetOriginalDelegate GetOriginal = null!;
        }

        /// <summary>
        /// 属性名 → 操作委托的映射表。
        /// key 与 GameSaveData / 构筑效果数据中的属性名对应。
        ///
        /// 新增属性示例：假设要支持 "crit_rate"（暴击率），只需添加：
        ///   ["crit_rate"] = new()
        ///   {
        ///       Apply = (a, v) => a.CritRate += v,
        ///       Revert = (a, v) => a.CritRate = v,
        ///       GetOriginal = a => a.CritRate,
        ///   },
        ///
        /// 注意：max_health 的 Apply 在改 MaxHealth 后同时恢复等量 CurrentHealth，
        /// 避免最大血量增加时当前血量占比下降。
        /// </summary>
        public static readonly Dictionary<string, Entry> Registry = new()
        {
            ["attack_damage"] = new()
            {
                Apply = (a, v) => a.AttackDamage += v,
                Revert = (a, v) => a.AttackDamage = v,
                GetOriginal = a => a.AttackDamage,
            },
            ["speed"] = new()
            {
                Apply = (a, v) => a.Speed += v,
                Revert = (a, v) => a.Speed = v,
                GetOriginal = a => a.Speed,
            },
            ["max_health"] = new()
            {
                // 增加最大血量时，同时增加等量当前血量，保持血量占比不变
                Apply = (a, v) => { int r = Mathf.RoundToInt(v); a.MaxHealth += r; a.RestoreHealth(a.CurrentHealth + r); },
                Revert = (a, v) => a.MaxHealth = Mathf.RoundToInt(v),
                GetOriginal = a => a.MaxHealth,
            },
        };
    }
}
