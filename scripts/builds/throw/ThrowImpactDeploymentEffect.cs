using System.Collections.Generic;
using Godot;
using Kuros.Core.Effects;
using Kuros.Fx;
using Kuros.Items.World;

namespace Kuros.Builds.Throw
{
    /// <summary>
    /// 冲击投放（BuildThrow_A_004）：任一乱码块(ThrowCore 件)被摧毁时,
    /// 在其位置生成一次 BoomDmgEffect —— 范围 AoE 伤害 + 击退,伤害随层(30/40/50)。
    /// 事件源: RigidBodyWorldItemEntity.Destroyed(在 QueueFree 之前触发,事件内件仍有效可读位置)。
    /// 伤害/击退逻辑完全复用 BoomDmgEffect;本效果只负责订阅、过滤、按层注入参数。
    /// 非本核心生成件(不在 throwcore 组)、离场清理(非 Destroy 路径)不触发。
    /// </summary>
    [GlobalClass]
    public partial class ThrowImpactDeploymentEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 30f, 40f, 50f };

        /// <summary>爆炸半径(px)。</summary>
        [Export(PropertyHint.Range, "50,800,10")] public float Radius { get; set; } = 380f;
        /// <summary>击退位移距离(px)。</summary>
        [Export(PropertyHint.Range, "0,1000,10")] public float KnockbackDistance { get; set; } = 420f;
        /// <summary>击退滑行时长(秒;沿用 Boom 场景默认 0.2,这里可统一调)。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.2f;
        /// <summary>冲击伤害载体场景(复用 BoomDmgEffect)。</summary>
        [Export] public PackedScene? BoomScene { get; set; }
        /// <summary>爆炸视觉效果场景(与伤害 Boom 同时在销毁点生成;为空则只有伤害无视觉)。</summary>
        [Export] public PackedScene? VisualScene { get; set; }
        /// <summary>冲击目标阵营(勾选;默认只炸敌人。BoomDmgEffect.tscn 里存的是 All,
        /// 会在实例化时被本参数覆盖——想炸世界物/玩家在此勾选)。</summary>
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public Core.TargetableFactions TargetableFactions { get; set; } = Core.TargetableFactions.Enemy;

        /// <summary>当前层(1 起)。注意:实例 MaxStacks 默认=1 会钳制 CurrentStacks,
        /// 因此与 A_001~A_003 一致用自计数 _tier,不读 CurrentStacks。</summary>
        private int _tier = 1;

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length);
        }

        protected override void OnApply()
        {
            RigidBodyWorldItemEntity.Destroyed += OnPieceDestroyed;
        }

        /// <summary>取件实际销毁位置:投掷飞行中 wrapper 根位置不同步(停在生成点≈玩家),
        /// 内部 RigidBody2D 才是真实位置。</summary>
        private static Vector2 ResolvePiecePosition(RigidBodyWorldItemEntity piece)
        {
            var body = piece.GetNodeOrNull<RigidBody2D>("RigidBody2D");
            if (body == null)
            {
                foreach (var child in piece.GetChildren())
                {
                    if (child is RigidBody2D rb) { body = rb; break; }
                }
            }
            return body != null && GodotObject.IsInstanceValid(body)
                ? body.GlobalPosition
                : piece.GlobalPosition;
        }

        /// <summary>在销毁点生成爆炸视觉(同 Boom 原点)。</summary>
        private void SpawnVisual(Vector2 position, Node host)
        {
            if (VisualScene == null || host == null) return;
            var visual = VisualScene.Instantiate<Node2D>();
            if (visual == null) return;
            host.AddChild(visual);
            visual.GlobalPosition = position;
        }

        public override void OnRemoved()
        {
            RigidBodyWorldItemEntity.Destroyed -= OnPieceDestroyed;
            base.OnRemoved();
        }

        /// <summary>误爆防护:生成后短时间内被销毁不算(如投掷出手瞬间碰撞),单位毫秒;0=关闭。</summary>
        [Export(PropertyHint.Range, "0,2000,10")] public int MinBornAgeMs { get; set; } = 200;
        /// <summary>跨实例/多事件去重:同一件短时间只炸一次。</summary>
        private static readonly Dictionary<ulong, ulong> RecentBlasts = new();

        private void OnPieceDestroyed(RigidBodyWorldItemEntity piece)
        {
            if (Actor == null || !GodotObject.IsInstanceValid(piece)) return;
            // 仅本核心"件":直接生成/放置件在脉冲组,投掷/恢复件在身份组
            if (!piece.IsInGroup(RigidBodyWorldItemEntity.ThrowCorePieceTag)
                && !piece.IsInGroup(RigidBodyWorldItemEntity.ThrowCorePieceIdentityTag))
                return;
            if (BoomScene == null) return;

            // 刚生成(<MinBornAgeMs)即被销毁 = 出手瞬间误爆(碰撞/落点贴脸),跳过
            ulong now = Time.GetTicksMsec();
            if (MinBornAgeMs > 0 && piece.HasMeta("throwcore_born_ms")
                && now - piece.GetMeta("throwcore_born_ms").As<ulong>() < (ulong)MinBornAgeMs)
                return;

            // 同件去重(同一销毁入口多次广播 / 多卡实例重复订阅)
            ulong id = piece.GetInstanceId();
            if (RecentBlasts.TryGetValue(id, out ulong last) && now - last < 400)
                return;
            RecentBlasts[id] = now;
            if (RecentBlasts.Count > 128)
            {
                foreach (var stale in new System.Collections.Generic.List<ulong>(RecentBlasts.Keys))
                {
                    if (now - RecentBlasts[stale] > 400)
                        RecentBlasts.Remove(stale);
                }
            }

            int damage = TierValues.Length > 0
                ? Mathf.RoundToInt(TierValues[Mathf.Clamp(_tier, 1, TierValues.Length) - 1])
                : 0;
            if (damage <= 0) return;

            var boom = BoomScene.Instantiate<Node2D>();
            if (boom is not BoomDmgEffect boomEffect)
            {
                boom.QueueFree();
                return;
            }

            // Attacker/参数必须在 AddChild(_Ready) 之前注入(与 Destroy() 内 OnThrowDestroy 注入同序)
            boomEffect.Attacker = Actor;
            boomEffect.Damage = damage;
            boomEffect.Radius = Radius;
            boomEffect.KnockbackDistance = KnockbackDistance;
            boomEffect.KnockbackDuration = KnockbackDuration;
            boomEffect.TargetableFactions = TargetableFactions;
            boomEffect.AllowSelfDamage = false;

            Node host = piece.GetParent() is Node parent ? parent : GetTree().CurrentScene;
            if (host == null)
            {
                boom.QueueFree();
                return;
            }
            Vector2 origin = ResolvePiecePosition(piece); // wrapper 与 RigidBody2D 位置不同步,取 body 实际位置
            host.AddChild(boom);
            boom.GlobalPosition = origin;
            SpawnVisual(origin, host);
        }
    }
}
