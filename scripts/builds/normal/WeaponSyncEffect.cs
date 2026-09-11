using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Items;
using Kuros.Items.Weapons;
using Kuros.Systems.Inventory;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 武器同步（BuildNormal_C_006）：玩家造成伤害时,若同步冷却就绪,随机从快捷栏抽一把
    /// 「其他武器」（非当前选中）,把该武器 WeaponSkillDefinition 的技能 Effects 挂到玩家身上,
    /// 持续到本次同步冷却结束(到点移除)——冷却期内玩家后续攻击自然带该效果。
    ///
    /// 实例管理关键:同步效果一律用**稳定 EffectId** = "weapon_sync_{技能SkillId}_{条目序}"——
    /// 同一技能被重复抽到时走 EffectController 同 id 刷新,不会产生新实例(修复此前
    /// Guid 每实例化一次导致去重失效、实例只增不减的问题);窗口结束按同一 key 精确移除,
    /// 与玩家正常技能效果(原 EffectId)互不污染。
    /// </summary>
    [GlobalClass]
    public partial class WeaponSyncEffect : ActorEffect
    {
        /// <summary>内置同步冷却(秒):触发频率节流——攻速快/慢武器命中触发频率一致,平衡由冷却承担而非概率。</summary>
        [Export(PropertyHint.Range, "0.2,10,0.1")] public float SyncCooldownSeconds = 1f;

        private const string SyncKeyPrefix = "weapon_sync_";

        private MainCharacter? _player;
        private float _cooldownRemaining;
        private readonly List<string> _activeSyncKeys = new();
        private readonly System.Random _rng = new();

        protected override void OnApply()
        {
            _player = Actor as MainCharacter;
            _cooldownRemaining = 0f;
            DamageEventBus.SubscribeWithSource(OnDamageResolved);
        }

        public override void OnRemoved()
        {
            DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
            RemoveAllSyncedEffects();
            _player = null;
            base.OnRemoved();
        }

        protected override void OnTick(double delta)
        {
            if (_cooldownRemaining <= 0f) return;

            _cooldownRemaining -= (float)delta;
            if (_cooldownRemaining <= 0f)
            {
                _cooldownRemaining = 0f;
                RemoveAllSyncedEffects(); // 窗口结束:精确移除本效果挂载的同步实例
            }
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (Actor == null || attacker != Actor || target == Actor) return;
            if (damage <= 0 || _cooldownRemaining > 0f) return;

            if (TryPickOtherWeaponSkill(out WeaponSkillDefinition skill))
            {
                _cooldownRemaining = SyncCooldownSeconds;
                ApplySyncedEffects(skill);
            }
        }

        /// <summary>从快捷栏随机挑一把非当前武器、带技能的武器,返回其首个技能。无可挑则 false。</summary>
        private bool TryPickOtherWeaponSkill(out WeaponSkillDefinition skill)
        {
            skill = null;
            if (_player?.InventoryComponent?.QuickBar == null) return false;

            var inventory = _player.InventoryComponent;
            var current = inventory.GetSelectedQuickBarStack();
            string? currentItemId = current?.Item?.ItemId;

            var candidates = new List<ItemDefinition>();
            foreach (var stack in inventory.QuickBar.Slots)
            {
                if (stack == null || stack.IsEmpty) continue;
                if (stack.Item.ItemId == "empty_item") continue;
                if (stack.Item.ItemId == currentItemId) continue; // 非当前武器
                if (stack.Item.GetWeaponSkillDefinitions().Any()) candidates.Add(stack.Item);
            }

            if (candidates.Count == 0) return false;
            var picked = candidates[_rng.Next(candidates.Count)];
            foreach (var s in picked.GetWeaponSkillDefinitions())
            {
                skill = s;
                return true;
            }
            return false;
        }

        private void ApplySyncedEffects(WeaponSkillDefinition skill)
        {
            if (_player == null || !GodotObject.IsInstanceValid(_player)) return;

            int entryIndex = 0;
            foreach (var entry in skill.Effects)
            {
                if (entry == null || entry.EffectScene == null) continue;

                string key = $"{SyncKeyPrefix}{skill.SkillId}_{entryIndex}";
                entryIndex++;

                // 稳定 key 去重:同一技能条目已在窗口内 → 刷新既有实例即可,不新增
                if (_player.EffectController.GetEffect(key) != null)
                    continue;

                var effect = entry.InstantiateEffect();
                if (effect == null) continue;

                effect.EffectId = key; // 覆盖为稳定 key,与玩家正常效果隔离、移除可精确配对
                _player.ApplyEffect(effect);
                _activeSyncKeys.Add(key);
            }
        }

        private void RemoveAllSyncedEffects()
        {
            if (_player == null || !GodotObject.IsInstanceValid(_player)) return;
            foreach (string key in _activeSyncKeys)
                _player.RemoveEffect(key);
            _activeSyncKeys.Clear();
        }
    }
}
