using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 背包扩容（BuildNormal_C_001）：每层解锁 1 个武器携带/快捷栏槽位
    /// （UnlockWeaponSlot,封顶 MaxCarriedWeaponSlots）。槽位为局内持久配置,
    /// 新一局由 ResetWeaponSlots 还原,故 OnRemoved 不回退。
    /// </summary>
    [GlobalClass]
    public partial class BackpackExpandEffect : ActorEffect
    {
        /// <summary>显示层数值(PropertyOverrides 注入匹配用;每层实际效果 = 一次 UnlockWeaponSlot,脚本不读取此数组)。</summary>
        [Export] public float[] TierValues { get; set; } = { 1f, 2f };

        private MainCharacter? _player;

        protected override void OnApply()
        {
            _player = Actor as MainCharacter;
            UnlockOnce();
        }

        protected override void OnStackRefreshed()
        {
            UnlockOnce();
        }

        private void UnlockOnce()
        {
            if (_player == null || !IsInstanceValid(_player)) return;
            var inventory = _player.InventoryComponent;
            inventory?.UnlockWeaponSlot();
        }
    }
}
