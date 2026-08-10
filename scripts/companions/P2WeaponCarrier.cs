using System;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Companions
{
    /// <summary>
    /// P2 武器搬运工：自动前往拾取范围内的武器 → 挂到 P2 的 Spine 骨骼上（单持有槽，无背包）→
    /// 拖拽回玩家身边 → 在玩家旁生成世界武器实体（玩家自行拾取）。
    /// 状态机：Idle → GoingToWeapon（前往）→ Returning（拖回）→ 放置 → Idle。
    /// </summary>
    [GlobalClass]
    public partial class P2WeaponCarrier : Node
    {
        /// <summary>武器放置完成时触发（Brain 据此开始拾取 CD）。</summary>
        [Signal] public delegate void WeaponPlacedEventHandler();

        [Export] public NodePath CompanionControllerPath { get; set; } = new("..");
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        /// <summary>武器挂载的 Spine 骨骼节点（P2.tscn 的 SpineSprite/SpineBoneNode，bone5）。
        /// 相对本节点（Carrier 是 P2 子节点，需 ../ 回到 P2 根再进入 SpineSprite）。</summary>
        [Export] public NodePath BonePath { get; set; } = new("../SpineSprite/SpineBoneNode");
        /// <summary>持有视觉相对骨骼的微调偏移（骨骼自带旋转/位移）。</summary>
        [Export] public Vector2 WeaponHoldOffset { get; set; } = Vector2.Zero;
        /// <summary>放置锚点（P2 自身的 Marker2D "ItemDrop"）：武器生成在锚点全局位置。</summary>
        [Export] public NodePath ItemDropAnchorPath { get; set; } = new("ItemDrop");
        /// <summary>回退放置偏移：锚点缺失时用 玩家位置 + 此偏移（参考玩家 Drop (32,0)）。</summary>
        [Export] public Vector2 DropOffset { get; set; } = new(32f, 0f);
        /// <summary>拾取检测范围（px）：此范围内的可拾取武器才响应。</summary>
        [Export(PropertyHint.Range, "100,3000,50")] public float CarryRange { get; set; } = 2000f;
        /// <summary>忽略范围（px）：武器距玩家小于此值时不拾取（玩家自己会捡，避免反复拾取/放置循环）。</summary>
        [Export(PropertyHint.Range, "100,1000,50")] public float CarryRangeMin { get; set; } = 400f;

        private enum CarrierState { Idle, GoingToWeapon, Returning }

        private CarrierState _state = CarrierState.Idle;
        private P2CompanionController? _controller;
        private SamplePlayer? _player;
        private Node2D? _targetWeapon;      // 目标武器世界实体
        private ItemDefinition? _heldItem;  // 单持有槽（无背包）
        private int _heldQuantity;
        private Node2D? _heldVisual;        // 骨骼上的持有视觉（HoldScene 实例或 Icon Sprite）

        /// <summary>当前是否持有武器。</summary>
        public bool IsCarrying => _heldItem != null;
        /// <summary>是否正在执行拾取/拖拽流程。</summary>
        public bool IsBusy => _state != CarrierState.Idle;

        public override void _Ready()
        {
            ResolveDependencies();
        }

        public override void _Process(double delta)
        {
            switch (_state)
            {
                case CarrierState.GoingToWeapon:
                    UpdateGoingToWeapon();
                    break;
                case CarrierState.Returning:
                    UpdateReturning();
                    break;
            }
        }

        /// <summary>尝试拾取范围内最近的武器。失败返回 false（无目标/执行中/已有持有）。</summary>
        public bool TryFetchNearestWeapon()
        {
            if (IsBusy || IsCarrying || _controller == null) return false;

            var weapon = FindNearestWeapon();
            if (weapon == null) return false;

            _targetWeapon = weapon;
            _controller.IgnoreMoveRange = true; // 拾取流程期间忽略移动范围约束，不被拉回打断
            _controller.SetMoveTarget(weapon.GlobalPosition);
            _state = CarrierState.GoingToWeapon;
            return true;
        }

        /// <summary>取消当前流程（受击/打断时调用），还原为 Idle 并恢复移动范围约束。</summary>
        public void Cancel()
        {
            if (_heldVisual != null && IsInstanceValid(_heldVisual))
                _heldVisual.QueueFree();
            _heldVisual = null;
            _heldItem = null;
            _heldQuantity = 0;
            _targetWeapon = null;
            _state = CarrierState.Idle;
            if (_controller != null)
            {
                _controller.IgnoreMoveRange = false;
                _controller.StopMoving();
            }
        }

        // ── 状态推进 ──────────────────────────────────────────

        private void UpdateGoingToWeapon()
        {
            if (_controller == null || _targetWeapon == null || !IsInstanceValid(_targetWeapon))
            {
                Cancel();
                return;
            }

            // 每帧重新设置目标（拾取期间 IgnoreMoveRange 已忽略范围约束，目标不会被清空，此处仅为保险）
            _controller.SetMoveTarget(_targetWeapon.GlobalPosition);

            // 到达武器（ArriveDistance 内）→ 拾取并挂到骨骼
            if (_controller.GlobalPosition.DistanceTo(_targetWeapon.GlobalPosition) <= _controller.ArriveDistance)
            {
                PickupWeapon(_targetWeapon);
            }
        }

        private void PickupWeapon(Node2D weaponEntity)
        {
            if (!TryReadItem(weaponEntity, out ItemDefinition? def, out int quantity))
            {
                Cancel();
                return;
            }

            _heldItem = def;
            _heldQuantity = quantity;
            if (_heldItem == null)
            {
                Cancel();
                return;
            }

            weaponEntity.QueueFree(); // 世界实体消失（被 P2 拿起）
            ShowHeldVisual();

            // 拖回玩家身边
            _state = CarrierState.Returning;
            if (_player != null)
                _controller?.SetMoveTarget(_player.GlobalPosition);
        }

        private void UpdateReturning()
        {
            if (_controller == null || _player == null)
            {
                Cancel();
                return;
            }

            // 每帧把拖回目标更新为玩家当前位置（玩家移动时实时追踪）。
            // Controller 的范围约束（超 MoveRangeMax 清目标拉回）会与本目标竞争，
            // 但下一帧此处重新设置目标 → 自愈；不受约束影响地持续追踪玩家
            _controller.SetMoveTarget(_player.GlobalPosition);

            // 到达玩家身边（FollowRangeMin 内）→ 放置
            if (_controller.GlobalPosition.DistanceTo(_player.GlobalPosition) <= _controller.FollowRangeMin)
            {
                PlaceWeapon();
            }
        }

        private void PlaceWeapon()
        {
            if (_heldItem == null)
            {
                Cancel();
                return;
            }

            // 在 P2 的 ItemDrop 锚点位置生成世界武器实体（玩家从该处自行拾取）
            var stack = new InventoryItemStack(_heldItem, _heldQuantity);
            WorldItemSpawner.SpawnFromStack(this, stack, GetDropPosition());

            HideHeldVisual();
            _heldItem = null;
            _heldQuantity = 0;
            _state = CarrierState.Idle;
            if (_controller != null)
            {
                _controller.IgnoreMoveRange = false; // 放下武器后恢复移动范围约束
                _controller.StopMoving();
            }

            EmitSignal(SignalName.WeaponPlaced); // 放置完成：Brain 据此开始拾取 CD
        }

        /// <summary>放置位置：优先 P2 的 ItemDrop 锚点全局位置；锚点缺失回退 玩家位置 + DropOffset。</summary>
        private Vector2 GetDropPosition()
        {
            var anchor = GetNodeOrNull<Node2D>(ItemDropAnchorPath);
            if (anchor != null)
                return anchor.GlobalPosition;
            return _player != null ? _player.GlobalPosition + DropOffset
                : (_controller?.GlobalPosition ?? Vector2.Zero);
        }

        // ── 骨骼挂载 ──────────────────────────────────────────

        /// <summary>按武器 HoldScenePath 实例化（或 Icon 纹理回退）挂到 Spine 骨骼节点，仿玩家 PlayerItemAttachment。</summary>
        private void ShowHeldVisual()
        {
            if (_heldItem == null) return;
            var bone = GetNodeOrNull<Node2D>(BonePath);
            if (bone == null) return;

            Node2D? visual = null;

            if (!string.IsNullOrWhiteSpace(_heldItem.HoldScenePath))
            {
                var scene = ResourceLoader.Load<PackedScene>(_heldItem.HoldScenePath);
                if (scene != null)
                    visual = scene.Instantiate<Node2D>();
            }

            if (visual == null && _heldItem.Icon != null)
            {
                var sprite = new Sprite2D { Texture = _heldItem.Icon };
                visual = sprite;
            }

            if (visual == null) return;

            bone.AddChild(visual);
            visual.Position = WeaponHoldOffset;
            _heldVisual = visual;
        }

        private void HideHeldVisual()
        {
            if (_heldVisual != null && IsInstanceValid(_heldVisual))
                _heldVisual.QueueFree();
            _heldVisual = null;
        }

        // ── 目标查找 ──────────────────────────────────────────

        /// <summary>查找拾取目标武器：P2 距离 ≤ CarryRange、武器距玩家 ≥ CarryRangeMin（玩家附近的武器留给玩家自己捡），
        /// 取范围内最远的武器（优先搬运远距离掉落）。</summary>
        private Node2D? FindNearestWeapon()
        {
            if (_controller == null) return null;
            Node2D? target = null;
            float best = float.MinValue;

            foreach (Node node in GetTree().GetNodesInGroup("world_items"))
            {
                if (node is not Node2D node2D || !IsInstanceValid(node2D)) continue;
                // 两类实体都支持：非投掷武器 WorldItemEntity（CharacterBody2D）与投掷武器
                // RigidBodyWorldItemEntity（Node2D，RigidBody2D 包装）——并非继承关系，需分别读取
                if (!TryReadItem(node2D, out ItemDefinition? def, out _)) continue;
                if (def == null) continue;
                if (!IsWeapon(def)) continue;

                // 范围约束：P2 可达范围内（钳制到移动范围，防选中走不到的目标）+ 距玩家不小于忽略范围
                float dP2 = _controller.GlobalPosition.DistanceTo(node2D.GlobalPosition);
                float reachableRange = Mathf.Min(CarryRange, _controller.MoveRangeMax);
                if (dP2 > reachableRange) continue;
                if (_player != null
                    && _player.GlobalPosition.DistanceTo(node2D.GlobalPosition) < CarryRangeMin)
                    continue;

                // 取最远
                if (dP2 > best)
                {
                    best = dP2;
                    target = node2D;
                }
            }

            return target;
        }

        /// <summary>读取世界物品实体的物品信息（WorldItemEntity 与 RigidBodyWorldItemEntity 两类，
        /// 它们都实现 IWorldItemEntity 但无继承关系，需分别处理）。</summary>
        private static bool TryReadItem(Node node, out ItemDefinition? definition, out int quantity)
        {
            if (node is WorldItemEntity world)
            {
                definition = world.ItemDefinition;
                quantity = world.Quantity;
                return true;
            }

            if (node is RigidBodyWorldItemEntity rigid)
            {
                definition = rigid.ItemDefinition;
                quantity = rigid.Quantity;
                return true;
            }

            definition = null;
            quantity = 0;
            return false;
        }

        private static bool IsWeapon(ItemDefinition def)
        {
            return def.IsThrowWeapon
                || string.Equals(def.Category, "Weapon", StringComparison.OrdinalIgnoreCase);
        }

        private void ResolveDependencies()
        {
            _controller ??= GetNodeOrNull<P2CompanionController>(CompanionControllerPath)
                ?? GetNodeOrNull<P2CompanionController>(NormalizeRelativePath(CompanionControllerPath));
            _player ??= GetNodeOrNull<SamplePlayer>(PlayerPath)
                ?? GetNodeOrNull<SamplePlayer>(NormalizeRelativePath(PlayerPath))
                ?? GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", StringComparison.Ordinal))
                return path;
            return new NodePath($"../{text}");
        }
    }
}
