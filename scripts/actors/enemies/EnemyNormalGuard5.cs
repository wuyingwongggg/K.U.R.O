using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Managers;

namespace Kuros.Actors.Enemies
{
    /// <summary>
    /// guard5（大盾保镖）：**常驻盾牌**——正面 180° 的伤害永久按比例减免
    /// （<see cref="FrontalDamageReduction"/>，默认 99%；减免后至少保留 1 点，
    /// 保证每次格挡都有受击反馈与击退——守卫被挡**不免疫击退**）。
    /// **跪地持盾与冲撞期间（ShieldStance 的 Warmup/Active = skill1 + skill2）升级为正面 180° 完全免疫**：
    /// 伤害不结算、不进受击（攻击方视为未命中，击退也不施加）——正面无法打断冲撞，只能绕背或闪避。
    /// 背面 / 侧面 / 判不了方向（正上正下、拿不到来源）→ 全额伤害。
    /// 被减免的伤害仍走正常受击管线（Hit 状态 + 击退），动画控制器按
    /// <see cref="LastHitFromFront"/> 选 hit1（正面格挡表现）/ hit2（背面受击表现）。
    /// </summary>
    public partial class EnemyNormalGuard5 : SampleEnemy
    {
        /// <summary>正面伤害减免比例（0.99 = 只吃 1%；上限 0.999，减免后最低 1 点）。</summary>
        [Export(PropertyHint.Range, "0,0.999,0.001")] public float FrontalDamageReduction = 0.99f;

        /// <summary>最近一次受击是否来自正面（动画控制器据此选 hit1/hit2）。</summary>
        public bool LastHitFromFront { get; private set; }

        private EnemyGuard5ShieldStanceAttack? _shieldStance;

        public override bool TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null,
            DamageSource damageSource = DamageSource.DirectAttack, bool bypassMergeWindow = false)
        {
            LastHitFromFront = IsFrontal(attackOrigin, attacker);
            if (LastHitFromFront)
            {
                // 盾反期间（跪地 + 冲撞，含收招起身段）：正面全免——不结算伤害、不进受击（攻击方据此不施加击退）。
                // 受击反馈（纯视觉、无结算）：接触点火花 + 格挡震屏 + 闪光（闪白暂留，待替代方案定型）
                if (IsShieldStanceDefenseActive())
                {
                    SpawnBlockSpark(attackOrigin ?? (attacker as Node2D)?.GlobalPosition);
                    TriggerBlockShake();
                    FlashDamageEffect();
                    return false;
                }

                float keep = 1f - Mathf.Clamp(FrontalDamageReduction, 0f, 0.999f);
                damage = Mathf.Max(Mathf.RoundToInt(damage * keep), 1);
            }
            return base.TakeDamage(damage, attackOrigin, attacker, damageSource, bypassMergeWindow);
        }

        /// <summary>盾反防御是否生效中（ShieldStance 运行期间的前两段：Warmup 跪地 / Active 冲撞·收招起身）。
        /// Recovery（冲撞命中后的收势）不含——收势阶段恢复常规"减免+保底1点"。</summary>
        private bool IsShieldStanceDefenseActive()
        {
            var stance = ResolveShieldStance();
            return stance != null && stance.IsRunning
                && (stance.CurrentPhase == EnemyAttackTemplate.AttackPhase.Warmup
                    || stance.CurrentPhase == EnemyAttackTemplate.AttackPhase.Active);
        }

        private EnemyGuard5ShieldStanceAttack? ResolveShieldStance()
        {
            if (_shieldStance != null && GodotObject.IsInstanceValid(_shieldStance)) return _shieldStance;
            _shieldStance = GetNodeOrNull<EnemyGuard5ShieldStanceAttack>("StateMachine/Attack/AttackController/ShieldStance");
            return _shieldStance;
        }

        /// <summary>格挡震屏：与"打中敌人/受伤"震屏同源——直接调 <see cref="CameraFollow.Shake"/>（镜头公开入口），
        /// 强度取相机的"打中敌人"震屏配置（不在此写死数值；无相机时静默跳过）。
        /// 不借 AnyDamageTaken 事件广播——那个事件还挂着飘字/伤害统计等订阅者，格挡不应产生伤害语义。</summary>
        private void TriggerBlockShake()
        {
            if (GetViewport()?.GetCamera2D() is CameraFollow cam)
            {
                cam.Shake(cam.PlayerAttackHitShakeStrength);
            }
        }

        /// <summary>无敌格挡火花：在盾面朝攻击来源方向生成一次性火花（节点朝向 = 来源方向，粒子锥沿此方向向外喷）。
        /// 位置 = 视觉锚点（GetVisualAnchorWorld，身上特效的规范入口）+ 盾面偏移；来源拿不到时按自身朝向前方回退。
        /// 生成到敌人的父节点（世界侧），特效自管理销毁（FollowAndDestroy），不随敌人动画/状态变化。</summary>
        private void SpawnBlockSpark(Vector2? sourcePosition)
        {
            var parent = GetParent();
            if (parent == null) return;

            var scene = GD.Load<PackedScene>(BlockSparkScenePath);
            if (scene == null) return;

            Vector2 anchor = GetVisualAnchorWorld();
            // 喷溅方向用"脚底对脚底"的地面方向（视觉锚点比脚底高，用锚点算方向会让喷溅上仰）；
            // 落点仍用胸口锚点 + 盾面偏移
            Vector2 dir = sourcePosition.HasValue ? sourcePosition.Value - GlobalPosition : Vector2.Zero;
            if (dir.LengthSquared() < 0.0001f)
            {
                dir = FacingRight ? Vector2.Right : Vector2.Left;
            }
            dir = dir.Normalized();

            var fx = scene.Instantiate<Node2D>();
            parent.AddChild(fx);
            fx.GlobalPosition = anchor + dir * ResolveShieldFrontOffset();
            fx.Rotation = dir.Angle();
        }

        /// <summary>盾面偏移（火花落点离视觉锚点的距离）：取自身受击区矩形半宽的世界尺度——
        /// 火花出现在"攻击真正接触的盾沿"附近；拿不到受击区时回退常量。</summary>
        private float ResolveShieldFrontOffset()
        {
            var hitArea = GetSelfHitArea();
            var shapeNode = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (hitArea != null && shapeNode?.Shape is RectangleShape2D rect)
            {
                return rect.Size.X * 0.5f * Mathf.Abs(hitArea.GlobalScale.X);
            }
            return BlockSparkFallbackOffset;
        }

        private const string BlockSparkScenePath = "res://resources/effects/fire_hit.tscn";
        private const float BlockSparkFallbackOffset = 60f;

        /// <summary>正面 180°：来源相对朝向的横向半球；正上/正下（dx≈0）或拿不到来源 → 判不了 → 按非正面（全额）。</summary>
        private bool IsFrontal(Vector2? attackOrigin, GameActor? attacker)
        {
            Vector2? src = attackOrigin ?? (attacker as Node2D)?.GlobalPosition;
            if (src is not Vector2 p) return false;
            float dx = p.X - GlobalPosition.X;
            return FacingRight ? dx > 0f : dx < 0f;
        }
    }
}
