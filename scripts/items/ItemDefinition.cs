using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Effects;
using Kuros.Items.Attributes;
using Kuros.Items.Durability;
using Kuros.Items.Effects;
using Kuros.Items.Weapons;

namespace Kuros.Items
{
    /// <summary>
    /// 基础物品定义资源，用于描述可被背包、装备栏等系统引用的物品模板。
    /// </summary>
    [GlobalClass]
    public partial class ItemDefinition : Resource
    {
        [ExportGroup("Identity")]
        [Export] public string ItemId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "Unnamed Item";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("Presentation")]
        [Export] public Texture2D? Icon { get; set; }
        [Export(PropertyHint.File, "*.tscn")] public string HoldScenePath { get; set; } = string.Empty;
        [Export] public string Category { get; set; } = "General";
        [Export] public Godot.Collections.Array<string> Tags
        {
            get => _tags;
            set
            {
                _tags = value ?? new();
                _tagCache = null;
            }
        }

        [ExportGroup("Stacking")]
        [Export(PropertyHint.Range, "1,9999,1")] public int MaxStackSize { get; set; } = 99;

        [ExportGroup("Attributes")]
        [Export] public Godot.Collections.Array<ItemAttributeEntry> AttributeEntries
        {
            get => _attributeEntries;
            set
            {
                _attributeEntries = value ?? new();
                _attributeCache = null;
            }
        }

        [ExportGroup("Effects")]
        [Export] public Godot.Collections.Array<ItemEffectEntry> EffectEntries
        {
            get => _effectEntries;
            set => _effectEntries = value ?? new();
        }

        [ExportGroup("Weapon")]
        [Export] public Godot.Collections.Array<Resource> WeaponSkillResources
        {
            get => _weaponSkillResources;
            set => _weaponSkillResources = value ?? new();
        }
        [Export] public string BuildClass { get; set; } = string.Empty;
        [Export] public bool IsThrowable { get; set; } = false; // 是否为投掷物，影响拾取该物体的外观，非投掷物背在身后，投掷物则直接举起
        [Export] public bool IsThrowWeapon { get; set; } = false; // 投掷后是否回收（冷却归还背包）。true=投掷武器（回收），false=一次性投掷物（落地销毁）
        [Export] public bool PreventDropDuringCooldown { get; set; } = false; // CD 期间禁止将该投掷武器从背包放置到地面

        /// <summary>物品本体以世界物形式存在（敌人掉落 / 玩家放置 place——背包删除生成世界物）
        /// 时的存活时长（秒，0=禁用）：静止 N 秒未被拾取 → 闪烁预警并消失。
        /// 与一般武器/道具一致；投掷（throw）生成的副本走自身特效自毁路径，不挂过期。</summary>
        [Export(PropertyHint.Range, "0,300,1")] public float UnpickedLifetime { get; set; } = 0f;

        [ExportGroup("Durability")]
        [Export] public ItemDurabilityConfig? DurabilityConfig { get; set; }

        [ExportGroup("Throw Physics")]
        /// <summary>投掷瞬间立即销毁武器本体并触发 OnThrowDestroy 效果（视觉由效果接管，不飞行）——用于回旋镖等轨迹由独立效果表现的道具。</summary>
        [Export] public bool SpawnEffectOnThrow { get; set; } = false;
        [Export(PropertyHint.Range, "-1000,1000,1")] public Vector2 ThrowStartOffset { get; set; } = new Vector2(0, -400); // 投掷时相对于玩家的偏移
        [Export(PropertyHint.Range, "0,10,0.01")] public double ThrowParabolicDuration { get; set; } = 0.35;    // 投掷物飞行的总时间（秒）
        [Export(PropertyHint.Range, "0,500,10")] public float ThrowParabolicPeakHeight { get; set; } = 10f;   // 投掷物飞行过程中达到的最高点相对于起始点的高度
        [Export(PropertyHint.Range, "0,2000,1")] public float ThrowHorizontalDistance { get; set; } = 500f;   // 投掷物在水平方向的飞行距离（像素），速度由此和ThrowParabolicDuration决定
        [Export(PropertyHint.Range, "-1000,1000,1")] public float ThrowParabolicLandingYOffset { get; set; } = 300f;    // 投掷物落地点相对于目标点的垂直偏移
        [Export(PropertyHint.Range, "0.1,60,0.1")] public float ThrowWeaponCooldown { get; set; } = 2.0f;              // 投掷武器冷却时间（秒）：仅对 IsThrowWeapon=true 的投掷武器生效
        [Export(PropertyHint.Range, "-10,10,1")] public int ThrowZIndex { get; set; } = 3;                              // 投掷物飞行途中的 z_index

