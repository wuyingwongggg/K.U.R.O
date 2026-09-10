using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 坐标寻址（BuildThrow_A_010）：乱码块可以在**指定位置**生成。
    /// 指定位置由 AimPointResolver 解析——设备无关:鼠标为主,手柄右摇杆/键盘回退朝向×距离,
    /// 距离统一钳制在 AimRange 内(超出射程不会误放到天边)。
    /// 卡只负责:注册核心的 SpawnAtAimPointRange + 创建/移除解析器组件;生成管线单点读取。
    /// </summary>
    [GlobalClass]
    public partial class ThrowAimPositionEffect : ActorEffect
    {
        /// <summary>指定位置的最大距离(px):瞄准点与玩家距离超过该值会被钳制。</summary>
        [Export(PropertyHint.Range, "100,2000,10")] public float AimRange { get; set; } = 600f;

        private ThrowCoreEffect? _core;
        private AimPointResolver? _resolver;

        protected override void OnApply()
        {
            _core = Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            ApplyTier();
        }

        protected override void OnStackRefreshed()
        {
            _core ??= Actor?.EffectController?.GetEffect<ThrowCoreEffect>();
            ApplyTier();
        }

        private void ApplyTier()
        {
            if (Actor == null) return;

            // 解析器存在即启用:创建一次并注入射程(供生成点与预览标记共用)
            _resolver = AimPointResolver.GetOrCreate(Actor);
            _resolver.MaxRange = AimRange;

            if (_core != null)
                _core.SpawnAtAimPointRange = AimRange;
        }

        public override void OnRemoved()
        {
            if (_core != null && Mathf.Abs(_core.SpawnAtAimPointRange - AimRange) < 0.01f)
                _core.SpawnAtAimPointRange = 0f;
            _core = null;

            // 本卡是解析器唯一使用方:移除时一并释放(存在即启用语义)
            if (_resolver != null && IsInstanceValid(_resolver))
                _resolver.QueueFree();
            _resolver = null;

            base.OnRemoved();
        }
    }
}
