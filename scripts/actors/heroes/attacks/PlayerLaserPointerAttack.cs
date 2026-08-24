namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 激光笔专属攻击：长按进入 Active 阶段循环播放期间，按玩家移动方向实时切换
    /// 站立 / 前进(fwd) / 后退(bwd) 三种 Spine 动画。
    /// 方向移动机制已迁移到基类 PlayerAttackTemplate（EnableDirectionalMovement 开关）：
    /// 场景配置 ForwardAnimationName/BackwardAnimationName + EnableDirectionalMovement = true。
    /// 方向规则：Y 轴有移动 → x ≥ 0 → fwd、x < 0 → bwd；仅 X 轴移动 → 同向 fwd、反向 bwd、deadzone 内站立。
    /// </summary>
    public partial class PlayerLaserPointerAttack : PlayerBatteryAttack
    {
    }
}
