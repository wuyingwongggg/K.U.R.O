using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;
using Kuros.Items.World;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 对象撤回（BuildThrow_B_006）：投出的道具不再销毁 —— 出手瞬间给该件置"落地不销毁"标记，
    /// 到达落点后收尾成普通世界物（同步根位置、还原图层/碰撞），玩家可走过去用拾取键正常捡回背包。
    /// 投掷耐久：同一件最多投出 TierValues 次（层1 = 2 次 / 层2 = 3 次；计数随件跨"拾取→放置→投掷"由栈携带），
    /// 第 N 次投掷照常飞行/结算伤害，落地后正常销毁——防止本卡变成"道具永不消耗"。
    /// 只认本次投掷的**原件**（PieceThrown 钩子给的就是原件）：A_008 分裂件不经此钩子、不标记，
    /// 否则一次投掷裂 3 枚、捡回 3 件 = 白给道具。
    /// 投掷武器交给实体侧过滤（IsThrowWeapon 不适用：它本就 2s 自动归还且飞行的是临时副本）。
    /// </summary>
    [GlobalClass]
    public partial class ThrowRecallEffect : ActorEffect
    {
        /// <summary>各层投掷耐久上限（次，B_006 注入 [2,3]）：同一件投出该层次数后落地即销毁。
        /// 数组形式以便卡面描述用 {0}/{1} token 显示，单一真源。</summary>
        [Export] public float[] TierValues { get; set; } = { 2f, 3f };

        private int _tier = 1;

        protected override void OnApply()
        {
            PlayerItemInteractionComponent.PieceThrown += OnPieceThrown;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        public override void OnRemoved()
        {
            PlayerItemInteractionComponent.PieceThrown -= OnPieceThrown;
            base.OnRemoved();
        }

        /// <summary>出手瞬间标记本次投掷件：落地不销毁（留在场上可拾取），并交给当前层的投掷耐久上限。</summary>
        private void OnPieceThrown(RigidBodyWorldItemEntity entity)
        {
            if (Actor == null || !GodotObject.IsInstanceValid(entity)) return;
            if (entity.LastDroppedBy != Actor) return;

            entity.KeepAfterLanding = true;

            int idx = Mathf.Clamp(_tier, 1, Mathf.Max(TierValues.Length, 1)) - 1;
            entity.KeepThrowLimit = TierValues.Length > idx
                ? Mathf.Max(0, Mathf.RoundToInt(TierValues[idx]))
                : 0;
        }
    }
}
