using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Kuros.Systems.AI
{
    public sealed class QuickBarSlotState
    {
        public int SlotIndex { get; init; }
        public bool IsSelected { get; init; }
        public bool IsOccupied { get; init; }
        public string ItemId { get; init; } = string.Empty;
        public string ItemName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        /// <summary>物品类别（如 Weapon/General），供 AI 区分武器与非武器。</summary>
        public string Category { get; init; } = string.Empty;
        /// <summary>物品描述（截断到 AI 可读长度），供 AI 理解物品实际作用。</summary>
        public string Description { get; init; } = string.Empty;
        /// <summary>武器攻击力属性值（非武器为 0）。</summary>
        public int AttackPower { get; init; }
        /// <summary>武器技能名（首个武器技能的 DisplayName，无技能为空）。</summary>
        public string SkillName { get; init; } = string.Empty;
        /// <summary>武器技能描述（无技能为空）。</summary>
        public string SkillDescription { get; init; } = string.Empty;
        /// <summary>是否投掷武器。</summary>
        public bool IsThrowWeapon { get; init; }
        /// <summary>投掷武器剩余冷却（秒；0 = 不在冷却）。</summary>
        public float ThrowCooldownRemaining { get; init; }
        /// <summary>电池类武器当前电量（-1 = 无电池系统）。</summary>
        public float BatteryCharge { get; init; } = -1f;
        /// <summary>电池类武器最大电量（-1 = 无电池系统）。</summary>
        public float BatteryMax { get; init; } = -1f;

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["slot_index"] = SlotIndex,
                ["is_selected"] = IsSelected,
                ["is_occupied"] = IsOccupied,
                ["item_id"] = ItemId,
                ["item_name"] = ItemName,
                ["quantity"] = Quantity,
                ["category"] = Category,
                ["description"] = Description,
                ["attack_power"] = AttackPower,
                ["skill_name"] = SkillName,
                ["skill_description"] = SkillDescription,
                ["is_throw_weapon"] = IsThrowWeapon,
                ["throw_cooldown_remaining"] = ThrowCooldownRemaining,
                ["battery_charge"] = BatteryCharge,
                ["battery_max"] = BatteryMax
            };
        }
    }

    /// <summary>场上敌人摘要（AI 可读：类型/描述/血量/距离）。</summary>
    public sealed class EnemyState
    {
        public string Name { get; init; } = string.Empty;
        public string TypeName { get; init; } = string.Empty;
        public string AiDescription { get; init; } = string.Empty;
        public int CurrentHp { get; init; }
        public int MaxHp { get; init; }
        public float Distance { get; init; }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["name"] = Name,
                ["type"] = TypeName,
                ["description"] = AiDescription,
                ["current_hp"] = CurrentHp,
                ["max_hp"] = MaxHp,
                ["distance"] = Distance
            };
        }
    }

    /// <summary>背包物品摘要（AI 可读的语义信息，替代纯计数）。</summary>
    public sealed class BackpackSlotState
    {
        public string ItemId { get; init; } = string.Empty;
        public string ItemName { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public int Quantity { get; init; }
        /// <summary>是否武器（IsThrowWeapon 或 Category=="Weapon"）。</summary>
        public bool IsWeapon { get; init; }
        /// <summary>投掷武器剩余冷却（秒；0 = 不在冷却）。</summary>
        public float ThrowCooldownRemaining { get; init; }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["item_id"] = ItemId,
                ["item_name"] = ItemName,
                ["category"] = Category,
                ["quantity"] = Quantity,
                ["is_weapon"] = IsWeapon,
                ["throw_cooldown_remaining"] = ThrowCooldownRemaining
            };
        }
    }

    public sealed class CompanionState
    {
        public string Name { get; init; } = string.Empty;
        public int CurrentHp { get; init; }
        public int MaxHp { get; init; }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["name"] = Name,
                ["current_hp"] = CurrentHp,
                ["max_hp"] = MaxHp
            };
        }
    }

    /// <summary>
    /// Runtime game-state abstraction for AI decision making.
    /// </summary>
    public sealed class GameState
    {
        public ulong TimestampMs { get; init; }

        public int PlayerHp { get; init; }
        public int PlayerMaxHp { get; init; }
        public bool PlayerUnderAttack { get; init; }
        public string PlayerStateName { get; init; } = string.Empty;

        public int AliveEnemyCount { get; init; }
        public float NearestEnemyDistance { get; init; }
        public float AverageEnemyDistance { get; init; }
        /// <summary>最近敌人距离变化率（px/秒；正=双方接近，负=拉开；首次采集为 0）。</summary>
        public float NearestEnemyDistanceDelta { get; init; }
        /// <summary>接近方向判定：enemy_approaching / player_approaching / mutual / receding / static / none。</summary>
        public string ApproachSituation { get; init; } = "none";
        /// <summary>场上敌人摘要列表（含 AI 描述与血量）。</summary>
        public List<EnemyState> Enemies { get; init; } = new();
        /// <summary>当前关卡名（BattleSceneManager.LevelName 优先，回退场景名）。</summary>
        public string LevelName { get; init; } = string.Empty;
        /// <summary>AI 可读关卡描述（BattleSceneManager.AiLevelDescription，未配置为空）。</summary>
        public string LevelDescription { get; init; } = string.Empty;

        public int BackpackItemCount { get; init; }
        public int BackpackOccupiedSlots { get; init; }
        public List<BackpackSlotState> BackpackSlots { get; init; } = new();
        /// <summary>飞行中的投掷武器数量（ReservedQuickBarSlots 预占槽位数）。</summary>
        public int FlyingThrowWeaponCount { get; init; }
        public int QuickBarSlotCount { get; init; }
        public int QuickBarOccupiedSlots { get; init; }
        public int SelectedQuickBarSlotIndex { get; init; } = -1;
        public string SelectedQuickBarItemId { get; init; } = string.Empty;
        public string SelectedQuickBarItemName { get; init; } = string.Empty;
        public List<QuickBarSlotState> QuickBarSlots { get; init; } = new();

        public List<CompanionState> Companions { get; init; } = new();

        public int CompanionCount => Companions.Count;

        /// <summary>本局最近会话记忆（最近 N 条，短时效事实：击杀/受击/拾取/波次，供 AI 文本引用）。</summary>
        public List<string> MemoryEvents { get; init; } = new();
        /// <summary>L0 持久记忆摘要（单行：通关次数/击败总数/获取武器总数/剧情标志数）。</summary>
        public string PersistentMemorySummary { get; init; } = string.Empty;

        public Godot.Collections.Dictionary<string, Variant> ToAiInputDictionary()
        {
            var companions = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
            var quickBarSlots = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
            var backpackSlots = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
            var enemies = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
            int companionTotalHp = 0;
            int companionTotalMaxHp = 0;

            foreach (var companion in Companions)
            {
                companions.Add(companion.ToDictionary());
                companionTotalHp += companion.CurrentHp;
                companionTotalMaxHp += companion.MaxHp;
            }

            foreach (var slot in QuickBarSlots)
            {
                quickBarSlots.Add(slot.ToDictionary());
            }

            foreach (var slot in BackpackSlots)
            {
                backpackSlots.Add(slot.ToDictionary());
            }

            foreach (var enemy in Enemies)
            {
                enemies.Add(enemy.ToDictionary());
            }

            var memoryEvents = new Godot.Collections.Array<string>();
            foreach (var entry in MemoryEvents)
            {
                memoryEvents.Add(entry);
            }

            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["timestamp_ms"] = TimestampMs,
                ["player"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["hp"] = PlayerHp,
                    ["max_hp"] = PlayerMaxHp,
                    ["under_attack"] = PlayerUnderAttack,
                    ["state"] = PlayerStateName
                },
                ["companions"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["count"] = CompanionCount,
                    ["total_hp"] = companionTotalHp,
                    ["total_max_hp"] = companionTotalMaxHp,
                    ["members"] = companions
                },
                ["level"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["name"] = LevelName,
                    ["description"] = LevelDescription
                },
                ["enemies"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["alive_count"] = AliveEnemyCount,
                    ["nearest_distance"] = NearestEnemyDistance,
                    ["nearest_distance_delta"] = NearestEnemyDistanceDelta,
                    ["approach_situation"] = ApproachSituation,
                    ["average_distance"] = AverageEnemyDistance,
                    ["members"] = enemies
                },
                ["inventory"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["backpack_item_count"] = BackpackItemCount,
                    ["backpack_occupied_slots"] = BackpackOccupiedSlots,
                    ["backpack_slots"] = backpackSlots,
                    ["flying_throw_weapon_count"] = FlyingThrowWeaponCount,
                    ["quickbar_slot_count"] = QuickBarSlotCount,
                    ["quickbar_occupied_slots"] = QuickBarOccupiedSlots,
                    ["selected_quickbar_slot_index"] = SelectedQuickBarSlotIndex,
                    ["selected_quickbar_item_id"] = SelectedQuickBarItemId,
                    ["selected_quickbar_item_name"] = SelectedQuickBarItemName,
                    ["quickbar_slots"] = quickBarSlots
                },
                ["memory"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["persistent_summary"] = PersistentMemorySummary,
                    ["session_events"] = memoryEvents
                }
            };
        }

        public string ToAiInputJson(bool pretty = true)
        {
            return Json.Stringify(ToAiInputDictionary(), pretty ? "  " : string.Empty);
        }

        public string ToAiPromptText()
        {
            return string.Join("\n", new[]
            {
                "[GameState]",
                $"player.hp={PlayerHp}/{PlayerMaxHp}",
                $"player.under_attack={PlayerUnderAttack}",
                $"player.state={PlayerStateName}",
                $"companions.count={CompanionCount}",
                $"level.name={LevelName}",
                $"level.description={LevelDescription}",
                $"enemies.alive_count={AliveEnemyCount}",
                $"enemies.nearest_distance={NearestEnemyDistance:F2}",
                $"enemies.nearest_distance_delta={NearestEnemyDistanceDelta:F1}",
                $"enemies.approach_situation={ApproachSituation}",
                $"enemies.average_distance={AverageEnemyDistance:F2}",
                $"enemies.members={string.Join("; ", Enemies.Select(e => $"{e.Name}({e.TypeName})[{e.CurrentHp}/{e.MaxHp}hp]@{e.Distance:0}px{(string.IsNullOrEmpty(e.AiDescription) ? "" : ":" + e.AiDescription)}"))}",
                $"inventory.backpack_item_count={BackpackItemCount}",
                $"inventory.backpack_occupied_slots={BackpackOccupiedSlots}",
                $"inventory.backpack_weapons={string.Join("; ", BackpackSlots.Where(s => s.IsWeapon).Select(s => $"{s.ItemName}({s.ItemId})x{s.Quantity}"))}",
                $"inventory.flying_throw_weapon_count={FlyingThrowWeaponCount}",
                $"inventory.quickbar_slot_count={QuickBarSlotCount}",
                $"inventory.quickbar_occupied_slots={QuickBarOccupiedSlots}",
                $"inventory.selected_quickbar_slot_index={SelectedQuickBarSlotIndex}",
                $"inventory.selected_quickbar_item_id={SelectedQuickBarItemId}",
                $"inventory.selected_quickbar_item_name={SelectedQuickBarItemName}",
                $"inventory.selected_weapon_attack_power={QuickBarSlots.Where(s => s.IsSelected && s.IsOccupied).Select(s => s.AttackPower).FirstOrDefault()}",
                $"inventory.selected_weapon_skill={QuickBarSlots.Where(s => s.IsSelected && s.IsOccupied).Select(s => string.IsNullOrEmpty(s.SkillName) ? "none" : $"{s.SkillName}: {s.SkillDescription}").FirstOrDefault() ?? "none"}",
                $"inventory.selected_weapon_throw_cd={QuickBarSlots.Where(s => s.IsSelected && s.IsOccupied).Select(s => s.ThrowCooldownRemaining).FirstOrDefault():0.0}s",
                $"inventory.selected_weapon_battery={QuickBarSlots.Where(s => s.IsSelected && s.IsOccupied).Select(s => s.BatteryMax < 0 ? "none" : $"{s.BatteryCharge:0}/{s.BatteryMax:0}").FirstOrDefault() ?? "none"}",
                $"memory.persistent_summary={PersistentMemorySummary}",
                $"memory.session_events={string.Join("; ", MemoryEvents)}",
                "output_format=json"
            });
        }
    }
}
