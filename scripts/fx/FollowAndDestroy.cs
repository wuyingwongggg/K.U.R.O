using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 跟随目标并定时销毁（攻击预警特效等用途）：每帧同步到锚点位置（+Offset）。
    /// Duration &gt; 0：到期销毁自身；Duration &lt;= 0：保持存在，直到生成它的对象（敌人/锚点）被销毁。
    /// 实现 IAttackerProvider（生成链自动注入敌人引用）+ IFollowAnchor（生成方注入"产生它的锚点节点"——
    /// 实际使用的 marker 或敌人根，优先跟随锚点）。
    /// </summary>
    public partial class FollowAndDestroy : Node2D, IAttackerProvider, IFollowAnchor
    {
        public GameActor? Attacker { get; set; }

        /// <summary>跟随的锚点节点（生成方注入：实际使用的 marker 或敌人根）。null 时回退 Attacker。</summary>
        public Node2D? FollowAnchor { get; set; }

        /// <summary>相对锚点的偏移。</summary>
        [Export] public Vector2 Offset = Vector2.Zero;

        /// <summary>生命周期（秒，同 ActorEffect.Duration 语义）。&lt;= 0 = 不自动销毁，跟随至生成方销毁。</summary>
        [Export(PropertyHint.Range, "0,10,0.1")] public float Duration = 1.0f;

        private float _timer;

        public override void _Ready()
        {
            _timer = Duration;
        }

        public override void _Process(double delta)
        {
            // 生成方失效（敌人死亡/离场，或跟随锚点被销毁）→ 立即销毁
            if (Attacker != null && !GodotObject.IsInstanceValid(Attacker))
            {
                QueueFree();
                return;
            }
            if (FollowAnchor != null && !GodotObject.IsInstanceValid(FollowAnchor))
            {
                QueueFree();
                return;
            }

            // 优先跟随"产生它的锚点节点"（marker/敌人根），未注入时回退攻击者
            Node2D? follow = FollowAnchor ?? Attacker as Node2D;
            if (follow != null && GodotObject.IsInstanceValid(follow))
                GlobalPosition = follow.GlobalPosition + Offset;

            // Duration <= 0：保持存在（不自动销毁），跟随至生成方销毁
            if (Duration > 0f)
            {
                _timer -= (float)delta;
                if (_timer <= 0f)
                    QueueFree();
            }
        }
    }
}
