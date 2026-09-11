using System;
using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 坐标寻址（BuildThrow_A_010）：乱码块可以在**指定位置**生成。
    /// 短按核心技能进入瞄准模式(ThrowAimTargetingController):时间减缓 + 黑幕 + 瞄准点幽灵预览,
    /// 攻击键确认在该点生成、再按核心技能取消——指定位置由 AimPointResolver 解析(设备无关:
    /// 鼠标为主,手柄右摇杆/键盘回退朝向×距离)。
    /// 卡只负责:注册核心的 SpawnAtAimPointRange + 瞄准模式入口 + 创建/移除解析器与控制器组件;生成管线单点读取。
    /// </summary>
    [GlobalClass]
    public partial class ThrowAimPositionEffect : ActorEffect
    {
        /// <summary>指定位置的最大距离(px):手柄回退瞄准的基准距离(鼠标/键盘不做钳制)。</summary>
        [Export(PropertyHint.Range, "100,2000,10")] public float AimRange { get; set; } = 600f;

        private ThrowCoreEffect? _core;
        private AimPointResolver? _resolver;
        private ThrowAimTargetingController? _controller;
        private Func<bool>? _enterHandler;

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

            // 解析器存在即启用:创建一次并注入射程(供生成点与瞄准幽灵共用)
            _resolver = AimPointResolver.GetOrCreate(Actor);
            _resolver.MaxRange = AimRange;

            // 瞄准模式控制器:核心短按(未被 A_009 消费)时进入瞄准模式
            _controller = ThrowAimTargetingController.GetOrCreate(Actor);
            _enterHandler ??= TryEnterAimMode;

            if (_core != null)
            {
                _core.SpawnAtAimPointRange = AimRange;
                _core.AimModeEnterHandler = _enterHandler;
            }
        }

        private bool TryEnterAimMode()
        {
            if (Actor == null) return false;
            _controller ??= ThrowAimTargetingController.GetOrCreate(Actor);
            return _controller != null && _controller.TryEnter();
        }

        public override void OnRemoved()
        {
            if (_core != null)
            {
                if (Mathf.Abs(_core.SpawnAtAimPointRange - AimRange) < 0.01f)
                    _core.SpawnAtAimPointRange = 0f;
                if (ReferenceEquals(_core.AimModeEnterHandler, _enterHandler))
                    _core.AimModeEnterHandler = null;
            }
            _core = null;
            _enterHandler = null;

            // 本卡是解析器/控制器唯一使用方:移除时一并释放(存在即启用语义;控制器退出会还原时间/输入)
            if (_resolver != null && IsInstanceValid(_resolver))
                _resolver.QueueFree();
            _resolver = null;
            if (_controller != null && IsInstanceValid(_controller))
                _controller.QueueFree();
            _controller = null;

            base.OnRemoved();
        }
    }
}
