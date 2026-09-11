using Godot;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 复制粘贴（BuildThrow_A_007）：生成乱码块时,若玩家当前高亮(描边)着某件家具
    /// (即玩家拾取范围/UI 提示内的家具),复制该家具(定义与形态)为乱码块;无高亮则生成普通乱码块。
    /// 通过把 ThrowCoreEffect.CopyNearbyFurnitureRange 置非 0 启用(生成管线读 CurrentHighlightedEntity)。
    /// </summary>
    [GlobalClass]
    public partial class ThrowCopyNearbyEffect : ActorEffect
    {
        /// <summary>仅作启用标志(非 0 即开启);目标范围由玩家 GrabArea 高亮裁决决定,无需距离值。</summary>
        private const float CopyEnableFlag = 1f;

        private ThrowCoreEffect? _core;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            if (_core != null)
                _core.CopyNearbyFurnitureRange = CopyEnableFlag;
        }

        public override void OnRemoved()
        {
            if (_core != null && Mathf.Abs(_core.CopyNearbyFurnitureRange - CopyEnableFlag) < 0.01f)
                _core.CopyNearbyFurnitureRange = 0f;
            _core = null;
            base.OnRemoved();
        }
    }
}
