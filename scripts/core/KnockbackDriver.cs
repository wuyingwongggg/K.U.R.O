using Godot;

namespace Kuros.Core
{
    /// <summary>
    /// 临时挂载到被击退目标上的物理驱动节点。
    /// 每物理帧通过 MoveAndCollide 施加位移，自动与静态体（墙壁/家具）碰撞停止。
    /// 在 StateMachine._PhysicsProcess 之后执行，避免与 HitState.MoveAndSlide 叠加。
    /// 由 KnockbackOnAttackEffect、MachineBurstThrustEffect 等效果共用。
    /// </summary>
    public sealed partial class KnockbackDriver : Node
    {
        private const string NodeName = "__KnockbackDriver__";

        private CharacterBody2D? _target;
        private Vector2 _direction;
        private float _initialSpeed;
        private float _duration;
        private float _elapsed;

        /// <summary>
        /// 将击退驱动节点附加到目标身上。若目标已有驱动节点则先移除（替换式）。
        /// </summary>
        public static void Attach(CharacterBody2D target, Vector2 direction,
            float initialSpeed, float duration)
        {
            var existing = target.GetNodeOrNull<KnockbackDriver>(NodeName);
            if (existing != null && GodotObject.IsInstanceValid(existing))
            {
                existing.QueueFree();
            }

            var driver = new KnockbackDriver
            {
                Name = NodeName,
                _target = target,
                _direction = direction,
                _initialSpeed = initialSpeed,
                _duration = Mathf.Max(duration, 0.01f),
                _elapsed = 0f,
                // 使其在父节点 StateMachine 之后执行，避免 MoveAndSlide 叠加
                ProcessPhysicsPriority = 10
            };
            target.AddChild(driver);
        }

        /// <summary>
        /// 累加式击退：若目标已有驱动节点，把新击退的位移并入现有驱动
        /// （总位移相加、总时长取较长者、方向沿用先挂载者）；无驱动时等同 Attach。
        /// 用于需要与武器自带击退叠加的效果（如爆发推力）。
        /// </summary>
        public static void AttachStack(CharacterBody2D target, Vector2 direction,
            float initialSpeed, float duration)
        {
            var existing = target.GetNodeOrNull<KnockbackDriver>(NodeName);
            if (existing != null && GodotObject.IsInstanceValid(existing))
            {
                existing.Stack(initialSpeed, duration);
                return;
            }
            Attach(target, direction, initialSpeed, duration);
        }

        /// <summary>
        /// 把一段新击退（初速度 v、时长 T → 位移 v×T/2）并入当前驱动：
        /// 剩余位移 + 新增位移 = 新总位移，时长取较长者，从当前时刻重启线性减速曲线（位移守恒）。
        /// </summary>
        public void Stack(float initialSpeed, float duration)
        {
            // 当前剩余位移 = v0×(1-t/T)×(T-t)/2（线性减速曲线在 t..T 段的积分）
            float remaining = _initialSpeed * (1f - _elapsed / _duration) / 2f * (_duration - _elapsed);
            float added = initialSpeed * Mathf.Max(duration, 0.01f) / 2f;
            float newDuration = Mathf.Max(_duration - _elapsed, Mathf.Max(duration, 0.01f));

            _elapsed = 0f;
            _duration = newDuration;
            _initialSpeed = 2f * (remaining + added) / newDuration;
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_target == null || !GodotObject.IsInstanceValid(_target)
                || !_target.IsInsideTree())
            {
                QueueFree();
                return;
            }

            _elapsed += (float)delta;

            if (_elapsed >= _duration)
            {
                // 确保 HitState 下一帧从零速度开始
                _target.Velocity = Vector2.Zero;
                QueueFree();
                return;
            }

            // 线性减速：速度从 _initialSpeed 线性降到 0
            float t = _elapsed / _duration;
            float currentSpeed = _initialSpeed * (1f - t);

            Vector2 displacement = _direction * currentSpeed * (float)delta;

            // 用 MoveAndCollide 施加位移，自动与碰撞体停止
            _target.MoveAndCollide(displacement);

            // 将 Velocity 清零，防止 HitState 的 MoveAndSlide 再次叠加位移
            _target.Velocity = Vector2.Zero;
        }
    }
}
