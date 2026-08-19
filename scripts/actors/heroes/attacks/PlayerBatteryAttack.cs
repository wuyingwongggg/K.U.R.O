using Godot;
using Kuros.Managers;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 电量武器攻击模板（继承 PlayerBasicMeleeAttack）：
    /// 每次攻击前检查 WeaponBatteryManager 中绑定的武器技能电量是否足够，
    /// 攻击成功开始时消耗一次电量。电量不足时阻止攻击并设置 0.2s AttackTimer 节流，
    /// 防止长按期间每帧进入 Attack 状态又失败退回 Idle 的抖动。
    /// 电量语义完全留在本类与电池系统内，基类 PlayerAttackTemplate 不感知电量。
    /// </summary>
    public partial class PlayerBatteryAttack : PlayerBasicMeleeAttack
    {
        /// <summary>绑定的武器技能 ID（WeaponBatteryManager 的 key，与 WeaponBatteryEffect.SkillId 对应）。</summary>
        [Export] public string BatterySkillId { get; set; } = "";

        /// <summary>电量不足时的重试节流（秒）。</summary>
        private const float BatteryEmptyRetryInterval = 0.2f;

        protected override bool MeetsCustomConditions()
        {
            if (!base.MeetsCustomConditions())
            {
                return false;
            }

            if (WeaponBatteryManager.Instance.CanAfford(BatterySkillId))
            {
                return true;
            }

            Player.AttackTimer = Mathf.Max(Player.AttackTimer, BatteryEmptyRetryInterval);
            return false;
        }

        /// <summary>电量不足时不打断后摇：连击重启被拒，当前攻击的 Recovery 动画自然播放完毕。</summary>
        protected override bool CanCancelRecoveryForRestart()
        {
            return WeaponBatteryManager.Instance.CanAfford(BatterySkillId);
        }

        protected override void OnAttackStarted()
        {
            // 先走基类（其中 TriggerDefaultSkill 会施加/刷新电池效果，保证已注册），再扣一次电量
            base.OnAttackStarted();
            WeaponBatteryManager.Instance.TryConsume(BatterySkillId);
        }
    }
}
