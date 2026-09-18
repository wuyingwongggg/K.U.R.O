using Godot;
using Kuros.Managers;

namespace Kuros.Effects
{
    /// <summary>
    /// 镜头震动（全局反馈）：延迟若干秒后触发一次 <see cref="CameraFollow.Shake"/>，然后自销毁。
    ///
    /// 按 EFFECT_STANDARD 的"全局反馈"一族实现为 **Node2D + 自管理**（不走 ActorEffect / EffectController）：
    ///   · 与"世界效果"同族不是因为它需要世界坐标（其实不需要），而是必须
    ///     **脱离 EffectId 去重**——旧实现挂在 EffectController 上，而本场景写死了固定 EffectId，
    ///     同一时间窗内的多次震屏（例如两台机械臂同时投放、或多个家具同时炸开）只会生效一次；
    ///     并且**脱离 Actor 生命周期绑定**——敌人/投掷物的生死不该取消已经安排的震屏。
    ///   · 生成路径：敌人的 AttackEffectEntry 与物品的 OnThrowDestroy 都已有 `is Node2D` 的世界生成分支。
    /// </summary>
    [GlobalClass]
    public partial class CameraShakeEffect : Node2D
    {
        /// <summary>镜头震动强度（像素）</summary>
        [Export(PropertyHint.Range, "1,200,1")] public float ShakeStrength { get; set; } = 12.0f;

        /// <summary>延迟触发时间（秒），0 表示立即触发</summary>
        [Export(PropertyHint.Range, "0,10,0.1")] public float ShakeDelay { get; set; } = 0.0f;

        /// <summary>存活时长（秒，自销毁）。沿用旧 ActorEffect 的 Duration 语义——
        /// 节点总寿命 = max(Duration, ShakeDelay + 0.1)，保证延迟还没到不会被提前销毁。0 = 震屏后立即销毁。</summary>
        [Export(PropertyHint.Range, "0,10,0.05")] public float Duration { get; set; } = 0.05f;

        private Timer? _delayTimer;
        private Timer? _lifeTimer;
        private bool _shaken;

        public override void _Ready()
        {
            _lifeTimer = CreateOneShotTimer(Mathf.Max(Duration, ShakeDelay + 0.1f), OnLifetimeTimeout);

            // 无延迟的情况**不在这里震**：物品投掷时会预热 OnThrowDestroy 特效
            //（RigidBodyWorldItemEntity.WarmUpThrowDestroyEffectShaders：隐藏 + ProcessMode.Disabled + 入树），
            // _Ready 照跑 → 会把预热当成一次真触发、在"投掷瞬间"就震屏。改到首个可处理帧即可——
            // 预热实例是 Disabled，永远不会走到 _Process。
            if (ShakeDelay <= 0f) return;

            _delayTimer = CreateOneShotTimer(ShakeDelay, OnDelayTimeout);
        }

        public override void _Process(double delta)
        {
            if (_shaken || ShakeDelay > 0f) return;   // 延迟路径由计时器负责
            _shaken = true;
            SetProcess(false);
            DoShake();
        }

        public override void _ExitTree()
        {
            CleanupTimer(ref _delayTimer, OnDelayTimeout);
            CleanupTimer(ref _lifeTimer, OnLifetimeTimeout);
        }

        private Timer CreateOneShotTimer(float seconds, System.Action onTimeout)
        {
            var timer = new Timer { OneShot = true, WaitTime = Mathf.Max(0.01f, seconds) };
            timer.Timeout += onTimeout;
            AddChild(timer);
            timer.Start();
            return timer;
        }

        private void OnDelayTimeout()
        {
            CleanupTimer(ref _delayTimer, OnDelayTimeout);
            if (_shaken) return;
            _shaken = true;
            DoShake();
        }

        private void OnLifetimeTimeout()
        {
            CleanupTimer(ref _lifeTimer, OnLifetimeTimeout);
            QueueFree();
        }

        private void CleanupTimer(ref Timer? timer, System.Action onTimeout)
        {
            var target = timer;
            timer = null;
            if (target == null || !GodotObject.IsInstanceValid(target)) return;
            target.Stop();
            target.Timeout -= onTimeout;
            target.QueueFree();
        }

        private void DoShake()
        {
            if (GetViewport()?.GetCamera2D() is CameraFollow camera)
            {
                camera.Shake(ShakeStrength);
                return;
            }

            GD.PushWarning("[CameraShakeEffect] 找不到 CameraFollow，无法触发镜头震动");
        }
    }
}
