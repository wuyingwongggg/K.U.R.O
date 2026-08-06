using Godot;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;

namespace Kuros.Builds.Machine
{
    /// <summary>
    /// 热能抽离：热量达到 100%（MaxHeat）及以上时释放核心技能，
    /// 在玩家位置（快照）生成甜甜圈型火焰环特效，并发出范围信号。
    /// 视觉与伤害由导出的 EffectScene（火焰环场景）负责，本脚本只负责触发与定位。
    /// </summary>
    [GlobalClass]
    public partial class MachineThermalSunderEffect : ActorEffect
    {
        /// <summary>火焰环特效场景（甜甜圈型，视觉+伤害），在玩家位置快照处实例化。</summary>
        [Export] public PackedScene? EffectScene = null;
        /// <summary>每层火焰环最大范围（px）。</summary>
        [Export] public float[] RangeValues { get; set; } = { 300f, 500f };

        /// <summary>触发生成时发出，携带当前层范围值（px）。</summary>
        [Signal] public delegate void FlameRingRequestedEventHandler(float radius);

        private MachineCoreEffect? _core;
        private int _tier;
        private bool _buffWasActive;

        private float CurrentRange => _tier < RangeValues.Length ? RangeValues[_tier] : RangeValues[^1];

        protected override void OnApply()
        {
            _tier = 0;
            _core = Actor?.EffectController?.GetEffect<MachineCoreEffect>();
            _buffWasActive = _core?.IsBuffActive ?? false;
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, RangeValues.Length - 1);
        }

        protected override void OnTick(double delta)
        {
            if (_core == null || Actor == null) return;

            // 检测释放核心技能（buff false→true）
            bool buffActive = _core.IsBuffActive;
            if (buffActive && !_buffWasActive)
            {
                // 热量达到 100%（MaxHeat）及以上时释放 → 生成火焰环
                if (_core.Heat >= _core.MaxHeat)
                    SpawnFlameRing();
            }
            _buffWasActive = buffActive;
        }

        private void SpawnFlameRing()
        {
            Vector2 snapshotPos = Actor!.GlobalPosition; // 玩家位置快照

            // 发出范围信号（当前层 300/500px）
            EmitSignal(SignalName.FlameRingRequested, CurrentRange);

            // 在玩家位置快照处实例化火焰环特效
            if (EffectScene == null) return;
            var node = EffectScene.Instantiate();
            if (node is not Node2D node2d) { node?.QueueFree(); return; }

            var parent = Actor.GetParent();
            parent?.AddChild(node2d);
            node2d.GlobalPosition = snapshotPos;
            // 传入当前层范围与灼烧来源（玩家）
            node2d.Set("MaxRadius", CurrentRange);
            node2d.Set("Attacker", Actor);
        }
    }
}