        [ExportGroup("Throwable Tier")]
        /// <summary>一次性投掷道具档位(0=无档,1/2/3=小/中/大)。档位数值单真源 = <see cref="ThrowableTierTable"/>
        /// (CSV 列 ThrowTier);下方 Throw Physics 原始字段 >0 时作为逐项特化覆盖档位值。</summary>
        [Export(PropertyHint.Range, "0,3,1")] public int ThrowTier { get; set; } = 0;

        /// <summary>档位枚举视图。</summary>
        public ThrowableTier Tier => (ThrowableTier)ThrowTier;

        /// <summary>
        /// 是否为家具：可投掷且非投掷武器。自动推导，无需在 .tres 中手动设置。
        /// </summary>
        public bool IsFurniture => IsThrowable && !IsThrowWeapon;

        private Godot.Collections.Array<string> _tags = new();
        private HashSet<string>? _tagCache;
        private Godot.Collections.Array<ItemAttributeEntry> _attributeEntries = new();
        private Godot.Collections.Array<ItemEffectEntry> _effectEntries = new();
        private Godot.Collections.Array<Resource> _weaponSkillResources = new();
        private ItemAttributeSet? _attributeCache;

        private const string DefaultWorldSceneDirectory = "res://scenes/items/";

        [ExportGroup("World")]
        [Export(PropertyHint.File, "*.tscn")] public string WorldScenePath { get; set; } = string.Empty;

        public bool HasTag(string tag) => TagSet.Contains(tag);
        public bool HasAnyTag(IEnumerable<string> tagIds)
        {
            if (tagIds == null) return false;
            foreach (var id in tagIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (HasTag(id)) return true;
            }
            return false;
        }

        public IReadOnlyCollection<string> GetTags() => TagSet;

        private ItemAttributeSet AttributeSet => _attributeCache ??= new ItemAttributeSet(_attributeEntries);
        private HashSet<string> TagSet => _tagCache ??= BuildTagSet();

        public IEnumerable<ItemAttributeValue> GetAttributeValues()
        {
            return AttributeSet.Values;
        }

        public bool TryResolveAttribute(string attributeId, int quantity, out ResolvedItemAttribute attribute)
        {
            if (AttributeSet.TryGetValue(attributeId, out var attributeValue))
            {
                attribute = attributeValue.Resolve(quantity);
                return attribute.IsValid;
            }

            attribute = ResolvedItemAttribute.Empty;
            return false;
        }

        public Dictionary<string, float> GetAttributeSnapshot(int quantity = 1)
        {
            var result = new Dictionary<string, float>();
            foreach (var attributeValue in AttributeSet.Values)
            {
                var resolved = attributeValue.Resolve(quantity);
                if (resolved.IsValid)
                {
                    result[resolved.AttributeId] = resolved.Value;
                }
            }

            return result;
        }

        public IEnumerable<ItemEffectEntry> GetEffectEntries(ItemEffectTrigger trigger)
        {
            foreach (var entry in _effectEntries)
            {
                if (entry == null || entry.EffectScene == null) continue;
                if (entry.Trigger != trigger) continue;
                yield return entry;
            }
        }

        public void ApplyEffects(GameActor actor, ItemEffectTrigger trigger)
        {
            if (actor == null || actor.EffectController == null)
            {
                return;
            }

            foreach (var entry in GetEffectEntries(trigger))
            {
                var effect = entry.InstantiateEffect();
                if (effect == null) continue;

                // 家具持握减速注入：档位表为单真源（.tres PropertyOverrides 已废弃）——
                // 覆盖 effect 实例的倍率再应用；RemoveEffects 的临时实例不受影响（按 EffectId 移除）。
                // 无档（IsFurniture 但 ThrowTier=0）不注入，保留 effect 自身默认/覆写值。
                if (trigger == ItemEffectTrigger.OnEquip && IsFurniture
                    && effect is HeavyCarrySlowEffect slow
                    && GetResolvedTierSpec() is { } tierSpec)
                {
                    slow.SpeedMultiplierPerStack = tierSpec.CarrySlowMultiplier;
                }

                actor.ApplyEffect(effect);
            }
        }

