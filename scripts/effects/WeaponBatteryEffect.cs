using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Managers;
using Kuros.UI;

namespace Kuros.Effects
{
    /// <summary>
    /// 武器电量效果（ActorEffect，Duration=0 永久）：作为独立场景挂载到武器技能的 Effects 数组。
    /// 电量数值持久保存在 WeaponBatteryManager（以 SkillId 为 key），本效果实例只负责：
    /// 1. OnApply 注册武器电量 + 生成电池 bar 挂到玩家；
    /// 2. OnTick 更新 bar 数值/可见性 + 自检（技能已从当前武器技能集移除 → 自移除）；
    /// 3. OnRemoved 销毁 bar。
    /// 电量不随效果实例销毁——Passive 技能卸武器销毁效果、Active 技能每次攻击 Refresh，
    /// 切换武器后电量仍在管理器继续恢复（RegisterWeapon 幂等，不重置电量）。
    /// </summary>
    [GlobalClass]
    public partial class WeaponBatteryEffect : ActorEffect
    {
        [ExportCategory("Battery")]
        /// <summary>关联的武器技能 ID（WeaponBatteryManager 的 key，电量按此独立存储）。</summary>
        [Export] public string SkillId { get; set; } = "";
        /// <summary>最大电量。</summary>
        [Export(PropertyHint.Range, "1,1000,1")] public float MaxCharge { get; set; } = 100f;
        /// <summary>每次攻击消耗电量。</summary>
        [Export(PropertyHint.Range, "0,1000,0.5")] public float ConsumePerAttack { get; set; } = 10f;
        /// <summary>停止消耗后的恢复延迟（秒）。</summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float RecoveryDelaySeconds { get; set; } = 1f;
        /// <summary>恢复速度（电量/秒）。</summary>
        [Export(PropertyHint.Range, "0,1000,0.5")] public float RecoveryPerSecond { get; set; } = 20f;

        [ExportCategory("Bar")]
        /// <summary>电池条 UI 场景（CanvasLayer，跟随玩家屏幕位置）。</summary>
        [Export] public PackedScene? BatteryBarScene { get; set; }
        /// <summary>满电后 bar 保持显示的时长（秒）：恢复满后延迟隐藏；期间开始消耗（电量 < max）则保持显示。</summary>
        [Export(PropertyHint.Range, "0,10,0.1")] public float FullHideDelaySeconds { get; set; } = 1f;

        private WeaponBatteryBar? _bar;
        private PlayerWeaponSkillController? _skillController;
        private float _fullElapsed = -1f;   // 满电持续时间（-1 = 未满电）

        protected override void OnApply()
        {
            WeaponBatteryManager.Instance.RegisterWeapon(
                SkillId, MaxCharge, ConsumePerAttack, RecoveryDelaySeconds, RecoveryPerSecond);

            _skillController = Actor?.GetNodeOrNull<PlayerWeaponSkillController>("WeaponSkillController");

            if (BatteryBarScene != null && Actor != null)
            {
                var bar = BatteryBarScene.Instantiate<WeaponBatteryBar>();
                if (bar != null)
                {
                    Actor.AddChild(bar);
                    _bar = bar;
                }
            }
        }

        protected override void OnTick(double delta)
        {
            // 自检：武器切换后当前技能集不再包含本技能 → 自移除（bar 随 OnRemoved 销毁）
            if (_skillController == null || !IsInstanceValid(_skillController)
                || !_skillController.HasSkill(SkillId))
            {
                Controller?.RemoveEffect(this);
                return;
            }

            if (_bar == null || !IsInstanceValid(_bar))
            {
                return;
            }

            float current = WeaponBatteryManager.Instance.GetCharge(SkillId);
            float max = WeaponBatteryManager.Instance.GetMaxCharge(SkillId);
            bool isFull = current >= max;

            // 满电显示延迟：充满后继续显示 FullHideDelaySeconds 秒再隐藏；
            // 电量一旦低于 max（开始消耗）立即重新显示并重置计时
            if (isFull)
            {
                if (_fullElapsed < 0f) _fullElapsed = 0f;
                _fullElapsed += (float)delta;
            }
            else
            {
                _fullElapsed = -1f;
            }

            // 多技能武器同装时只让"当前 primary 技能"的电量条可见，防止重叠
            bool isPrimary = _skillController.GetPrimarySkillDefinition()?.SkillId == SkillId;
            bool showByBattery = !isFull || _fullElapsed < FullHideDelaySeconds;
            _bar.Visible = isPrimary && showByBattery;
            _bar.SetCharge(current, max);
        }

        public override void OnRemoved()
        {
            if (_bar != null && IsInstanceValid(_bar))
            {
                _bar.QueueFree();
            }
            _bar = null;
            base.OnRemoved();
        }
    }
}
