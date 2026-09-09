using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items;

namespace Kuros.Builds.BuildCore
{
    /// <summary>
    /// Throw 核心机制：家具生成（充能制,串行逐格恢复）。
    /// OnApply 激活投掷指示器,按下核心技能键消耗 1 充能,在指示器位置生成一次性家具。
    /// 充能与冷却可被 build 修饰（RegisterChargeModifier 聚合;生成物替换 = 改 FurnitureScene）。
    /// </summary>
    [GlobalClass]
    public partial class ThrowCoreEffect : ActorEffect
    {
        [ExportCategory("Furniture")]
        /// <summary>小型乱码块家具场景(默认生成件)。</summary>
        [Export] public PackedScene? FurnitureScene { get; set; }
        /// <summary>中型乱码块家具场景(A_006 层 1 升级目标;暂空 = 回退小型)。</summary>
        [Export] public PackedScene? FurnitureSceneMedium { get; set; }
        /// <summary>大型乱码块家具场景(A_006 层 2 升级目标;暂空 = 回退上一档)。</summary>
        [Export] public PackedScene? FurnitureSceneLarge { get; set; }
        [ExportCategory("Charges")]
        /// <summary>每格充能恢复时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.5,60,0.5")] public float ChargeCooldown = 10f;
        /// <summary>最大充能数（默认 1 = 单次生成 + 冷却,与旧行为等价）。</summary>
        [Export(PropertyHint.Range, "1,8,1")] public int MaxCharges = 1;
        /// <summary>家具生成位置校准边距（像素,用于避免生成时与玩家碰撞）。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public float PlacementMargin = 16f;

        /// <summary>本效果生成家具的组:长按 F 销毁 / 追踪用(离树自动移除)。</summary>
        public const string ThrowCoreFurnitureGroup = Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCorePieceTag;

        // ── 生成聚合运行字段(BuildThrow 卡写入,ThrowCoreEffect 生成时单点读取)──
        /// <summary>生成件档位覆盖(0=默认 FurnitureScene;2/3=从该档家具池随机一件生成,由 A_006 写入)。</summary>
        public int SpawnTierOverride { get; set; }
        /// <summary>复制半径(0=关闭):生成点此半径内存在其它家具实体时复制最近一件(由 A_007 写入,默认 250)。</summary>
        public float CopyNearbyFurnitureRange { get; set; }

        /// <summary>当前可用充能数（HUD 读取）。</summary>
        public int ReadyCharges { get; private set; }
        /// <summary>是否正在恢复充能（ReadyCharges &lt; EffectiveMaxCharges）。</summary>
        public bool Charging => ReadyCharges < EffectiveMaxCharges;
        /// <summary>当前恢复进度 0-1（串行:同一时间只有一格在恢复）。</summary>
        public float ChargingProgress => EffectiveChargeCooldown > 0f
            ? Mathf.Clamp(_rechargeTimer / EffectiveChargeCooldown, 0f, 1f)
            : 0f;
        public bool CanSpawn => ReadyCharges > 0 && FurnitureScene != null;

        /// <summary>生成的家具图标（HUD 充能格显示;取 FurnitureScene 根节点 ItemDefinition.Icon,缓存）。</summary>
        public Texture2D? FurnitureIcon
        {
            get
            {
                if (_furnitureIconCached) return _furnitureIcon;
                _furnitureIconCached = true;
                if (FurnitureScene == null) return null;
                var root = FurnitureScene.Instantiate<Node>();
                try
                {
                    var itemDef = root.Get("ItemDefinition").As<ItemDefinition>();
                    _furnitureIcon = itemDef?.Icon;
                }
                catch
                {
                    _furnitureIcon = null;
                }
                finally
                {
                    root.Free(); // 未入树节点直接释放(不经 QueueFree)
                }
                return _furnitureIcon;
            }
        }

        private Texture2D? _furnitureIcon;
        private bool _furnitureIconCached;
        private float _rechargeTimer;
        private bool _indicatorEnabled;
        private readonly Dictionary<string, (int flatCharges, float cdMultiplier)> _chargeModifiers = new();

        /// <summary>有效最大充能 = MaxCharges + Σflat（build 修饰聚合,钳 ≥1）。</summary>
        public int EffectiveMaxCharges
        {
            get
            {
                int total = MaxCharges;
                foreach (var (flat, _) in _chargeModifiers.Values)
                    total += flat;
                return Mathf.Max(1, total);
            }
        }

        /// <summary>有效恢复时长 = ChargeCooldown × Πmultiplier（build 修饰聚合,钳 ≥0.1）。</summary>
        public float EffectiveChargeCooldown
        {
            get
            {
                float result = ChargeCooldown;
                foreach (var (_, mult) in _chargeModifiers.Values)
                    result *= mult;
                return Mathf.Max(0.1f, result);
            }
        }

