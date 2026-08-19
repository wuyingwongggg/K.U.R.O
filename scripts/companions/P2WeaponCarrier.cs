using System;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Companions
{
    /// <summary>
    /// P2 武器搬运工：将"前往武器 → 拾取 → 返回 → 放置"拆分为可自由组合的步骤，
    /// 在关键决策点发出信号（WeaponTargetReached / WeaponPickedUp / WeaponPlaced），
    /// 由 AI（AI_Brain）同步判断"该武器是否为玩家需要"，可改向（换下一把）或中途原地放置。
    /// 无外部决策时按默认流程自动完成（到达→拾取→返回→放置），保持向后兼容。
    /// </summary>
    [GlobalClass]
    public partial class P2WeaponCarrier : Node
    {
        /// <summary>到达目标武器时触发（参数 = 武器物品定义）。AI 在此判断是否拾取。</summary>
        [Signal] public delegate void WeaponTargetReachedEventHandler(ItemDefinition item);
        /// <summary>拾取并挂载到骨骼后触发（参数 = 武器物品定义）。AI 在此判断返回或原地放置。</summary>
        [Signal] public delegate void WeaponPickedUpEventHandler(ItemDefinition item);
        /// <summary>武器放置完成时触发（参数 = 武器物品定义）。Brain 据此开始拾取 CD。</summary>
        [Signal] public delegate void WeaponPlacedEventHandler(ItemDefinition item);

        /// <summary>搬运步骤（可自由组合，外部通过公开方法驱动）。</summary>
        public enum CarrierStep { Idle, GoingToWeapon, PickedUp, Returning }

        [Export] public NodePath CompanionControllerPath { get; set; } = new("..");
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        /// <summary>武器挂载的 Spine 骨骼节点（P2.tscn 的 SpineSprite/SpineBoneNode，bone5）。
        /// 相对本节点（Carrier 是 P2 子节点，需 ../ 回到 P2 根再进入 SpineSprite）。</summary>
        [Export] public NodePath BonePath { get; set; } = new("../SpineSprite/SpineBoneNode");
        /// <summary>持有视觉相对骨骼的微调偏移（骨骼自带旋转/位移）。</summary>
        [Export] public Vector2 WeaponHoldOffset { get; set; } = Vector2.Zero;
        /// <summary>放置锚点（P2 自身的 Marker2D "Anchor_ItemDrop"）：武器生成在锚点全局位置。</summary>
        [Export] public NodePath ItemDropAnchorPath { get; set; } = new("Anchor_ItemDrop");
        /// <summary>回退放置偏移：锚点缺失时用 玩家位置 + 此偏移（参考玩家 Drop (32,0)）。</summary>
        [Export] public Vector2 DropOffset { get; set; } = new(32f, 0f);
        /// <summary>拾取检测范围（px）：此范围内的可拾取武器才响应。</summary>
        [Export(PropertyHint.Range, "100,3000,50")] public float CarryRange { get; set; } = 2000f;
        /// <summary>忽略范围（px）：武器距玩家小于此值时不拾取（玩家自己会捡，避免反复拾取/放置循环）。</summary>
        [Export(PropertyHint.Range, "100,1000,50")] public float CarryRangeMin { get; set; } = 400f;

        private CarrierStep _step = CarrierStep.Idle;
        private P2CompanionController? _controller;
        private SamplePlayer? _player;
        private Node2D? _targetWeapon;      // 目标武器世界实体
        private ItemDefinition? _targetItem; // 目标武器物品定义（决策点参数）
        private ItemDefinition? _heldItem;  // 单持有槽（无背包）
        private int _heldQuantity;
        private Node2D? _heldVisual;        // 骨骼上的持有视觉（HoldScene 实例或 Icon Sprite）
        private bool _targetReachedNotified; // 当前目标是否已发"到达"通知（防重复）

        /// <summary>当前搬运步骤。</summary>
        public CarrierStep CurrentStep => _step;
        /// <summary>当前是否持有武器。</summary>
        public bool IsCarrying => _heldItem != null;
        /// <summary>是否正在执行拾取/拖拽流程。</summary>
        public bool IsBusy => _step != CarrierStep.Idle;

        public override void _Ready()
        {
            ResolveDependencies();
        }

        public override void _Process(double delta)
        {
            // 玩家死亡：取消搬运流程（放下持有物恢复约束，回 Idle；守尸由 Controller 接管移动）
            if (_step != CarrierStep.Idle && _player != null
                && (_player.IsDeathSequenceActive || _player.IsDead))
            {
                Cancel();
                return;
            }

            switch (_step)
            {
                case CarrierStep.GoingToWeapon:
                    UpdateGoingToWeapon();
                    break;
                case CarrierStep.PickedUp:
                    UpdatePickedUp();
                    break;
                case CarrierStep.Returning:
                    UpdateReturning();
                    break;
            }
        }

        // ── 步骤控制接口（AI 可自由组合调用） ─────────────────────

        /// <summary>搬运专用移动速度（px/秒）：拾取/拖拽期间覆盖 P2 的模式速度（跟随/游走）；0 = 不覆盖。</summary>
        [Export(PropertyHint.Range, "0,3000,10")] public float CarrySpeed { get; set; } = 450f;

        /// <summary>开始前往范围内最近的武器（目标选择含最远优先/范围过滤）。失败返回 false。</summary>
        public bool StartFetchNearestWeapon()
        {
            if (IsBusy || IsCarrying || _controller == null) return false;

            var weapon = FindNearestWeapon();
            if (weapon == null) return false;

            StartFetchWeapon(weapon);
            return true;
        }

        /// <summary>前往指定武器实体（AI 指定目标时用）。</summary>
        public void StartFetchWeapon(Node2D weaponEntity)
        {
            if (weaponEntity == null || _controller == null) return;

            _targetWeapon = weaponEntity;
            _targetItem = ReadItem(weaponEntity);
            _targetReachedNotified = false;
            _controller.IgnoreMoveRange = true; // 拾取流程期间忽略移动范围约束/空气墙，不被打断
            _controller.MoveSpeedOverride = CarrySpeed > 0f ? CarrySpeed : null; // 搬运专用速度（独立于跟随/游走模式）
            _controller.SetMoveTarget(weaponEntity.GlobalPosition);
            _step = CarrierStep.GoingToWeapon;
        }

        /// <summary>放弃当前目标并自动查找下一把（GoTo 阶段 AI 判断"不需要"时调用）。无下一把则回 Idle。</summary>
        public void AbortAndFindNext()
        {
            if (_targetWeapon == null)
            {
                FinishToIdle();
                return;
            }

            // 排除当前目标，重新查找（最远优先 + 范围过滤）
            var current = _targetWeapon;
            _targetWeapon = null;
            _targetItem = null;
            var next = FindNearestWeapon(exclude: current);
            if (next == null)
            {
                FinishToIdle();
                return;
            }

            StartFetchWeapon(next);
        }

        /// <summary>拾取当前目标并挂载到骨骼，停在 PickedUp 步骤（AI 在 WeaponPickedUp 事件中决定下一步）。</summary>
        public void PickupAndHold()
        {
            if (_step != CarrierStep.GoingToWeapon || _targetWeapon == null) return;
            PickupWeaponInternal(_targetWeapon);
        }

        /// <summary>开始拖回玩家位置（PickedUp 步骤 AI 判断"需要"时调用）。</summary>
        public void ReturnToPlayer()
        {
            if (_step != CarrierStep.PickedUp) return;
            _step = CarrierStep.Returning;
            if (_player != null)
                _controller?.SetMoveTarget(_player.GlobalPosition);
        }

        /// <summary>原地放置（PickedUp/Returning 步骤 AI 判断"不需要"时调用）：在 P2 当前位置生成武器实体。</summary>
        public void PlaceAtCurrent()
        {
            if (_heldItem == null)
            {
                FinishToIdle();
                return;
            }

            PlaceWeaponAt(_controller?.GlobalPosition ?? GetDropPosition());
        }

        /// <summary>在放置锚点（Anchor_ItemDrop）位置放置（返回玩家后的默认放置）。</summary>
        public void PlaceAtPlayer()
        {
            if (_heldItem == null)
            {
                FinishToIdle();
                return;
            }

            PlaceWeaponAt(GetDropPosition());
        }

        /// <summary>AI 查询：该武器是否为玩家需要（玩家已持有同 ID 武器 → 不需要，避免重复搬运）。</summary>
        public bool IsWeaponDesired(ItemDefinition item)
        {
            if (item == null) return false;
            if (_player?.InventoryComponent == null) return true; // 无背包信息：默认需要

            // 玩家快捷栏/背包已持有同 ID → 不需要
            var quickBar = _player.InventoryComponent.QuickBar;
            if (quickBar != null)
            {
                for (int i = 0; i < quickBar.Slots.Count; i++)
                {
                    var stack = quickBar.GetStack(i);
                    if (stack != null && !stack.IsEmpty && stack.Item?.ItemId == item.ItemId)
                        return false;
                }
            }

            var backpack = _player.InventoryComponent.Backpack;
            if (backpack != null)
            {
                for (int i = 0; i < backpack.Slots.Count; i++)
                {
                    var stack = backpack.GetStack(i);
                    if (stack != null && !stack.IsEmpty && stack.Item?.ItemId == item.ItemId)
                        return false;
                }
            }

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
            _targetItem = null;
            FinishToIdle();
        }

        // ── 内部状态推进 ──────────────────────────────────────────

        private void UpdateGoingToWeapon()
        {
            if (_controller == null || _targetWeapon == null || !IsInstanceValid(_targetWeapon))
            {
                Cancel();
                return;
            }

            // 每帧重新设置目标（拾取期间 IgnoreMoveRange 已忽略范围约束，目标不会被清空，仅为保险）
            _controller.SetMoveTarget(_targetWeapon.GlobalPosition);

            // 到达武器：发出决策事件，AI 同步响应（PickupAndHold / AbortAndFindNext）；
            // 无人响应时下一帧自动拾取（默认流程）
            if (_controller.GlobalPosition.DistanceTo(_targetWeapon.GlobalPosition) <= _controller.ArriveDistance)
            {
                if (!_targetReachedNotified)
                {
                    _targetReachedNotified = true;
                    EmitSignal(SignalName.WeaponTargetReached, _targetItem);
                    return; // 本帧停：等待 AI 决策
                }

                PickupAndHold();
            }
        }

        private void UpdatePickedUp()
        {
            // 无人响应 WeaponPickedUp 时下一帧默认返回玩家（默认流程）
            ReturnToPlayer();
        }

        private void UpdateReturning()
        {
            if (_controller == null || _player == null)
            {
                Cancel();
                return;
            }

            // 每帧把拖回目标更新为玩家当前位置（玩家移动时实时追踪，自愈范围约束竞争）
            _controller.SetMoveTarget(_player.GlobalPosition);

            // 到达玩家身边（FollowRangeMin 内）→ 放置
            if (_controller.GlobalPosition.DistanceTo(_player.GlobalPosition) <= _controller.FollowRangeMin)
            {
                PlaceAtPlayer();
            }
        }

        private void PickupWeaponInternal(Node2D weaponEntity)
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
            _targetWeapon = null;
            _targetItem = null;
            _step = CarrierStep.PickedUp;

            // 发出拾取决策事件，AI 同步响应（ReturnToPlayer / PlaceAtCurrent）；
            // 无人响应时 UpdatePickedUp 下一帧自动返回
            EmitSignal(SignalName.WeaponPickedUp, _heldItem);
        }

        /// <summary>在世界指定位置生成武器实体并完成放置（清持有/恢复约束/发 WeaponPlaced）。</summary>
        private void PlaceWeaponAt(Vector2 worldPosition)
        {
            if (_heldItem == null)
            {
                FinishToIdle();
                return;
            }

            var stack = new InventoryItemStack(_heldItem, _heldQuantity);
            var placedItem = _heldItem;
            WorldItemSpawner.SpawnFromStack(this, stack, worldPosition);

            HideHeldVisual();
            _heldItem = null;
            _heldQuantity = 0;
            FinishToIdle();

            EmitSignal(SignalName.WeaponPlaced, placedItem); // 放置完成：Brain 据此开始拾取 CD
        }

        /// <summary>还原到 Idle 并恢复移动范围约束/搬运速度覆盖。</summary>
        private void FinishToIdle()
        {
            _step = CarrierStep.Idle;
            if (_controller != null)
            {
                _controller.IgnoreMoveRange = false;
                _controller.MoveSpeedOverride = null; // 结束搬运：速度还原为模式速度
                _controller.StopMoving();
            }
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

        /// <summary>查找拾取目标武器：P2 距离 ≤ min(CarryRange, MoveRangeMax)、距玩家 ≥ CarryRangeMin，
        /// 取范围内最远（优先搬运远处掉落）。exclude 用于"换下一把"时排除当前目标。</summary>
        private Node2D? FindNearestWeapon(Node2D? exclude = null)
        {
            if (_controller == null) return null;
            Node2D? target = null;
            float best = float.MinValue;

            foreach (Node node in GetTree().GetNodesInGroup("world_items"))
            {
                if (node is not Node2D node2D || !IsInstanceValid(node2D)) continue;
                if (exclude != null && node2D == exclude) continue;
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

        /// <summary>读取实体物品定义（决策点信号参数用；失败返回 null）。</summary>
        private static ItemDefinition? ReadItem(Node node)
        {
            return TryReadItem(node, out ItemDefinition? def, out _) ? def : null;
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
