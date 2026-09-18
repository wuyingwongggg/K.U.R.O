using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 隔空抓取（BuildThrow_B_009）：拾取以**瞄准点（鼠标指针世界坐标）**为目标——
    /// 抓取瞄准点半径内最近一件，不再要求玩家走到道具旁边；瞄准点附近没有可拾取物时回落常规拾取。
    /// 高亮同步跟随瞄准点（同一次选取），做到"看到的就是抓到的"。
    /// 瞄准点优先复用 A_010 的 AimPointResolver（设备无关：鼠标/手柄/键盘回退；本卡不会创建它），
    /// 未装该组件时直接用鼠标世界坐标。放置/投掷不受影响。
    /// </summary>
    [GlobalClass]
    public partial class ThrowRemoteGrabEffect : ActorEffect
    {
        /// <summary>抓取判定半径（世界像素）：瞄准点该半径内最近一件会被抓取。0 = 不生效。</summary>
        [Export(PropertyHint.Range, "0,600,10")] public float GrabRadius { get; set; } = 160f;

        private PlayerItemInteractionComponent? _interaction;

        protected override void OnApply()
        {
            _interaction = Actor?.GetNodeOrNull<PlayerItemInteractionComponent>("ItemInteraction");
            if (_interaction != null)
                _interaction.AimInteractRadius = Mathf.Max(GrabRadius, 0f);
        }

        public override void OnRemoved()
        {
            if (_interaction != null && GodotObject.IsInstanceValid(_interaction))
                _interaction.AimInteractRadius = 0f;

            _interaction = null;
            base.OnRemoved();
        }
    }
}