        /// <summary>build 修饰注册（如"充能+1""冷却-20%"卡）;id 用于幂等注册/移除。</summary>
        public void RegisterChargeModifier(string id, int flatCharges, float cdMultiplier)
        {
            _chargeModifiers[id] = (flatCharges, Mathf.Max(0.1f, cdMultiplier));
            _rechargeTimer = Mathf.Min(_rechargeTimer, EffectiveChargeCooldown);
        }

        public void UnregisterChargeModifier(string id)
        {
            _chargeModifiers.Remove(id);
            _rechargeTimer = Mathf.Min(_rechargeTimer, EffectiveChargeCooldown);
        }

        /// <summary>推进当前充能恢复(A_005 等"命中减生成CD"用):把恢复进度**提前** seconds 秒
        /// (计时器前进,满一格才补),串行逐格。未在恢复中(全满)不生效。
        /// 注意方向:减 CD = 进度加快 = 计时器前进;旧实现用 -= 并越界即补格,
        /// 导致单格核心任意一击立刻补满整格(误报"瞬间刷完CD")。</summary>
        public void ReduceRecharge(float seconds)
        {
            if (seconds <= 0f || ReadyCharges >= EffectiveMaxCharges) return;

            _rechargeTimer += seconds;
            float cd = EffectiveChargeCooldown;
            while (_rechargeTimer >= cd && ReadyCharges < EffectiveMaxCharges)
            {
                _rechargeTimer -= cd;
                ReadyCharges++;
            }
            if (ReadyCharges >= EffectiveMaxCharges)
                _rechargeTimer = 0f;
        }

        protected override void OnApply()
        {
            ReadyCharges = EffectiveMaxCharges;
            _rechargeTimer = 0f;
            _indicatorEnabled = true;
            GetMainCharacter()?.EnableThrowIndicator(true);
        }

        protected override void OnTick(double delta)
        {
            // 串行逐格恢复：单计时器,满一格补一格（溢出丢弃）
            if (ReadyCharges < EffectiveMaxCharges)
            {
                _rechargeTimer += (float)delta;
                float cd = EffectiveChargeCooldown;
                while (ReadyCharges < EffectiveMaxCharges && _rechargeTimer >= cd)
                {
                    _rechargeTimer -= cd;
                    ReadyCharges++;
                }
            }
            else
            {
                _rechargeTimer = 0f;
            }

            // 输入走玩家 InputHoldTracker 的长短按语义（阈值 = GameSettings HoldThresholdSeconds）:
            // 短按(松开 < 阈值) → 消耗 1 充能生成;长按(按住 ≥ 阈值) → 销毁本效果生成的家具
            var player = GetMainCharacter();
            if (player == null || !GodotObject.IsInstanceValid(player)) return;

            if (player.WasActionShortPressed(Kuros.Core.InputActions.CoreSkill))
                TrySpawnOnce();

            if (player.WasActionLongPressTriggered(Kuros.Core.InputActions.CoreSkill))
                DestroyAllGeneratedFurniture();
        }

        private void TrySpawnOnce()
        {
            if (!CanSpawn) return;
            SpawnFurniture();
            ReadyCharges--;
        }

        /// <summary>长按 F:触发场上本效果生成家具的**正常销毁流程**(OnThrowDestroy 特效/掉落链,非直接
        /// QueueFree)。飞行/回弹/销毁中的实体由实体侧 RequestDestroy 自行忽略;非实体节点兜底 QueueFree。</summary>
        private void DestroyAllGeneratedFurniture()
        {
            var mc = GetMainCharacter();
            if (mc == null || !GodotObject.IsInstanceValid(mc)) return;

            foreach (Node node in GetTree().GetNodesInGroup(ThrowCoreFurnitureGroup))
            {
                if (node == null || !GodotObject.IsInstanceValid(node)) continue;
                if (node is Kuros.Items.World.RigidBodyWorldItemEntity rigidBody)
                    rigidBody.RequestDestroy();
                else
                    node.QueueFree();
            }
        }

