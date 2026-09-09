using System.Collections.Generic;
using Godot;
using Kuros.Core;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 全局共享"移动速度降低"管理:同一目标可被多个来源叠加减速,
    /// 取**最小乘数**生效;最后一个来源移除时还原原始 Speed。
    /// 由 SlowHitAreaEffect(区域减速)与 BuildThrow_A_003(连线减速)共用,
    /// 避免各自快照原始速度导致互相覆盖。
    /// </summary>
    public static class SharedSpeedSlowManager
    {
        private static readonly Dictionary<GameActor, List<float>> ActiveMultipliers = new();
        private static readonly Dictionary<GameActor, float> OriginalSpeeds = new();

        /// <summary>目标当前是否处于本系统减速中。</summary>
        public static bool IsSlowed(GameActor actor) => actor != null && OriginalSpeeds.ContainsKey(actor);

        /// <summary>叠加一个减速乘数(≤1)。首次叠加时快照原始速度。</summary>
        public static void Apply(GameActor actor, float multiplier)
        {
            if (actor == null || multiplier >= 1f) return;

            if (!OriginalSpeeds.ContainsKey(actor))
            {
                OriginalSpeeds[actor] = actor.Speed;
                ActiveMultipliers[actor] = new List<float>();
            }

            ActiveMultipliers[actor].Add(multiplier);
            Recalculate(actor);
        }

        /// <summary>移除一个减速乘数;无剩余来源时还原原始速度并清理记录。</summary>
        public static void Remove(GameActor actor, float multiplier)
        {
            if (actor == null) return;
            if (!ActiveMultipliers.TryGetValue(actor, out var list)) return;

            list.Remove(multiplier);

            if (list.Count == 0)
            {
                if (OriginalSpeeds.TryGetValue(actor, out float original))
                {
                    if (GodotObject.IsInstanceValid(actor) && !actor.IsDeadOrDying)
                        actor.Speed = original;
                    OriginalSpeeds.Remove(actor);
                }
                ActiveMultipliers.Remove(actor);
            }
            else
            {
                Recalculate(actor);
            }
        }

        /// <summary>目标死亡/离树时的强制清理(还原速度,丢弃全部来源记录)。</summary>
        public static void Clear(GameActor actor)
        {
            if (actor == null) return;
            if (OriginalSpeeds.TryGetValue(actor, out float original))
            {
                if (GodotObject.IsInstanceValid(actor) && !actor.IsDeadOrDying)
                    actor.Speed = original;
                OriginalSpeeds.Remove(actor);
            }
            ActiveMultipliers.Remove(actor);
        }

        private static void Recalculate(GameActor actor)
        {
            if (!OriginalSpeeds.TryGetValue(actor, out float original)) return;
            if (!ActiveMultipliers.TryGetValue(actor, out var list)) return;

            float best = 1f;
            foreach (float m in list)
                best = Mathf.Min(best, m);

            float final = original * best;
            if (GodotObject.IsInstanceValid(actor) && !actor.IsDeadOrDying)
                actor.Speed = final;
        }
    }
}