        public void RemoveEffects(GameActor actor, ItemEffectTrigger trigger)
        {
            if (actor == null || actor.EffectController == null) return;

            foreach (var entry in GetEffectEntries(trigger))
            {
                var tempEffect = entry.InstantiateEffect();
                if (tempEffect == null) continue;
                actor.RemoveEffect(tempEffect.EffectId);
                tempEffect.Free();
            }
        }

        public IEnumerable<WeaponSkillDefinition> GetWeaponSkillDefinitions()
        {
            foreach (var skillResource in _weaponSkillResources)
            {
                if (skillResource is WeaponSkillDefinition skill)
                {
                    yield return skill;
                }
            }
        }

        private HashSet<string> BuildTagSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in _tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                set.Add(tag.Trim());
            }

            return set;
        }

        /// <summary>家具且档位有效 → 定义档规格（无任何构筑修饰;持握减速注入专用）。</summary>
        public ThrowableTierSpec? GetResolvedTierSpec()
        {
            if (!IsFurniture || ThrowTier <= 0) return null;
            return ThrowableTierTable.TryGetSpec(Tier, out var spec) ? spec : null;
        }

        /// <summary>构筑修饰后的有效档(定义档 + 整体 shift + 参数专属 shift,clamp 至表上下限)。
        /// 无档(武器/非投掷)或非家具返回 None(修饰不影响无档物品)。</summary>
        public ThrowableTier EffectiveTier(ThrowableModifiers mods, int paramShift)
        {
            if (!IsFurniture || ThrowTier <= 0) return ThrowableTier.None;
            int effective = ThrowableTierTable.ClampTier(ThrowTier + mods.TierShift + paramShift);
            return (ThrowableTier)effective;
        }

        /// <summary>修饰后档位规格(供实体按参数取 HP/击退等;无档 → null)。</summary>
        public ThrowableTierSpec? GetResolvedTierSpec(ThrowableModifiers mods, int paramShift)
            => ThrowableTierTable.TryGetSpec(EffectiveTier(mods, paramShift), out var spec) ? spec : null;

        /// <summary>投掷飞行时长：原始字段 &gt;0 覆盖档位(特化属原本属性,不受升档影响)；均无 → 内置 0.6。
        /// 时长跟随整体升档(TierShift),无独立/倍率修饰。</summary>
        public double GetEffectiveThrowDuration(ThrowableModifiers mods = default)
            => ThrowParabolicDuration > 0 ? ThrowParabolicDuration
                : GetResolvedTierSpec(mods, 0)?.ThrowDuration ?? 0.6;

        /// <summary>投掷水平距离：原始字段 &gt;0 覆盖档位;否则按修饰档取值;增幅(覆盖小型/倍率)作用于其上。</summary>
        public float GetEffectiveThrowDistance(ThrowableModifiers mods = default)
        {
            float baseDistance = ThrowHorizontalDistance > 0 ? ThrowHorizontalDistance
                : GetResolvedTierSpec(mods, 0)?.ThrowDistance ?? 600f;

            if (mods.DistanceAsSmall)
                baseDistance = ThrowableTierTable.TryGetSpec(ThrowableTier.Small, out var smallSpec)
                    ? smallSpec.ThrowDistance
                    : baseDistance;

            float scale = mods.DistanceScale > 0f ? mods.DistanceScale : 1f;
            return baseDistance * scale;
        }

        /// <summary>撞击伤害：实体已解析的 attack_power &gt;0 优先(逐项特化) → 修饰档伤害 → 调用方场景兜底。
        /// 伤害升档 = AttackTierShift + 整体 TierShift。</summary>
        public float ResolveThrowImpactDamage(float attributeDamage, float sceneFallbackDamage,
            ThrowableModifiers mods = default)
            => attributeDamage > 0f ? attributeDamage
                : GetResolvedTierSpec(mods, mods.AttackTierShift)?.AttackPower ?? sceneFallbackDamage;

        public string ResolveWorldScenePath()
        {
            if (!string.IsNullOrWhiteSpace(WorldScenePath))
            {
                return WorldScenePath;
            }

            if (string.IsNullOrWhiteSpace(ItemId))
            {
                return string.Empty;
            }

            return $"{DefaultWorldSceneDirectory}{ItemId}.tscn";
        }
    }
}