        private void SpawnFurniture()
        {
            var mc = GetMainCharacter();
            if (mc == null) return;

            var indicator = mc.GetThrowIndicatorNode();
            Vector2 spawnPos = indicator != null && IsInstanceValid(indicator)
                ? ((Node2D)indicator).GlobalPosition
                : mc.GlobalPosition;

            var scene = ResolveSpawnScene(out bool isCopy);
            if (scene == null) return;

            var furniture = scene.Instantiate<Node2D>();
            // 进换关清场组(换关残留清理) + 本效果专属组(长按 F 销毁)
            furniture.AddToGroup(Kuros.Items.World.WorldItemSpawner.StageWorldItemsGroup);
            furniture.AddToGroup(ThrowCoreFurnitureGroup);
            furniture.AddToGroup(Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCorePieceIdentityTag); // 件身份(摧毁爆炸;投掷/放置通用)
            furniture.SetMeta("throwcore_born_ms", Time.GetTicksMsec()); // A_004 误爆防护(刚生成不炸)
            if (isCopy)
                furniture.AddToGroup(Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCoreCopyTag); // 复制身份(拾取→放置恢复滤镜)
            mc.GetParent()?.AddChild(furniture);
            // A_007 复制件整件乱码滤镜(视觉策略独立于生成管线,见 PieceCopyGlitchDecorator)
            if (isCopy)
                Kuros.Builds.Throw.PieceCopyGlitchDecorator.Apply(furniture);
            furniture.GlobalPosition = spawnPos;

            // 读取家具碰撞形状，沿朝向校准位置：Player.X + FacingSign * (半宽 + margin)
            var shape = FindFirstCollisionShape(furniture);
            if (shape != null)
            {
                float halfWidth = GetCollisionHalfWidth(shape) + PlacementMargin;
                float sign = mc.FacingRight ? 1f : -1f;
                furniture.GlobalPosition = new Vector2(
                    mc.GlobalPosition.X + sign * halfWidth,
                    spawnPos.Y);
            }
        }

        // ═══════════════════════════ 生成场景聚合(BuildThrow 卡驱动) ═══════════════════════════
        // 优先级:A_007 复制玩家当前高亮家具 > A_006 升级档(中型/大型 export) > 默认 FurnitureScene(小型)
        private PackedScene? ResolveSpawnScene(out bool isCopy)
        {
            isCopy = false;
            if (CopyNearbyFurnitureRange > 0f)
            {
                var highlighted = FindHighlightedFurnitureDefinition();
                if (highlighted != null)
                {
                    isCopy = true;
                    return LoadFurnitureScene(highlighted);
                }
            }

            if (SpawnTierOverride == 2)
                return FurnitureSceneMedium ?? FurnitureScene;
            if (SpawnTierOverride == 3)
                return FurnitureSceneLarge ?? FurnitureSceneMedium ?? FurnitureScene;

            return FurnitureScene;
        }

        private static readonly System.Collections.Generic.Dictionary<string, PackedScene> SceneCache = new();

        private static PackedScene? LoadFurnitureScene(ItemDefinition def)
        {
            if (def == null) return null;
            string path = def.ResolveWorldScenePath();
            if (string.IsNullOrEmpty(path)) return null;
            if (!SceneCache.TryGetValue(path, out var scene))
            {
                scene = ResourceLoader.Load<PackedScene>(path);
                if (scene != null)
                    SceneCache[path] = scene;
            }
            return scene;
        }

        /// <summary>
        /// A_007 复制目标 = 玩家当前高亮(描边)的家具(PlayerItemInteractionComponent 每帧仲裁,
        /// 与 UI 拾取提示同源:只有玩家 GrabArea 重叠范围内最近的一件会高亮)。
        /// 排除本效果生成的件(防复制链);无高亮 → null(生成普通乱码块)。
        /// </summary>
        private ItemDefinition? FindHighlightedFurnitureDefinition()
        {
            var highlighted = Kuros.Items.World.RigidBodyWorldItemEntity.CurrentHighlightedEntity;
            if (highlighted == null || !GodotObject.IsInstanceValid(highlighted)) return null;
            if (highlighted.IsInGroup(ThrowCoreFurnitureGroup)) return null;
            var def = highlighted.ItemDefinition;
            return def != null && def.IsFurniture ? def : null;
        }

        private static CollisionShape2D? FindFirstCollisionShape(Node2D root)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is CollisionShape2D shape)
                    return shape;
                if (child is Node2D childNode)
                {
                    var found = FindFirstCollisionShape(childNode);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static float GetCollisionHalfWidth(CollisionShape2D shape)
        {
            if (shape?.Shape == null) return 32f;
            if (shape.Shape is RectangleShape2D rect) return rect.Size.X * 0.5f;
            if (shape.Shape is CircleShape2D circle) return circle.Radius;
            return 32f;
        }

        public override void OnRemoved()
        {
            if (_indicatorEnabled)
                GetMainCharacter()?.EnableThrowIndicator(false);
            base.OnRemoved();
        }

        private MainCharacter? GetMainCharacter()
        {
            return Actor as MainCharacter;
        }
    }
}
