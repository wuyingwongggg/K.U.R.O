using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 自驱动抛物线弹射体。
    /// 在 _Ready 首帧根据 Direction + Distance 计算落点，沿正弦抛物线飞行，
    /// 到达落点或命中后自毁。无需外部调用方设置落点。
    /// </summary>
    public partial class ParabolicProjectile : Node2D
    {
        /// <summary>飞行方向（单位向量）。外部可在 AddChild 前覆盖。</summary>
        [Export] public Vector2 Direction = Vector2.Up;
        /// <summary>飞行水平距离（像素）。</summary>
        [Export] public float Distance = 300f;
        /// <summary>飞行总时长（秒）。</summary>
        [Export] public float Duration = 1.0f;
        /// <summary>抛物线峰值高度（像素，正值向上）。</summary>
        [Export] public float PeakHeight = 400f;
        /// <summary>击中造成的伤害。</summary>
        [Export] public int Damage = 20;
        /// <summary>可攻击的阵营。</summary>
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player;
        /// <summary>是否允许伤害同阵营。</summary>
        [Export] public bool AllowSelfDamage;
        /// <summary>击退速度（像素/秒），0 不击退。</summary>
        [Export] public float KnockbackSpeed = 200f;
        /// <summary>命中判定 Area2D 路径。</summary>
        [Export] public NodePath HitboxPath = new("AttackArea");

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _elapsed;
        private bool _launched;
        private bool _hasHit;
        private Area2D? _hitbox;

        public override void _Ready()
        {
            if (!HitboxPath.IsEmpty)
                _hitbox = GetNodeOrNull<Area2D>(HitboxPath);

            if (_hitbox != null)
            {
                _hitbox.BodyEntered += OnHitboxBodyEntered;
                _hitbox.AreaEntered += OnHitboxAreaEntered;
            }

            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            // 首帧记录起点——此时 GlobalPosition 已被调用方设置
            if (!_launched)
            {
                _startPos = GlobalPosition;
                _targetPos = _startPos + Direction.Normalized() * Distance;
                _launched = true;
                return;
            }

            _elapsed += (float)delta;
            float t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

            float x = Mathf.Lerp(_startPos.X, _targetPos.X, t);
            float y = Mathf.Lerp(_startPos.Y, _targetPos.Y, t)
                       - Mathf.Sin(t * Mathf.Pi) * PeakHeight;
            GlobalPosition = new Vector2(x, y);

            if (t >= 1f)
                QueueFree();
        }

        public override void _ExitTree()
        {
            if (_hitbox != null)
            {
                _hitbox.BodyEntered -= OnHitboxBodyEntered;
                _hitbox.AreaEntered -= OnHitboxAreaEntered;
            }
            base._ExitTree();
        }

        private void OnHitboxBodyEntered(Node body)
        {
            if (_hasHit) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, null)) return;

            bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, null,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _hitbox);
            if (!dealt) return;

            if (body is GameActor actor)
                ApplyKnockback(actor);

            _hasHit = true;
            QueueFree();
        }

        private void OnHitboxAreaEntered(Area2D area)
        {
            if (_hasHit) return;
            var target = area.Owner ?? area;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, null)) return;

            bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, null,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _hitbox);
            if (!dealt) return;

            if (area.Owner is GameActor actor)
                ApplyKnockback(actor);

            _hasHit = true;
            QueueFree();
        }

        private void ApplyKnockback(GameActor actor)
        {
            if (KnockbackSpeed <= 0f) return;
            Vector2 dir = (actor.GlobalPosition - GlobalPosition).Normalized();
            if (dir == Vector2.Zero) dir = Vector2.Right;
            actor.Velocity = dir * KnockbackSpeed;
        }
    }
}
