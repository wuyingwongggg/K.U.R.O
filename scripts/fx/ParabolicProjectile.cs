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
        [ExportCategory("Trajectory")]
        /// <summary>飞行方向（单位向量）。外部可在 AddChild 前覆盖。</summary>
        [Export] public Vector2 Direction = Vector2.Up;
        /// <summary>飞行水平距离（像素）。</summary>
        [Export] public float Distance = 300f;
        /// <summary>飞行总时长（秒）。</summary>
        [Export] public float Duration = 1.0f;
        /// <summary>抛物线峰值高度（像素，正值向上）。</summary>
        [Export] public float PeakHeight = 400f;

        [ExportCategory("Damage")]
        /// <summary>击中造成的伤害。</summary>
        [Export] public int Damage = 20;
        /// <summary>可攻击的阵营。</summary>
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player;
        /// <summary>是否允许伤害同阵营。</summary>
        [Export] public bool AllowSelfDamage;

        [ExportCategory("Knockback")]
        /// <summary>击退速度（像素/秒），0 不击退。</summary>
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 200f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        [ExportCategory("Hit Detection")]
        /// <summary>命中判定 Area2D 路径。</summary>
        [Export] public NodePath HitboxPath = new("AttackArea");

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _elapsed;
        private bool _launched;
        private bool _hasHit;
        private Area2D? _hitbox;
        private GameActor? _attacker;

        public override void _Ready()
        {
            if (!HitboxPath.IsEmpty)
                _hitbox = GetNodeOrNull<Area2D>(HitboxPath);

            if (_hitbox != null)
            {
                _hitbox.BodyEntered += OnHitboxBodyEntered;
                _hitbox.AreaEntered += OnHitboxAreaEntered;
            }

            ResolveAttacker();
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
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;

            bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
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
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, _attacker)) return;

            bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _hitbox);
            if (!dealt) return;

            if (area.Owner is GameActor actor)
                ApplyKnockback(actor);

            _hasHit = true;
            QueueFree();
        }

        /// <summary>
        /// 沿飞行方向施加击退速度。方向为零时回退到 Vector2.Right。
        /// </summary>
        private void ApplyKnockback(GameActor actor)
        {
            if (KnockbackSpeed <= 0f) return;

            Vector2 dir = Direction;
            if (dir.LengthSquared() < 0.01f)
                dir = Vector2.Right;

            actor.Velocity = dir.Normalized() * KnockbackSpeed;
        }

        /// <summary>
        /// 从父节点查找 enemies 组的 GameActor 作为攻击来源，用于 DealDamage 的阵营过滤。
        /// </summary>
        private void ResolveAttacker()
        {
            var parent = GetParent();
            if (parent == null) return;
            foreach (var child in parent.GetChildren())
            {
                if (child.IsInGroup("enemies") && child is GameActor ga)
                {
                    _attacker = ga;
                    break;
                }
            }
        }
    }
}
