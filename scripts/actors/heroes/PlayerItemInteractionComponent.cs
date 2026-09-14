using System;
using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Systems.Inventory;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 负责处理玩家与背包物品之间的放置/投掷交互。
    /// </summary>
    public partial class PlayerItemInteractionComponent : Node
    {
        /// <summary>投掷出手事件(实体已生成并 ApplyThrowImpulse 后同步触发)。
        /// A_008 进程分叉等玩家侧构筑订阅;投掷者侧不做任何构筑逻辑,零耦合。</summary>
        public static event Action<RigidBodyWorldItemEntity>? PieceThrown;

        [Export(PropertyHint.Range, "0,200,1")]
        public float PlacementMargin = 16f;

        private enum DropDisposition
        {
            Place,
            Throw
        }

        [Export] public PlayerInventoryComponent? InventoryComponent { get; private set; }
        [Export] public Vector2 DropOffset = new Vector2(32, 0);
        [Export] public Vector2 ThrowOffset = new Vector2(48, -10);
        [Export(PropertyHint.Range, "0,2000,1")] public float ThrowImpulse = 800f;
        [Export] public bool EnableInput = true;
        [Export] public string ThrowStateName { get; set; } = "Throw";
        [Export] public NodePath? InteractionAreaPath { get; set; }
        [Export(PropertyHint.Range, "50,500,10")] public float PickupRange = 150f; // 拾取范围（像素）

        /// <summary>隔空抓取（BuildThrow_B_009）：>0 时拾取（含高亮）以**瞄准点（鼠标指针世界坐标）**为目标——
        /// 抓取瞄准点该半径内最近一件；瞄准点附近没有可拾取物时回落常规拾取。
        /// 由卡效果在 OnApply 置位、OnRemoved 清零（0 = 关闭，行为与未持卡完全一致）。</summary>
        public float AimInteractRadius { get; set; } = 0f;
        public int PendingThrowFrame { get; set; } = -1;

        private GameActor? _actor;
        private Area2D? _interactionArea;

        public override void _Ready()
        {
            base._Ready();

            // 获取 Actor 引用（优先使用父节点，然后是 Owner）
            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            
            // 如果还是 null，尝试从父节点的父节点获取（处理嵌套结构）
            if (_actor == null && GetParent() != null)
            {
                var parent = GetParent();
                _actor = parent.GetParent() as GameActor;
            }
            
            // 如果还是 null，尝试通过场景树查找
            if (_actor == null)
            {
                var player = GetTree().GetFirstNodeInGroup("player") as GameActor;
                if (player != null)
                {
                    _actor = player;
                    GD.Print($"[{Name}] 通过场景树查找找到 Actor: {_actor.Name}");
                }
            }

            if (_actor == null)
            {
                GameLogger.Error(nameof(PlayerItemInteractionComponent), $"{Name} 未能找到 GameActor（父节点: {GetParent()?.Name ?? "null"}, Owner: {GetOwner()?.Name ?? "null"}）。");
            }
            else
            {
                GD.Print($"[{Name}] Actor 初始化成功: {_actor.Name}");
            }

            // 查找 InventoryComponent（优先使用 Export 属性，然后是节点查找）
            if (InventoryComponent == null)
            {
                InventoryComponent = GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            }
            
            if (InventoryComponent == null && _actor != null)
            {
                InventoryComponent = _actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            }
            
            if (InventoryComponent == null)
            {
                InventoryComponent = FindChildComponent<PlayerInventoryComponent>(GetParent());
            }

            if (InventoryComponent == null)
            {
                GameLogger.Error(nameof(PlayerItemInteractionComponent), $"{Name} 未能找到 PlayerInventoryComponent。");
            }
            else
            {
                GD.Print($"[{Name}] InventoryComponent 初始化成功: {InventoryComponent.Name}");
            }

            // 尝试解析互动区域
            ResolveInteractionArea();

            SetProcess(true);
        }
        
        private void ResolveInteractionArea()
        {
            // 优先使用指定的路径
            if (InteractionAreaPath != null && !InteractionAreaPath.IsEmpty)
            {
                _interactionArea = GetNodeOrNull<Area2D>(InteractionAreaPath);
            }
            
            // 尝试常见的路径
            if (_interactionArea == null && _actor != null)
            {
                _interactionArea = _actor.GetNodeOrNull<Area2D>("SpineCharacter/GrabArea");
            }
            
            if (_interactionArea == null && _actor != null)
            {
                _interactionArea = _actor.GetNodeOrNull<Area2D>("GrabArea");
            }
            
            if (_interactionArea == null && _actor != null)
            {
                // 尝试查找任何名为 GrabArea 的子节点
                _interactionArea = _actor.FindChild("GrabArea", recursive: true) as Area2D;
            }
            
            if (_interactionArea == null)
            {
                GameLogger.Warn(nameof(PlayerItemInteractionComponent), 
                    $"{Name}: 未找到 InteractionArea，将使用距离检测模式。拾取范围: {PickupRange} 像素");
            }
            else
            {
                GameLogger.Info(nameof(PlayerItemInteractionComponent), 
                    $"{Name}: InteractionArea 已解析: {_interactionArea.GetPath()}");
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (!EnableInput || InventoryComponent?.Backpack == null)
            {
                return;
            }

            var player = _actor as SamplePlayer;

            if (player != null && player.WasActionLongPressTriggered("place") && CanPerformItemAction())
            {
                TryHandleDrop(DropDisposition.Place);
            }

            if ((_actor is SamplePlayer sp && sp.IsActionJustPressedArbitrated("throw")) && CanPerformItemAction())
            {
                TryHandleDrop(DropDisposition.Throw);
            }

            if ((_actor is SamplePlayer sp2 && sp2.IsActionJustPressedArbitrated("item_select_right")) && CanSwitchEquipment())
            {
                InventoryComponent?.SelectNextBackpackSlot();
            }

            if ((_actor is SamplePlayer sp3 && sp3.IsActionJustPressedArbitrated("item_select_left")) && CanSwitchEquipment())
            {
                InventoryComponent?.SelectPreviousBackpackSlot();
            }

            if (_actor is SamplePlayer sp4 && sp4.IsActionJustPressedArbitrated("item_use"))
            {
                TryUseSelectedItem();
            }

            if (player != null && player.WasActionShortPressed("take_up"))
            {
                TriggerPickupState();
            }

            // 每帧计算一次最近的可高亮物品（O(N) 替代原先每个物品 O(N) → 总 O(N²)）
            UpdateClosestHighlight();
        }

        /// <summary>
        /// 遍历 world_items 组，找到距离玩家 GrabArea 最近且重叠的物品，设为高亮。
        /// 每帧只运行一次（在 PlayerItemInteractionComponent._Process 中调用）。
        /// </summary>
        private void UpdateClosestHighlight()
        {
            // 隔空抓取（BuildThrow_B_009）：瞄准点**有目标时**高亮它（与拾取用同一选取函数，
            // 保证"看到的就是抓到的"）；瞄准点没目标时**不接管**，落回下面的常规 GrabArea 高亮——
            // 否则持卡后鼠标指向空地会把周围可拾取道具的高亮一起清掉。
            if (AimInteractRadius > 0f && TryResolveAimWorldPoint(out var aimPoint)
                && FindNearestPickableNearPoint(aimPoint, AimInteractRadius) is Node2D aimTarget)
            {
                RigidBodyWorldItemEntity.CurrentHighlightedEntity = aimTarget as RigidBodyWorldItemEntity;
                WorldItemEntity.CurrentHighlightedEntity = aimTarget as WorldItemEntity;
                return;
            }

            RigidBodyWorldItemEntity? closestRigid = null;
            WorldItemEntity? closestWorld = null;
            float minDistRigid = float.MaxValue;
            float minDistWorld = float.MaxValue;

            if (_interactionArea != null && GodotObject.IsInstanceValid(_interactionArea))
            {
                var tree = GetTree();
                if (tree != null)
                {
                    // 第一遍：收集所有与玩家交互区域重叠的候选
                    var candidates = new System.Collections.Generic.List<Node2D>();
                    var rigidCandidates = new System.Collections.Generic.List<RigidBodyWorldItemEntity>();
                    var worldCandidates = new System.Collections.Generic.List<WorldItemEntity>();
                    var worldCandidatesAsNode = new System.Collections.Generic.List<Node2D>();

                    foreach (var node in tree.GetNodesInGroup("world_items"))
                    {
                        if (node is RigidBodyWorldItemEntity rigidItem && rigidItem.IsHighlightCandidate)
                        {
                            if (rigidItem.GrabArea!.OverlapsArea(_interactionArea))
                            {
                                candidates.Add(rigidItem);
                                rigidCandidates.Add(rigidItem);
                            }
                        }
                        else if (node is WorldItemEntity worldItem && worldItem.IsHighlightCandidate)
                        {
                            if (worldItem.TriggerArea.OverlapsArea(_interactionArea))
                            {
                                candidates.Add(worldItem);
                                worldCandidates.Add(worldItem);
                                worldCandidatesAsNode.Add(worldItem);
                            }
                        }
                    }

                    // 第二遍：过滤被遮挡的 RigidBody 候选
                    var highlightOcclusionList = BuildOcclusionCheckList(candidates);
                    foreach (var rigidItem in rigidCandidates)
                    {
                        if (IsBlockedByOtherItem(rigidItem, highlightOcclusionList))
                            continue;
                        float dist = rigidItem.GlobalPosition.DistanceSquaredTo(_interactionArea.GlobalPosition);
                        if (dist < minDistRigid)
                        {
                            minDistRigid = dist;
                            closestRigid = rigidItem;
                        }
                    }

                    // 过滤被遮挡的 WorldItem 候选
                    foreach (var worldItem in worldCandidates)
                    {
                        if (IsBlockedByOtherItem(worldItem, highlightOcclusionList))
                            continue;
                        float dist = worldItem.GlobalPosition.DistanceSquaredTo(_interactionArea.GlobalPosition);
                        if (dist < minDistWorld)
                        {
                            minDistWorld = dist;
                            closestWorld = worldItem;
                        }
                    }

                    // 跨类型比较：只高亮全局最近的那一件
                    if (closestRigid != null && closestWorld != null)
                    {
                        if (minDistRigid <= minDistWorld)
                            closestWorld = null;
                        else
                            closestRigid = null;
                    }
                }
            }

            RigidBodyWorldItemEntity.CurrentHighlightedEntity = closestRigid;
            WorldItemEntity.CurrentHighlightedEntity = closestWorld;
        }

        public bool TryTriggerThrowAfterAnimation()
        {
            bool result = TryHandleDrop(DropDisposition.Throw, skipAnimation: true);
            if (result && _actor is SamplePlayer player)
            {
                // 投掷物已出手：置 ThrowInProgress=false（每帧 UpdateAttachmentIcon 持续抑制显示——投掷后摇不显示任何投掷物/武器）
                // 并立即清空手上视觉。投掷武器另有 IsThrowOnCooldown 双保险；投掷家具（一次性，无 cooldown）靠此抑制，
                // 否则家具投出后 activeItem 回落到背包选中投掷武器会在 Throw 状态被举起显示
                var holding = player.GetNodeOrNull<PlayerItemAttachment>("ItemHoldingAttachment");
                if (holding != null)
                {
                    holding.SetThrowInProgress(false);
                    holding.ClearHeldVisual();
                }
            }
            return result;
        }

        private bool TryHandleDrop(DropDisposition disposition)
        {
            return TryHandleDrop(disposition, skipAnimation: false);
        }

        private bool TryHandleDrop(DropDisposition disposition, bool skipAnimation)
        {
            if (InventoryComponent == null)
            {
                GD.PrintErr($"[PlayerItemInteractionComponent] TryHandleDrop 失败: InventoryComponent 为 null");
                return false;
            }

            var selectedStack = InventoryComponent.GetSelectedQuickBarStack();
            if (selectedStack == null || selectedStack.IsEmpty || selectedStack.Item.ItemId == "empty_item")
                return false;

            if (!skipAnimation && disposition == DropDisposition.Throw)
            {
                if (TryTriggerThrowState())
                    return false;
                return TryHandleDrop(disposition, skipAnimation: true);
            }

            bool isThrowWeapon = disposition == DropDisposition.Throw && selectedStack.Item.IsThrowWeapon;
            if (isThrowWeapon && selectedStack.IsThrowOnCooldown)
                return false;

            if (disposition == DropDisposition.Place
                && selectedStack.Item.IsThrowable
                && selectedStack.Item.IsThrowWeapon
                && selectedStack.Item.PreventDropDuringCooldown
                && selectedStack.IsThrowOnCooldown)
                return false;

            InventoryItemStack extracted;
            bool extractedFromInventory;
            float savedCd = selectedStack.ThrowCooldownRemaining;

            if (isThrowWeapon)
            {
                selectedStack.ThrowCooldownRemaining = selectedStack.Item.ThrowWeaponCooldown;
                // CD 开始：战斗武器解析变化（武器飞行中按空手处理，技能控制器切空手回退）
                InventoryComponent.NotifyCombatWeaponResolutionChanged();
                extracted = new InventoryItemStack(selectedStack.Item, 1);
                extractedFromInventory = false;
            }
            else
            {
                if (!InventoryComponent.TryExtractFromSelectedQuickBarSlot(selectedStack.Quantity, out var invExtracted, _actor)
                    || invExtracted == null || invExtracted.IsEmpty)
                    return false;
                extracted = invExtracted;
                extractedFromInventory = true;
            }

            var tempPos = ComputeDropPosition(disposition, null);
            var entity = WorldItemSpawner.SpawnFromStack(this, extracted, tempPos);
            if (entity is Node2D entityNode)
            {
                entityNode.GlobalPosition = ComputeDropPosition(disposition, entityNode);
            }

            if (entity == null)
            {
                if (extractedFromInventory)
                    InventoryComponent.TryReturnStackToSelectedQuickBarSlot(extracted, out _);
                return false;
            }

            entity.LastDroppedBy = _actor;

            // 消费件身份(跨拾取恢复):
            // · 件 + 放置 → 重新入 throwcore 组 → 在场脉冲恢复(投掷不恢复)
            // · A_007 复制件(任意生成方式)→ 恢复复制身份组 + 乱码块滤镜
            bool isPiece = extracted.RuntimeSourceTag == Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCorePieceTag;
            if (isPiece || extracted.RuntimeIsThrowCoreCopy)
            {
                if (entity is Node2D spawnedPiece)
                {
                    // 件身份(摧毁爆炸依据):放置与投掷都入组;脉冲恢复(ThrowCorePieceTag)仅放置
                    if (isPiece)
                    {
                        spawnedPiece.AddToGroup(Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCorePieceIdentityTag);
                        spawnedPiece.SetMeta("throwcore_born_ms", Time.GetTicksMsec()); // A_004 误爆防护
                    }
                    if (isPiece && disposition == DropDisposition.Place)
                        spawnedPiece.AddToGroup(Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCorePieceTag);
                    if (extracted.RuntimeIsThrowCoreCopy)
                    {
                        spawnedPiece.AddToGroup(Kuros.Items.World.RigidBodyWorldItemEntity.ThrowCoreCopyTag);
                        Kuros.Fx.PieceCopyGlitchDecorator.Apply(spawnedPiece);
                    }
                }
                extracted.RuntimeSourceTag = null;
                extracted.RuntimeIsThrowCoreCopy = false;
            }

            if (entity is RigidBodyWorldItemEntity re && savedCd > 0f)
                re.ThrowCooldownRemaining = savedCd;

            // 投掷耐久(B_006)：已用次数随件跨"拾取→放置→投掷"传递——放置不计数，出手 +1
            if (entity is RigidBodyWorldItemEntity spawnCount)
                spawnCount.ThrowCountUsed = extracted.RuntimeThrowCountUsed;

            if (disposition == DropDisposition.Throw)
            {
                if (entity is RigidBodyWorldItemEntity rigidEntity)
                {
                    // 构筑修饰：投掷者实现 IThrowableModifierProvider 时携带其构筑对投掷参数的修饰
                    if (_actor is IThrowableModifierProvider modProvider)
                        rigidEntity.Modifiers = modProvider.GetThrowableModifiers();

                    rigidEntity.IsDisposableCopy = isThrowWeapon;
                    rigidEntity.ThrowHoldFrame = PendingThrowFrame;
                    PendingThrowFrame = -1;
                    rigidEntity.ThrowCountUsed++;
                }
                entity.ApplyThrowImpulse(GetFacingDirection() * ThrowImpulse);
                if (entity is RigidBodyWorldItemEntity thrownRigid)
                    PieceThrown?.Invoke(thrownRigid); // 出手瞬间钩子(A_008 分裂散射)
                if (entity is Node2D eNode)
                    eNode.ZIndex = extracted.Item.ThrowZIndex;
            }

            if (extractedFromInventory)
                InventoryComponent.NotifyItemRemoved(extracted.Item.ItemId);
            return true;
        }

        private bool TryUseSelectedItem()
        {
            if (InventoryComponent == null)
            {
                return false;
            }

            return InventoryComponent.TryConsumeSelectedItem(_actor);
        }

        private Vector2 ComputeDropPosition(DropDisposition disposition, Node2D? spawnedEntity)
        {
            var origin = _actor?.GlobalPosition ?? Vector2.Zero;
            var direction = GetFacingDirection();

            Vector2 baseOffset = disposition == DropDisposition.Throw ? ThrowOffset : DropOffset;
            float halfWidth = baseOffset.X;
            if (spawnedEntity != null)
            {
                var shape = GetPickableCollisionShape(spawnedEntity);
                if (shape != null)
                    halfWidth = GetCollisionHalfWidth(shape) + PlacementMargin + baseOffset.X;
            }

            return origin + new Vector2(direction.X * halfWidth, baseOffset.Y);
        }

        internal bool ExecutePickupAfterAnimation() => TryHandlePickup();

        private bool CanPerformItemAction()
        {
            var currentState = _actor?.StateMachine?.CurrentState?.Name ?? string.Empty;
            if (currentState == "Attack" || currentState == "Throw")
            {
                return false;
            }
            return true;
        }

        private bool CanSwitchEquipment()
        {
            return CanPerformItemAction();
        }

        private void TriggerPickupState()
        {
            if (_actor?.StateMachine == null)
            {
                TryHandlePickup();
                return;
            }

            if (_actor.StateMachine.HasState("PickUp"))
            {
                if (!_actor.StateMachine.ChangeState("PickUp"))
                {
                    // 状态转换被拒绝（如攻击/投掷中不允许进入 PickUp），直接执行拾取
                    TryHandlePickup();
                }
            }
            else
            {
                GameLogger.Warn(nameof(PlayerItemInteractionComponent), "StateMachine 中未找到 'PickUp' 状态，直接执行拾取逻辑。");
                TryHandlePickup();
            }
        }

        private bool TryHandlePickup()
        {
            if (_actor == null)
            {
                GD.PrintErr("[PlayerItemInteractionComponent] _actor 为 null");
                return false;
            }

            // 隔空抓取（BuildThrow_B_009）：优先抓"瞄准点(鼠标指针)附近"的最近一件；
            // 瞄准点附近没有可拾取物时回落到常规拾取（持卡也不会破坏原有手感）。
            if (AimInteractRadius > 0f && TryResolveAimWorldPoint(out var aimPoint)
                && FindNearestPickableNearPoint(aimPoint, AimInteractRadius) is Node2D aimTarget)
            {
                return TryPickupNode(aimTarget);
            }

            var actorPosition = _actor.GlobalPosition;
            Node2D? nearestPickable = null;
            float nearestDistanceSq = float.MaxValue;

            // 方法1: 通过 InteractionArea 检测（如果存在）
            if (_interactionArea != null)
            {
                nearestPickable = FindNearestPickableFromArea(_interactionArea, actorPosition, ref nearestDistanceSq);
            }

            // 方法2: 通过距离检测（备用方案，支持 RigidBodyWorldItemEntity）
            if (nearestPickable == null)
            {
                nearestPickable = FindNearestPickableByDistance(actorPosition, ref nearestDistanceSq);
            }

            return nearestPickable != null && TryPickupNode(nearestPickable);
        }

        /// <summary>按节点类型分派拾取（常规拾取与隔空抓取共用）。</summary>
        private bool TryPickupNode(Node2D target)
        {
            if (_actor == null) return false;

            return target switch
            {
                WorldItemEntity worldItem => worldItem.TryPickupByActor(_actor),
                RigidBodyWorldItemEntity rigidItem => rigidItem.TryPickupByActor(_actor),
                PickupProperty pickupProp => pickupProp.TryPickupByActor(_actor),
                _ => false,
            };
        }

        /// <summary>隔空抓取的选取：瞄准点半径内最近一件可拾取物（不要求靠近玩家，与常规拾取相反）。
        /// 过滤规则与常规路径一致：IsPickupAvailable（投掷生命周期中的件不参与）+ 遮挡过滤。</summary>
        private Node2D? FindNearestPickableNearPoint(Vector2 point, float radius)
        {
            var tree = GetTree();
            if (tree == null) return null;

            float rangeSq = radius * radius;
            var candidates = new System.Collections.Generic.List<Node2D>();

            foreach (var node in tree.GetNodesInGroup("world_items"))
            {
                if (node is RigidBodyWorldItemEntity rigid)
                {
                    if (rigid.IsPickupAvailable && point.DistanceSquaredTo(rigid.GlobalPosition) <= rangeSq)
                        candidates.Add(rigid);
                }
                else if (node is WorldItemEntity world)
                {
                    if (point.DistanceSquaredTo(world.GlobalPosition) <= rangeSq && !candidates.Contains(world))
                        candidates.Add(world);
                }
            }

            foreach (var node in tree.GetNodesInGroup("pickables"))
            {
                if (node is PickupProperty pickup
                    && point.DistanceSquaredTo(pickup.GlobalPosition) <= rangeSq
                    && !candidates.Contains(pickup))
                {
                    candidates.Add(pickup);
                }
            }

            var occlusionList = BuildOcclusionCheckList(candidates);
            Node2D? nearest = null;
            float bestDistSq = float.MaxValue;
            foreach (var candidate in candidates)
            {
                if (IsBlockedByOtherItem(candidate, occlusionList)) continue;
                float distSq = point.DistanceSquaredTo(candidate.GlobalPosition);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        /// <summary>瞄准点世界坐标：优先 A_010 的 AimPointResolver（设备无关：鼠标/手柄/键盘回退，
        /// 且不会因本卡而创建它）；未装解析器时直接用鼠标世界坐标。</summary>
        private bool TryResolveAimWorldPoint(out Vector2 point)
        {
            point = Vector2.Zero;
            if (_actor == null || !GodotObject.IsInstanceValid(_actor)) return false;

            var resolver = AimPointResolver.Find(_actor);
            if (resolver != null && GodotObject.IsInstanceValid(resolver))
                return resolver.TryGetAimWorldPoint(out point);

            point = _actor.GetGlobalMousePosition();
            return true;
        }
        
        /// <summary>
        /// 检查 candidate 是否被列表中的其他物品遮挡。
        /// 遮挡规则：若 other 的碰撞区域与 candidate 重叠，且 other 的 Y 轴更大（在画面中更靠前），则 candidate 被遮挡。
        /// </summary>
        /// <summary>
        /// 纯几何 AABB 重叠检测。从两个 CollisionShape2D 计算全局包围盒并做相交测试。
        /// </summary>
        /// <summary>
        /// 从 CollisionShape2D 提取 X 轴半宽（用于放置偏移计算）。
        /// </summary>
        private static float GetCollisionHalfWidth(CollisionShape2D shape)
        {
            if (shape?.Shape == null) return 32f;
            if (shape.Shape is RectangleShape2D rect) return rect.Size.X * 0.5f;
            if (shape.Shape is CircleShape2D circle) return circle.Radius;
            return 32f;
        }

        private static bool AreCollisionShapesOverlapping(CollisionShape2D shapeA, CollisionShape2D shapeB)
        {
            if (shapeA.Shape == null || shapeB.Shape == null)
                return false;

            static Vector2 GetHalfExtents(CollisionShape2D node)
            {
                if (node.Shape is RectangleShape2D rect)
                    return rect.Size * 0.5f;
                if (node.Shape is CircleShape2D circle)
                {
                    float r = circle.Radius;
                    return new Vector2(r, r);
                }
                return new Vector2(32f, 32f);
            }

            Vector2 extA = GetHalfExtents(shapeA);
            Vector2 extB = GetHalfExtents(shapeB);
            Vector2 posA = shapeA.GlobalPosition;
            Vector2 posB = shapeB.GlobalPosition;

            Rect2 aabbA = new Rect2(posA - extA, extA * 2);
            Rect2 aabbB = new Rect2(posB - extB, extB * 2);
            return aabbA.Intersects(aabbB, true);
        }

        /// <summary>遮挡判定（叠层拾取）：家具类候选若被"Y 更大（更靠前）"且形状重叠的另一件家具压住,
        /// 则不参与高亮/拾取——模拟"先拿桌子上的苹果,而不是先拿桌子"。仅家具参与（武器无 StaticBody2D）；
        /// 形状用**拿取判定区**（CollisionArea）而非 StaticBody2D 底座：底座是导航用的大盒子,
        /// 相邻两件仅一个角相碰就会互相遮挡,叠层意图被误伤。</summary>
        private static bool IsBlockedByOtherItem(Node2D candidate, System.Collections.Generic.List<Node2D> allCandidates)
        {
            if (!IsFurnitureItem(candidate))
                return false;

            var candidateShape = GetOcclusionShape(candidate);
            if (candidateShape == null) return false;

            float candidateY = candidate.GlobalPosition.Y;

            foreach (var other in allCandidates)
            {
                if (other == candidate) continue;
                if (!IsFurnitureItem(other)) continue;

                var otherShape = GetOcclusionShape(other);
                if (otherShape == null) continue;

                // 只检查 Y 轴在 candidate 之下的物品（更靠前）
                if (other.GlobalPosition.Y <= candidateY) continue;

                if (AreCollisionShapesOverlapping(candidateShape, otherShape))
                    return true;
            }

            return false;
        }

        /// <summary>遮挡判定专用形状：拿取判定区（CollisionArea）的形状——精确表达"可拿取的那一层"；
        /// 缺拿取区时回退实体底座（StaticBody2D），再回退物品触发区。
        /// 与 <see cref="GetPickableCollisionShape"/> 分开：后者供放置落点计算用（需按物理底座留出间距）。</summary>
        private static CollisionShape2D? GetOcclusionShape(Node2D pickable)
        {
            if (pickable is RigidBodyWorldItemEntity rigidItem)
            {
                var grabArea = rigidItem.GrabArea;
                if (grabArea != null)
                {
                    var grabShape = FindFirstShape(grabArea);
                    if (grabShape != null) return grabShape;
                }
                var parent = grabArea?.GetParent();
                return parent?.GetNodeOrNull<CollisionShape2D>("StaticBody2D/CollisionShape2D");
            }
            return GetPickableCollisionShape(pickable);
        }

        /// <summary>区域内首个可用碰撞形状（子节点命名不统一时的兜底：先找默认名 CollisionShape2D）。</summary>
        private static CollisionShape2D? FindFirstShape(Node area)
        {
            var named = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (named?.Shape != null) return named;
            foreach (var child in area.GetChildren())
            {
                if (child is CollisionShape2D cs && cs.Shape != null)
                    return cs;
            }
            return null;
        }

        /// <summary>
        /// 判断是否为家具类物品（一次性的投掷物，非投掷武器）。
        /// 只有家具有 StaticBody2D 碰撞体，武器没有，需要区分处理。
        /// </summary>
        private static bool IsFurnitureItem(Node2D pickable)
        {
            ItemDefinition? def = null;
            if (pickable is WorldItemEntity worldItem)
                def = worldItem.ItemDefinition;
            else if (pickable is RigidBodyWorldItemEntity rigidItem)
                def = rigidItem.ItemDefinition;

            return def != null && def.IsThrowable && !def.IsThrowWeapon;
        }

        /// <summary>
        /// 获取可拾取物品的物理底座形状（放置落点计算用：按底座宽度留出与玩家的间距）。
        /// RigidBodyWorldItemEntity 使用 StaticBody2D 的碰撞形状（导航底座，比视觉大），
        /// WorldItemEntity/PickupProperty 使用 TriggerArea 的碰撞形状。
        /// 遮挡判定不要用这个（底座过大会误判叠层）——见 <see cref="GetOcclusionShape"/>。
        /// </summary>
        private static CollisionShape2D? GetPickableCollisionShape(Node2D pickable)
        {
            if (pickable is RigidBodyWorldItemEntity rigidItem)
            {
                var grabArea = rigidItem.GrabArea;
                if (grabArea == null) return null;
                // 从 GrabArea 路径推导 StaticBody2D 的位置：同级 StaticBody2D
                var parent = grabArea.GetParent();
                if (parent != null)
                    return parent.GetNodeOrNull<CollisionShape2D>("StaticBody2D/CollisionShape2D");
                return null;
            }
            if (pickable is WorldItemEntity worldItem)
                return worldItem.TriggerArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (pickable is PickupProperty pickupProp)
                return pickupProp.GetNodeOrNull<Area2D>("TriggerArea")?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return null;
        }

        private Node2D? FindNearestPickableFromArea(Area2D area, Vector2 actorPosition, ref float nearestDistanceSq)
        {
            Node2D? nearestPickable = null;
            var candidates = new System.Collections.Generic.List<Node2D>();

            // 第一遍：收集所有候选物品
            foreach (var areaNode in area.GetOverlappingAreas())
            {
                var parent = areaNode.GetParent();

                if (parent is WorldItemEntity entity)
                    candidates.Add(entity);
                else if (parent is RigidBodyWorldItemEntity rigidEntity)
                {
                    // 投掷生命周期中的道具不参与候选（否则会抢占"最近"位置 → 拾取静默失败）
                    if (rigidEntity.IsPickupAvailable)
                        candidates.Add(rigidEntity);
                }
                else if (parent is RigidBody2D rigidBody)
                {
                    if (rigidBody.GetParent() is RigidBodyWorldItemEntity rigidEntityFromBody
                        && rigidEntityFromBody.IsPickupAvailable)
                        candidates.Add(rigidEntityFromBody);
                }
                else if (parent is PickupProperty pickup)
                    candidates.Add(pickup);
            }

            // 第二遍：过滤被遮挡的物品，从剩余候选中选距离最近的
            var areaOcclusionList = BuildOcclusionCheckList(candidates);
            foreach (var candidate in candidates)
            {
                if (IsBlockedByOtherItem(candidate, areaOcclusionList))
                    continue;

                float distanceSq = actorPosition.DistanceSquaredTo(candidate.GlobalPosition);
                if (distanceSq < nearestDistanceSq)
                {
                    nearestDistanceSq = distanceSq;
                    nearestPickable = candidate;
                }
            }

            return nearestPickable;
        }

        /// <summary>
        /// 合并候选列表与场景中所有可拾取物品，返回用于遮挡检测的扩展列表。
        /// 确保不直接触碰玩家的遮挡物也能被检测到。
        /// </summary>
        private System.Collections.Generic.List<Node2D> BuildOcclusionCheckList(System.Collections.Generic.List<Node2D> candidates)
        {
            var tree = GetTree();
            if (tree == null) return candidates;

            var expanded = new System.Collections.Generic.List<Node2D>(candidates);
            foreach (var node in tree.GetNodesInGroup("world_items"))
            {
                if (node is RigidBodyWorldItemEntity rigidItem && rigidItem.IsHighlightCandidate && !expanded.Contains(rigidItem))
                    expanded.Add(rigidItem);
                else if (node is WorldItemEntity worldItem && worldItem.IsHighlightCandidate && !expanded.Contains(worldItem))
                    expanded.Add(worldItem);
            }
            foreach (var node in tree.GetNodesInGroup("pickables"))
            {
                if (node is WorldItemEntity worldItem && worldItem.IsHighlightCandidate && !expanded.Contains(worldItem))
                    expanded.Add(worldItem);
                else if (node is PickupProperty pickup && !expanded.Contains(pickup))
                    expanded.Add(pickup);
            }
            return expanded;
        }

        private Node2D? FindNearestPickableByDistance(Vector2 actorPosition, ref float nearestDistanceSq)
        {
            Node2D? nearestPickable = null;
            float rangeSq = PickupRange * PickupRange;
            var candidates = new System.Collections.Generic.List<Node2D>();

            var sceneTree = GetTree();
            if (sceneTree != null)
            {
                // 第一遍：收集所有在范围内的候选物品
                var allRigidItems = sceneTree.GetNodesInGroup("world_items");
                foreach (var node in allRigidItems)
                {
                    if (node is RigidBodyWorldItemEntity rigidItem)
                    {
                        // 投掷生命周期中的道具不参与候选（否则会抢占"最近"位置，TryPickupByActor 再拒绝 → 按了没反应）
                        if (!rigidItem.IsPickupAvailable) continue;
                        float distanceSq = actorPosition.DistanceSquaredTo(rigidItem.GlobalPosition);
                        bool inRange = rigidItem.IsActorInRange(_actor!);
                        if (inRange && distanceSq < rangeSq)
                            candidates.Add(rigidItem);
                    }
                }

                var allPickables = sceneTree.GetNodesInGroup("pickables");
                foreach (var node in allPickables)
                {
                    if (node is WorldItemEntity worldItem)
                    {
                        float distanceSq = actorPosition.DistanceSquaredTo(worldItem.GlobalPosition);
                        if (distanceSq < rangeSq)
                            candidates.Add(worldItem);
                    }
                    else if (node is PickupProperty pickup)
                    {
                        float distanceSq = actorPosition.DistanceSquaredTo(pickup.GlobalPosition);
                        if (distanceSq < rangeSq)
                            candidates.Add(pickup);
                    }
                }
            }

            // 第二遍：过滤被遮挡的物品，从剩余候选中选距离最近的
            var distanceOcclusionList = BuildOcclusionCheckList(candidates);
            foreach (var candidate in candidates)
            {
                if (IsBlockedByOtherItem(candidate, distanceOcclusionList))
                    continue;

                float distanceSq = actorPosition.DistanceSquaredTo(candidate.GlobalPosition);
                if (distanceSq < nearestDistanceSq)
                {
                    nearestDistanceSq = distanceSq;
                    nearestPickable = candidate;
                }
            }

            return nearestPickable;
        }

        private Vector2 GetFacingDirection()
        {
            if (_actor == null)
            {
                return Vector2.Right;
            }

            return _actor.FacingRight ? Vector2.Right : Vector2.Left;
        }

        private bool TryTriggerThrowState()
        {
            if (_actor?.StateMachine == null)
            {
                GD.PrintErr($"[PlayerItemInteractionComponent] TryTriggerThrowState 失败: StateMachine 为 null (_actor={_actor?.Name ?? "null"})");
                return false;
            }

            if (!_actor.StateMachine.HasState(ThrowStateName))
            {
                GD.PrintErr($"[PlayerItemInteractionComponent] TryTriggerThrowState 失败: StateMachine 中不存在 '{ThrowStateName}' 状态");
                return false;
            }

            _actor.StateMachine.ChangeState(ThrowStateName);
            return true;
        }

        private static T? FindChildComponent<T>(Node? root) where T : Node
        {
            if (root == null)
            {
                return null;
            }

            foreach (Node child in root.GetChildren())
            {
                if (child is T typed)
                {
                    return typed;
                }

                if (child.GetChildCount() > 0)
                {
                    var nested = FindChildComponent<T>(child);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            return null;
        }
    }
}
