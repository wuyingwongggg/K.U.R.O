using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Items
{
    /// <summary>
    /// 投掷武器"甩出/打出"工具，两种用法：
    ///   · <see cref="LaunchFrontmost"/>（剧终谢幕 B_006-Normal）：快捷栏最前列一件，**消耗原槽 CD**，
    ///     从玩家身上向面朝方向甩出——常规攻击性投掷（飞行副本 / SpawnEffectOnThrow 类直接出效果）。
    ///   · <see cref="TriggerWeaponEffectAt"/>（连锁响应 B_010）：随机一件，**不占 CD、不扣数量、不飞行**——
    ///     在指定位置（道具销毁点）生成副本后立刻走"投掷销毁"链，把该武器的 OnThrowDestroy 效果打在那里。
    /// </summary>
    public static class ThrowWeaponLauncher
    {
        private enum LaunchMode
        {
            /// <summary>从投掷者身上甩出（最前列 / 跳过冷却中 / 消耗 CD）。</summary>
            FrontmostFromOwner,
            /// <summary>在指定位置原地打出武器效果（随机 / 不看 CD / 不消耗 CD / 不飞行）。</summary>
            DestroyAtPosition,
        }

        /// <summary>甩出快捷栏**排序最靠前**的可用投掷武器（跳过 CD 中；消耗其 CD）。
        /// overrideDistance/overrideDuration &gt;0 时覆盖该次飞行的距离/时长（固定短抛用）。</summary>
        public static bool LaunchFrontmost(GameActor player, float overrideDistance = 0f, float overrideDuration = 0f)
            => Launch(player, LaunchMode.FrontmostFromOwner, player.GlobalPosition, overrideDistance, overrideDuration);

        /// <summary>在指定位置**免费**打出随机一件投掷武器的效果：生成副本后立刻按"投掷销毁"处理，
        /// 其 OnThrowDestroy 效果（= 各武器自己的投掷表现）就落在该点；不飞行、不落地、不进 CD、不扣数量，
        /// 因此也不会被拾取或产生复制——连锁响应 B_010 专用。</summary>
        public static bool TriggerWeaponEffectAt(GameActor player, Vector2 position)
            => Launch(player, LaunchMode.DestroyAtPosition, position, 0f, 0f);

        private static bool Launch(GameActor player, LaunchMode mode, Vector2 origin,
            float overrideDistance, float overrideDuration)
        {
            bool randomPick = mode == LaunchMode.DestroyAtPosition;
            bool fromOwner = mode == LaunchMode.FrontmostFromOwner;

            var inventory = (player as SamplePlayer)?.InventoryComponent;
            var quickBar = inventory?.QuickBar;
            if (quickBar == null) return false;

            // 选取：剧终谢幕 = 最前列且不在冷却；连锁响应 = 随机一件（CD 状态与本次无关）
            InventoryItemStack? picked = null;
            int available = 0;
            for (int i = 0; i < quickBar.Slots.Count; i++)
            {
                var stack = quickBar.GetStack(i);
                if (stack == null || stack.IsEmpty) continue;
                if (stack.Item.ItemId == "empty_item") continue;
                if (!stack.Item.IsThrowWeapon) continue;              // 投掷武器（背包内投掷物本体）
                if (fromOwner && stack.IsThrowOnCooldown) continue;   // 冷却中（与手动投掷同可投性）

                available++;
                // 蓄水池抽样：随机选取时无需先收集成表
                if (!randomPick || GD.RandRange(1, available) == 1)
                    picked = stack;
                if (!randomPick) break;
            }
            if (picked == null) return false;

            if (fromOwner)
            {
                // 原槽进入投掷冷却（普通投掷同款）；原地打效果模式不碰原槽
                picked.ThrowCooldownRemaining = picked.Item.ThrowWeaponCooldown;
                inventory!.NotifyCombatWeaponResolutionChanged();
            }

            // 生成副本（不扣原槽数量），与 PlayerItemInteractionComponent 同构
            var extracted = new InventoryItemStack(picked.Item, 1);
            var spawned = WorldItemSpawner.SpawnFromStack(player, extracted, origin);
            if (spawned == null) return false;

            if (spawned is not RigidBodyWorldItemEntity rigidEntity)
            {
                // 非 RigidBody 实体无法投掷：放弃并回滚冷却（不吞武器）
                (spawned as Node2D)?.QueueFree();
                if (fromOwner) picked.ThrowCooldownRemaining = 0f;
                return false;
            }

            rigidEntity.IsDisposableCopy = true;
            if (overrideDistance > 0f) rigidEntity.OverrideThrowDistance = overrideDistance;
            if (overrideDuration > 0f) rigidEntity.OverrideThrowDuration = overrideDuration;

            if (mode == LaunchMode.DestroyAtPosition)
            {
                // 归属先写好（销毁链按 LastDroppedBy 注入攻击者），随即原地销毁 → 打出 OnThrowDestroy 效果
                spawned.LastDroppedBy = player;
                rigidEntity.RequestDestroy();
                return true;
            }

            // 从投掷者身上出手：先设归属，ApplyThrowImpulse 才会以"投掷者位置"为飞行起点
            spawned.LastDroppedBy = player;
            Vector2 direction = player.FacingRight ? Vector2.Right : Vector2.Left;
            rigidEntity.ApplyThrowImpulse(direction * ResolveThrowImpulse(player));
            rigidEntity.ZIndex = picked.Item.ThrowZIndex;
            return true;
        }

        private static float ResolveThrowImpulse(GameActor player)
        {
            var interaction = player.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
            if (interaction != null && interaction.ThrowImpulse > 0f)
                return interaction.ThrowImpulse;
            return 800f;
        }
    }
}
