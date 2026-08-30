using Godot;

namespace Kuros.Effects
{
    /// <summary>
    /// 受伤眩晕打断的时间减缓载体：全局 Engine.TimeScale 减缓（无视觉层）。
    /// 静态触发、单实例宿主：重叠触发时新触发覆盖旧的（重置剩余时长与倍率），
    /// 只有最后一次触发到期才恢复 TimeScale = 1。
    /// 持续计时用真实时间（delta / TimeScale 还原）——"持续 N 秒"按玩家体感时间。
    /// 已知共存：NPC 对话暂停也使用 Engine.TimeScale（0→1），战斗中不会重叠，可接受。
    /// </summary>
    public static partial class DamageInterruptSlowMo
    {
        private static SlowMoHost? _host;

        /// <summary>触发全局时间减缓。timeScale 如 0.3 = 30%；durationSeconds 为真实秒。</summary>
        public static void Trigger(float timeScale, float durationSeconds)
        {
            if (durationSeconds <= 0f || timeScale <= 0f) return;
            EnsureHost();
            if (_host == null || !GodotObject.IsInstanceValid(_host)) return;

            _host.Begin(Mathf.Clamp(timeScale, 0.05f, 1f), durationSeconds);
        }

        private static void EnsureHost()
        {
            if (_host != null && GodotObject.IsInstanceValid(_host)) return;
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;

            _host = new SlowMoHost { Name = "DamageInterruptSlowMo" };
            tree.Root.AddChild(_host);
        }

        private sealed partial class SlowMoHost : Node
        {
            private float _remaining;

            public void Begin(float timeScale, float durationSeconds)
            {
                _remaining = durationSeconds;
                Engine.TimeScale = timeScale;
            }

            public override void _Process(double delta)
            {
                if (_remaining <= 0f) return;

                // 真实时间倒计时（TimeScale < 1 时 delta 已被缩放，除以 TimeScale 还原真实秒）
                _remaining -= (float)delta / Mathf.Max((float)Engine.TimeScale, 0.05f);
                if (_remaining <= 0f)
                {
                    _remaining = 0f;
                    Engine.TimeScale = 1f;
                    QueueFree();
                }
            }

            public override void _ExitTree()
            {
                // 兜底：场景切换/强制销毁时若减缓仍在进行，恢复时间
                if (_remaining > 0f)
                    Engine.TimeScale = 1f;
                base._ExitTree();
            }
        }
    }
}
