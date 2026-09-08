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
        [Export] public PackedScene? FurnitureScene { get; set; }
        [ExportCategory("Charges")]
        /// <summary>每格充能恢复时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.5,60,0.5")] public float ChargeCooldown = 10f;
        /// <summary>最大充能数（默认 1 = 单次生成 + 冷却,与旧行为等价）。</summary>
        [Export(PropertyHint.Range, "1,8,1")] public int MaxCharges = 1;
        /// <summary>家具生成位置校准边距（像素,用于避免生成时与玩家碰撞）。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public float PlacementMargin = 16f;

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
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsInstanceValid(this) || Actor == null) return;
            if (!@event.IsActionPressed(InputActions.CoreSkill) || @event.IsEcho()) return;
            if (!CanSpawn) return;

            SpawnFurniture();
            ReadyCharges--;
            GetViewport()?.SetInputAsHandled();
        }

        private void SpawnFurniture()
        {
            var mc = GetMainCharacter();
            if (mc == null || FurnitureScene == null) return;

            var indicator = mc.GetThrowIndicatorNode();
            Vector2 spawnPos = indicator != null && IsInstanceValid(indicator)
                ? ((Node2D)indicator).GlobalPosition
                : mc.GlobalPosition;

            var furniture = FurnitureScene.Instantiate<Node2D>();
            // 进换关清场组:核心技能生成的家具也是场上残留物,换关(Regenerate)时须一并清掉
            furniture.AddToGroup(Kuros.Items.World.WorldItemSpawner.StageWorldItemsGroup);
            mc.GetParent()?.AddChild(furniture);
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
