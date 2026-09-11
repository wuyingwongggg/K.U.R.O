using Godot;
using Kuros.Actors.Heroes;
using Kuros.Actors.Heroes.States;
using Kuros.Core;
using Kuros.Core.Effects;
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

        /// <summary>快捷栏排序最靠前的可用投掷武器,做一次攻击性投掷(与普通投掷同构)。</summary>
        private void ThrowFirstThrowableWeapon()
        {
            var inventory = _player?.InventoryComponent;
            if (inventory?.QuickBar == null) return;

            var quickBar = inventory.QuickBar;
            InventoryItemStack? picked = null;
            int slotIndex = -1;

            for (int i = 0; i < quickBar.Slots.Count; i++)
            {
                var stack = quickBar.GetStack(i);
                if (stack == null || stack.IsEmpty) continue;
                if (stack.Item.ItemId == "empty_item") continue;
                if (!stack.Item.IsThrowWeapon) continue; // 投掷武器(背包内投掷物本体)
                if (stack.IsThrowOnCooldown) continue;    // 冷却中(与手动投掷同可投性)
                picked = stack;
                slotIndex = i;
                break;
            }

            if (picked == null) return;

            // 原槽进入投掷冷却(普通投掷同款)
            picked.ThrowCooldownRemaining = picked.Item.ThrowWeaponCooldown;
            inventory.NotifyCombatWeaponResolutionChanged();

            // 投掷武器投掷 = 生成副本(不扣原槽数量),与 PlayerItemInteractionComponent 同构
            var extracted = new InventoryItemStack(picked.Item, 1);
            var spawned = WorldItemSpawner.SpawnFromStack(this, extracted, _player!.GlobalPosition);
            if (spawned == null)
                return;

            spawned.LastDroppedBy = Actor;
            if (spawned is RigidBodyWorldItemEntity rigidEntity)
            {
                rigidEntity.IsDisposableCopy = true;
                // 固定短抛重载:该次飞行按 200px/0.3s,不读武器自身投掷参数
                rigidEntity.OverrideThrowDistance = OverrideThrowDistancePx;
                rigidEntity.OverrideThrowDuration = OverrideThrowDurationSec;
                Vector2 facing = _player.FacingRight ? Vector2.Right : Vector2.Left;
                float impulse = ResolveThrowImpulse();
                rigidEntity.ApplyThrowImpulse(facing * impulse);
                rigidEntity.ZIndex = picked.Item.ThrowZIndex;
            }
            else if (spawned is Node2D node)
            {
                node.QueueFree(); // 非 RigidBody 实体无法投掷:放弃(不扣槽)
                picked.ThrowCooldownRemaining = 0f;
            }
        }

        private float ResolveThrowImpulse()
        {
            var interaction = _player?.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
            if (interaction != null && interaction.ThrowImpulse > 0f)
                return interaction.ThrowImpulse;
            return 800f;
        }
    }
}
