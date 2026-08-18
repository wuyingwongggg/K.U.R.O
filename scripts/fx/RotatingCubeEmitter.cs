using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Fx
{
    /// <summary>
    /// 旋转立方体发射器。继承 RotatingCube 的瞄准追踪/单次命中伤害/击退/销毁特效行为，
    /// 在飞行途中额外做两件事（与原版 CubeData 的区别）：
    /// 1. 每隔 EmissionIntervalSeconds 朝上、下各发射一发抛射物——抛射物走 AttackEffectEntry
    ///    （与 EnemyAttackTemplate.Effects 同机制：场景 + 属性重载，数据驱动）；
    /// 2. 飞行期间整体缩放线性衰减到 0，直至直接消失（无销毁特效，静默 QueueFree）。
    /// 命中行为与原版一致：单次伤害 + DestroyEffect 粒子四散。
    /// </summary>
    public partial class RotatingCubeEmitter : RotatingCube
    {
        [ExportCategory("Emission")]
        /// <summary>上下发射抛射物的间隔（秒）。首个齐射在 spawn 构建动画结束一个间隔后发出。</summary>
        [Export(PropertyHint.Range, "0.05,10,0.05")] public float EmissionIntervalSeconds = 0.2f;
        /// <summary>抛射物特效条目（与 EnemyAttackTemplate.Effects 同机制：AttackEffectEntry = 场景 + 属性重载）。
        /// 每次发射时，每个条目在上、下两个方向各生成 1 个。</summary>
        [Export] public Godot.Collections.Array<AttackEffectEntry> EmissionEffects { get; set; } = new();
        /// <summary>发射点相对立方体中心的垂直偏移（像素），上下对称。</summary>
        [Export(PropertyHint.Range, "0,500,1")] public float EmissionOffset = 30f;

        [ExportCategory("Shrink")]
        /// <summary>飞行期间缩放线性衰减到 0 的时长（秒）。到期后立方体直接消失（无销毁特效）。
        /// 0 = 不缩小（与原版 CubeData 行为一致）。</summary>
        [Export(PropertyHint.Range, "0,30,0.05")] public float ShrinkLifetime = 0f;

        private float _elapsed;
        private float _emissionTimer;

        public override void _Ready()
        {
            base._Ready();
            _emissionTimer = EmissionIntervalSeconds;
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            // 命中或超时销毁时 base 已 QueueFree，本帧不再发射
            if (IsQueuedForDeletion()) return;

            float dt = (float)delta;
            _elapsed += dt;

            // spawn 构建动画期间不缩放、不发射（与 base 的飞行启动时机一致）
            if (_elapsed < BuildDuration) return;

            // ———— 飞行中缩放线性衰减，直至消失 ————
            if (ShrinkLifetime > 0f)
            {
                float t = Mathf.Min(1f, (_elapsed - BuildDuration) / ShrinkLifetime);
                Scale = new Vector2(BaseScale, BaseScale) * (1f - t);
                if (t >= 1f)
                {
                    QueueFree();
                    return;
                }
            }

            // Duration 到期进入 despawn 消解阶段后停止发射（避免消解动画期间继续吐弹）
            if (_elapsed >= Duration - DespawnDuration) return;

            _emissionTimer -= dt;
            if (_emissionTimer <= 0f)
            {
                _emissionTimer = Mathf.Max(0.05f, EmissionIntervalSeconds);
                EmitUpDown();
            }
        }

        /// <summary>
        /// 每个条目在上、下两个方向各生成一发抛射物。
        /// </summary>
        private void EmitUpDown()
        {
            if (EmissionEffects.Count == 0 || GetParent() == null) return;

            foreach (var entry in EmissionEffects)
            {
                if (entry == null || entry.Scene == null) continue;
                SpawnEmittedProjectile(entry, Vector2.Up);
                SpawnEmittedProjectile(entry, Vector2.Down);
            }
        }

        /// <summary>
        /// 生成单发抛射物：与 EnemyAttackTemplate.SpawnSingleEffect 同套路——
        /// 实例化 → AttackEffectEntry.ApplyOverrides 重载属性 → 指定固定方向/攻击来源 → 挂到父节点下。
        /// </summary>
        private void SpawnEmittedProjectile(AttackEffectEntry entry, Vector2 direction)
        {
            try
            {
                var instance = entry.Scene!.Instantiate();
                entry.ApplyOverrides(instance);

                if (instance is not Node2D node2D)
                {
                    instance.QueueFree();
                    return;
                }

                if (node2D is RotatingCube cube)
                {
                    // 固定方向跳过瞄准：直接朝上/下直飞（须在 AddChild 前设置，_Ready 读取）
                    cube.FixedDirection = direction;
                    // 攻击来源与母体一致（含同阵营伤害过滤）
                    cube.Attacker = Attacker;
                }

                GetParent()!.AddChild(node2D);
                node2D.GlobalPosition = GlobalPosition + direction * EmissionOffset;
            }
            catch (System.Exception ex)
            {
                GD.PushWarning($"[{Name}] 无法发射抛射物: {ex.Message}");
            }
        }
    }
}
