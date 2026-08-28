using Godot;
using Kuros.Items.Effects;

namespace Kuros.Items.Weapons
{
    /// <summary>
    /// 武器技能定义，可配置主动/被动效果、动画、数值等。
    /// </summary>
    [GlobalClass]
    public partial class WeaponSkillDefinition : Resource
    {
        [Export] public string SkillId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "Weapon Skill";
        [Export] public WeaponSkillType SkillType { get; set; } = WeaponSkillType.Passive;
        [Export] public string AnimationName { get; set; } = string.Empty;
        [Export(PropertyHint.Range, "0,5,0.1")] public float DamageMultiplier { get; set; } = 1f;
        /// <summary>冲刺攻击伤害倍率。-1 = 用普通 DamageMultiplier。</summary>
        [Export(PropertyHint.Range, "-1,5,0.1")] public float DashDamageMultiplier { get; set; } = -1f;
        [Export(PropertyHint.Range, "0,30,0.1")] public float CooldownSeconds { get; set; } = 0.5f;
        [ExportGroup("Hitbox Debug")]
        [Export] public bool ShowHitboxDebug { get; set; } = true;
        [Export] public Color HitboxDebugColor { get; set; } = new Color(1f, 0.28f, 0.18f, 0.95f);
        [Export(PropertyHint.Range, "0.05,5,0.05")] public float HitboxDebugDuration { get; set; } = 0.6f;
        [Export(PropertyHint.Range, "1,12,0.5")] public float HitboxDebugLineWidth { get; set; } = 3f;
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;
        [Export] public Godot.Collections.Array<ItemEffectEntry> Effects { get; set; } = new();
        [Export] public Godot.Collections.Array<string> StateWhitelist { get; set; } = new();
        [Export] public bool UseDefaultAttackAnimationFallback { get; set; } = true;
        [Export] public string ActivationAction { get; set; } = string.Empty;
        [Export] public bool AllowHoldContinuousAttack { get; set; } = true;

        [ExportGroup("Attack Timing Override")]
        [Export(PropertyHint.MultilineText)] public string AttackTimingNote { get; set; } = "负数 = 使用攻击模板的默认值";
        [Export(PropertyHint.Range, "-1,5,0.01")] public float WarmupDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float ActiveDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float RecoveryDuration { get; set; } = -1f;

        [Export(PropertyHint.Range, "0.1,3,0.1")]
        public float WarmupAnimationSpeed { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0.1,3,0.1")]
        public float ActiveAnimationSpeed { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0.1,3,0.1")]
        public float RecoveryAnimationSpeed { get; set; } = 1.0f;

        [ExportGroup("Dash Attack Override")]
        [Export(PropertyHint.MultilineText)] public string DashAttackTimingNote { get; set; } = "冲刺攻击（EnableDashMovement）专属配置；负数/空 = 使用普通攻击配置";
        /// <summary>冲刺攻击动画名（Spine）。空 = 用普通攻击动画（AnimationName）。</summary>
        [Export] public string DashAnimationName { get; set; } = string.Empty;
        /// <summary>冲刺动画阶段时长（秒）。-1 = 用普通阶段（WarmupDuration 或模板默认）。</summary>
        [Export(PropertyHint.Range, "-1,5,0.01")] public float DashWarmupDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float DashActiveDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float DashRecoveryDuration { get; set; } = -1f;
        /// <summary>冲刺动画各阶段播放速度。-1 = 用普通动画速度。</summary>
        [Export(PropertyHint.Range, "-1,3,0.1")] public float DashWarmupAnimationSpeed { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,3,0.1")] public float DashActiveAnimationSpeed { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,3,0.1")] public float DashRecoveryAnimationSpeed { get; set; } = -1f;

        public bool IsUsableInState(string stateName)
        {
            if (StateWhitelist.Count == 0) return true;
            return StateWhitelist.Contains(stateName);
        }
    }
}

