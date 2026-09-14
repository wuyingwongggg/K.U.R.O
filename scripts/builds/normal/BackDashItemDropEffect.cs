using Godot;
using Kuros.Actors.Heroes;
using Kuros.Actors.Heroes.States;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 剧终谢幕（BuildNormal_B_006）：后撤闪避（无方向输入的 dash）开始的瞬间,
    /// 把快捷栏排序最靠前的一件可用投掷武器以**攻击性投掷**形态甩出——
    /// 等价一次普通投掷:原槽进入投掷冷却,生成投掷副本(IsDisposableCopy)向面朝方向飞出,
    /// 按物品自身投掷参数飞行、可砸伤敌人、命中/落点销毁。CD 中的武器跳过(与手动投掷同可投性)。
    /// 闪避检测沿 NormalDashCacheEffect 轮询范式(Enter 时刻变化 + LastDashWasBackDash)。
    /// </summary>
    [GlobalClass]
    public partial class BackDashItemDropEffect : ActorEffect
    {
        /// <summary>固定短抛:后撤甩出重载的投掷距离/飞行时长(覆盖武器定义参数;普通投掷不受影响)。</summary>
        private const float OverrideThrowDistancePx = 100f;
        private const float OverrideThrowDurationSec = 0.25f;

        private MainCharacter? _player;
        private PlayerDashState? _dash;
        private ulong _lastSeenEnteredAtMs;

        protected override void OnApply()
        {
            _player = Actor as MainCharacter;
            _dash = Actor?.StateMachine?.GetNodeOrNull<PlayerDashState>("Dash");
            _lastSeenEnteredAtMs = 0;
        }

        protected override void OnTick(double delta)
        {
            if (_player == null || _dash == null || !IsInstanceValid(_player)) return;

            ulong entered = _dash.LastDashEnteredAtMs;
            if (entered != _lastSeenEnteredAtMs)
            {
                _lastSeenEnteredAtMs = entered;
                if (_dash.LastDashWasBackDash)
                    ThrowFirstThrowableWeapon();
            }
        }

        /// <summary>快捷栏排序最靠前的可用投掷武器,做一次攻击性投掷（与普通投掷同构；
        /// 实现抽到 <see cref="ThrowWeaponLauncher"/>，与 B_010 连锁响应同源）。
        /// 该次飞行按固定短抛覆盖（200px/0.25s），不读武器自身投掷参数。</summary>
        private void ThrowFirstThrowableWeapon()
        {
            if (_player == null) return;
            ThrowWeaponLauncher.LaunchFrontmost(_player, OverrideThrowDistancePx, OverrideThrowDurationSec);
        }
    }
}
