using Godot;
using Kuros.Actors.Heroes;
using Kuros.Items.World;
using Kuros.Managers;
using Kuros.Systems.Inventory;

namespace Kuros.UI
{
	/// <summary>
	/// 快捷栏面板（当前使用的背包 UI）：5 个快捷槽位展示（图标/名称/高亮/锁定/投掷 CD 覆盖层）
	/// + 金币显示。从 BattleHUD 独立出来的组件，交互功能（拖拽/丢弃/删除）在此扩展。
	/// </summary>
	public partial class QuickBarPanel : Control
	{
		[ExportCategory("Styles")]
		// 快捷物品栏整体面板样式（例如使用武器栏底图做 StyleBoxTexture）
		[Export] public StyleBox? QuickBarPanelStyle { get; set; }
		// 非锁定武器槽的底层空白方块纹理（在所有已解锁槽位上持续显示，位于武器图标下方）
		[Export] public Texture2D? EmptySlotFrameTexture { get; set; }
		// 当前选中武器槽的高亮框架纹理（选中框，选中时替换 EmptySlotFrameTexture）
		[Export] public Texture2D? SelectedFrameTexture { get; set; }
		// 锁定武器槽的遮掩纹理（覆盖在未解锁的槽位上）
		[Export] public Texture2D? LockedFrameTexture { get; set; }
		// 金币文本样式（可配合金币图标背景）
		[Export] public StyleBox? GoldLabelStyle { get; set; }
		// 分解区素材（美术替换：配置贴图后自动用 StyleBoxTexture，null 时回退代码配色）
		[Export] public Texture2D? TrashNormalTexture { get; set; }
		[Export] public Texture2D? TrashHoverTexture { get; set; }

		[Export] public Label? GoldLabel { get; private set; } = null!;
		[Export] public Control? TrashBin { get; private set; } = null!;
		[Export] public ConfirmationDialog? DeleteConfirmDialog { get; private set; } = null!;
		// 弹窗背景遮罩（半透明黑色，弹窗时变暗背景）
		private ColorRect? _dialogDim;

		// 拖拽状态
		private int _draggingSlotIndex = -1;
		private InventoryItemStack? _draggingStack;
		private Control? _dragPreview;
		// 槽位/垃圾桶 hover 计数（UIManager.IsMouseOverUI 只注册 Button/Slider，Panel 需自管）
		private int _uiHoverCount = 0;
		private StyleBox? _trashNormalStyle;
		private StyleBox? _trashHoverStyle;

		// 待删除状态（确认对话框）
		private int _pendingDeleteSlotIndex = -1;
		private InventoryItemStack? _pendingDeleteStack;
		// 本次弹窗的暂停是否已恢复（保证 PopPause 恰好一次，覆盖确认/取消/ESC/点击外部关闭）
		private bool _dialogPauseRestored = false;

		// 快捷栏UI引用
		private readonly Label[] _quickSlotLabels = new Label[5];
		private readonly Panel[] _quickSlotPanels = new Panel[5];
		private readonly TextureRect[] _quickSlotIcons = new TextureRect[5];
		// 槽位框架（SlotFrame）纹理：选中框/锁定遮罩由导出属性配置；解锁未选中槽无框
		private readonly TextureRect?[] _quickSlotFrames = new TextureRect?[5];
		private int _leftHandSlotIndex = -1;
		private int _rightHandSlotIndex = -1;
		// 投掷武器冷却遮罩覆盖层（每个快捷槽一个）
		private readonly ThrowCooldownOverlay?[] _quickSlotCooldownOverlays = new ThrowCooldownOverlay?[5];
		private float _throwCooldownUpdateTimer = 0f;
		private const float ThrowCooldownUpdateInterval = 0.05f;

		private InventoryContainer? _quickBarContainer;
		private SamplePlayer? _player;

		public override void _Ready()
		{
			CacheQuickBarLabels();
			GoldLabel ??= GetNodeOrNull<Label>("GoldSection/GoldLabel");
			TrashBin ??= GetNodeOrNull<Control>("TrashBin");
			DeleteConfirmDialog ??= GetNodeOrNull<ConfirmationDialog>("DeleteConfirmDialog");
			_dialogDim = GetNodeOrNull<ColorRect>("DialogDim");
			ApplyCustomStyles();

			// 槽位鼠标交互（拖拽）+ hover 计数（面板非 Button/Slider，需自管 IsMouseOverUI 抑制攻击）
			for (int i = 0; i < 5; i++)
			{
				int index = i;
				if (_quickSlotPanels[i] != null)
				{
					_quickSlotPanels[i].GuiInput += (InputEvent ev) => OnSlotGuiInput(index, ev);
					_quickSlotPanels[i].MouseEntered += OnUiHoverEntered;
					_quickSlotPanels[i].MouseExited += OnUiHoverExited;
				}
			}

			if (TrashBin != null)
			{
				// hover 计数（抑制攻击）+ 悬停视觉切换（Panel 无内置 hover 状态，手动切样式）
				TrashBin.MouseEntered += OnUiHoverEntered;
				TrashBin.MouseExited += OnUiHoverExited;
				TrashBin.MouseEntered += OnTrashHoverEntered;
				TrashBin.MouseExited += OnTrashHoverExited;
			}

			// 分解区样式：美术贴图优先（StyleBoxTexture），null 回退代码配色
			_trashNormalStyle = BuildTrashStyle(TrashNormalTexture, new Color(0.5f, 0.2f, 0.2f, 0.8f));
			_trashHoverStyle = BuildTrashStyle(TrashHoverTexture, new Color(0.7f, 0.3f, 0.3f, 0.95f));

			// 垃圾桶删除确认对话框（弹窗按钮非 UIManager 注册范围，需自管抑制攻击）
			if (DeleteConfirmDialog != null)
			{
				// 暂停期间（PushPause）按钮仍响应：Dialog 默认 Inherit=Pausable，暂停时 GuiInput 不处理
				DeleteConfirmDialog.ProcessMode = Node.ProcessModeEnum.Always;
				DeleteConfirmDialog.Confirmed += OnDeleteConfirmed;
				DeleteConfirmDialog.Canceled += OnDeleteCanceled;
				var hideCallable = new Callable(this, MethodName.OnDeleteDialogHidden);
				if (!DeleteConfirmDialog.IsConnected("popup_hide", hideCallable))
					DeleteConfirmDialog.Connect("popup_hide", hideCallable);
			}
		}

		private void OnDeleteDialogHidden()
		{
			// 任意方式关闭对话框（确认/取消/ESC/点击外部）后：恢复暂停 + 重算 IsMouseOverUI
			RestoreDialogPause();
			UpdateMouseOverUI();
		}

		/// <summary>
		/// 恢复弹窗暂停（幂等：本次弹窗只 PopPause 一次）。
		/// Confirmed/Canceled 与 popup_hide 都可能触发关闭，用标志去重。
		/// </summary>
		private void RestoreDialogPause()
		{
			if (_dialogPauseRestored) return;
			_dialogPauseRestored = true;
			PauseManager.Instance?.PopPause();
			// 遮罩随弹窗关闭隐藏
			if (_dialogDim != null) _dialogDim.Visible = false;
			// 弹窗按钮可能被 UIManager 注册（MouseEntered 置 true），而窗口隐藏时其
			// MouseExited 不触发导致标志卡 true → 攻击被禁用。强制重置后延迟一帧按当前 hover 重算
			UIManager.IsMouseOverUI = false;
			CallDeferred(MethodName.UpdateMouseOverUI);
		}

		private void OnUiHoverEntered()
		{
			_uiHoverCount++;
			UpdateMouseOverUI();
		}

		private void OnUiHoverExited()
		{
			_uiHoverCount = Mathf.Max(0, _uiHoverCount - 1);
			UpdateMouseOverUI();
		}

		private static StyleBox BuildTrashStyle(Texture2D? texture, Color fallback)
		{
			if (texture != null)
				return new StyleBoxTexture { Texture = texture };
			return new StyleBoxFlat { BgColor = fallback };
		}

		private void OnTrashHoverEntered()
		{
			TrashBin?.AddThemeStyleboxOverride("panel", _trashHoverStyle);
		}

		private void OnTrashHoverExited()
		{
			TrashBin?.AddThemeStyleboxOverride("panel", _trashNormalStyle);
		}

		/// <summary>
		/// 同步 IsMouseOverUI：hover 任一槽位/垃圾桶，或正在拖拽（鼠标可能已移出面板区域）时抑制游戏攻击输入。
		/// </summary>
		private void UpdateMouseOverUI()
		{
			UIManager.IsMouseOverUI = _uiHoverCount > 0 || _draggingSlotIndex >= 0;
		}

		// ── 拖拽交互 ──────────────────────────────────────────────

		private void OnSlotGuiInput(int slotIndex, InputEvent @event)
		{
			if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					// 开始拖拽（空槽/空白道具不可拖）
					var stack = _quickBarContainer?.GetStack(slotIndex);
					if (stack == null || stack.IsEmpty || stack.Item.ItemId == "empty_item")
						return;
					_draggingSlotIndex = slotIndex;
					_draggingStack = stack;
					CreateDragPreview(stack, mb.GlobalPosition);
					if (TrashBin != null) TrashBin.Visible = true; // 拖拽中显示"分解"区域
					UpdateMouseOverUI();
					GetViewport().SetInputAsHandled();
				}
				else if (_draggingSlotIndex >= 0)
				{
					FinishDrag(mb.GlobalPosition);
					GetViewport().SetInputAsHandled();
				}
			}
			else if (@event is InputEventMouseMotion mm && _dragPreview != null)
			{
				_dragPreview.GlobalPosition = mm.GlobalPosition - new Vector2(40, 40);
				GetViewport().SetInputAsHandled();
			}
		}

		private void CreateDragPreview(InventoryItemStack stack, Vector2 position)
		{
			_dragPreview = new Panel
			{
				Size = new Vector2(80, 80),
				GlobalPosition = position - new Vector2(40, 40),
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			// 透明面板：只显示图标，不出现默认深色底
			_dragPreview.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) });

			// 物品图标（与快捷栏槽位同款展示）
			if (stack.Item.Icon != null)
			{
				var iconRect = new TextureRect
				{
					Texture = stack.Item.Icon,
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					MouseFilter = Control.MouseFilterEnum.Ignore,
					Size = new Vector2(80, 80),
				};
				_dragPreview.AddChild(iconRect);
			}

			// 数量标签（底部）
			var label = new Label
			{
				Text = stack.Quantity > 1 ? $"x{stack.Quantity}" : "",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Bottom,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				Size = new Vector2(80, 80),
			};
			_dragPreview.AddChild(label);

			AddChild(_dragPreview);
			_dragPreview.SetAsTopLevel(true);
		}

		private void DestroyDragPreview()
		{
			if (_dragPreview != null)
			{
				_dragPreview.QueueFree();
				_dragPreview = null;
			}
		}

		private void FinishDrag(Vector2 globalPos)
		{
			DestroyDragPreview();
			int fromIndex = _draggingSlotIndex;
			var fromStack = _draggingStack;
			_draggingSlotIndex = -1;
			_draggingStack = null;
			if (TrashBin != null) TrashBin.Visible = false; // 拖拽结束隐藏"分解"区域
			UpdateMouseOverUI();
			if (fromIndex < 0 || fromStack == null || _quickBarContainer == null) return;

			// 1. 垃圾桶：确认后删除
			if (TrashBin != null && IsPointInControl(TrashBin, globalPos))
			{
				ShowDeleteConfirmDialog(fromIndex, fromStack);
				return;
			}

			// 2. 槽位交换（同一快捷栏容器，用复制构造器保留 CD/耐久/运行时属性）
			int target = FindSlotAtPosition(globalPos);
			if (target >= 0)
			{
				SwapSlots(fromIndex, target);
				return;
			}

			// 3. 快捷栏区域外：丢弃到世界（纯掉落）
			if (!IsInsideQuickBarArea(globalPos))
			{
				DropToWorld(fromIndex, fromStack);
			}
			// 4. 面板内非槽位：取消（物品留在原槽）
		}

		/// <summary>
		/// 快捷栏内部交换（空目标槽 = 移动）。使用 0.3 的复制构造器，运行时状态（投掷 CD/耐久/强化）不丢失。
		/// </summary>
		private void SwapSlots(int index1, int index2)
		{
			if (_quickBarContainer == null || index1 == index2) return;

			var stack1 = _quickBarContainer.GetStack(index1);
			var stack2 = _quickBarContainer.GetStack(index2);
			if ((stack1 == null || stack1.IsEmpty) && (stack2 == null || stack2.IsEmpty)) return;

			_quickBarContainer.SetStack(index1,
				stack2 != null && !stack2.IsEmpty ? new InventoryItemStack(stack2) : null);
			_quickBarContainer.SetStack(index2,
				stack1 != null && !stack1.IsEmpty ? new InventoryItemStack(stack1) : null);
		}

		/// <summary>
		/// 丢弃到世界（纯掉落）：ApplyScatterImpulse 仅物理弹出——不激活投掷伤害判定、
		/// 不触发 SpawnEffectOnThrow（丢弃回旋镖不会变成投掷攻击）。
		/// </summary>
		private void DropToWorld(int slotIndex, InventoryItemStack stack)
		{
			var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
			if (player == null) return;

			float facingX = player.FacingRight ? 1f : -1f;
			var dropPosition = player.GlobalPosition + new Vector2(50 * facingX, 0);

			var entity = WorldItemSpawner.SpawnFromStack(this, stack, dropPosition);
			if (entity == null) return;

			var random = new RandomNumberGenerator();
			random.Randomize();
			entity.ApplyScatterImpulse(new Vector2(
				random.RandfRange(-100, 100),
				random.RandfRange(-150, -50)));

			_quickBarContainer?.RemoveItemFromSlot(slotIndex, stack.Quantity);
		}

		// ── 垃圾桶删除 ────────────────────────────────────────────

		private void ShowDeleteConfirmDialog(int slotIndex, InventoryItemStack stack)
		{
			_pendingDeleteSlotIndex = slotIndex;
			_pendingDeleteStack = stack;

			if (DeleteConfirmDialog != null)
			{
				string itemInfo = stack.Quantity > 1
					? $"{stack.Item.DisplayName} x{stack.Quantity}"
					: stack.Item.DisplayName;
				DeleteConfirmDialog.DialogText = $"确定要删除 [{itemInfo}] 吗？\n此操作无法撤销。";
				DeleteConfirmDialog.PopupCentered();
				// 半透明黑色遮罩（背景变暗突出对话框）
				if (_dialogDim != null) _dialogDim.Visible = true;
				// 弹窗按钮不在 UIManager 注册范围，弹窗期间强制抑制游戏攻击输入
				UIManager.IsMouseOverUI = true;
				// 弹窗期间暂停游戏（与菜单窗口一致）；关闭时由 RestoreDialogPause 恢复
				_dialogPauseRestored = false;
				PauseManager.Instance?.PushPause();
			}
			else
			{
				PerformDelete(slotIndex, stack.Quantity);
			}
		}

		private void OnDeleteConfirmed()
		{
			RestoreDialogPause(); // 确认关闭（幂等）
			if (_pendingDeleteSlotIndex >= 0 && _pendingDeleteStack != null)
				PerformDelete(_pendingDeleteSlotIndex, _pendingDeleteStack.Quantity);
			ClearPendingDelete();
		}

		private void OnDeleteCanceled()
		{
			RestoreDialogPause(); // 取消关闭（幂等）
			ClearPendingDelete();
		}

		private void PerformDelete(int slotIndex, int quantity)
		{
			_quickBarContainer?.RemoveItemFromSlot(slotIndex, quantity);
		}

		private void ClearPendingDelete()
		{
			_pendingDeleteSlotIndex = -1;
			_pendingDeleteStack = null;
		}

		// ── 命中判断 ──────────────────────────────────────────────

		private static bool IsPointInControl(Control control, Vector2 globalPosition)
		{
			var rect = new Rect2(control.GlobalPosition, control.Size);
			return rect.HasPoint(globalPosition);
		}

		private bool IsInsideQuickBarArea(Vector2 globalPosition)
		{
			var container = GetNodeOrNull<Control>("QuickBarContainer");
			if (container == null) return false;
			return IsPointInControl(container, globalPosition);
		}

		private int FindSlotAtPosition(Vector2 globalPosition)
		{
			for (int i = 0; i < 5; i++)
			{
				if (_quickSlotPanels[i] != null && IsPointInControl(_quickSlotPanels[i], globalPosition))
					return i;
			}
			return -1;
		}

		/// <summary>
		/// 绑定快捷栏容器（BattleHUD 创建后注入；信号驱动槽位刷新）。
		/// </summary>
		public void SetQuickBarContainer(InventoryContainer container)
		{
			_quickBarContainer = container;
			if (_quickBarContainer != null)
			{
				_quickBarContainer.SlotChanged += OnQuickBarSlotChanged;
				_quickBarContainer.InventoryChanged += OnQuickBarChanged;
			}
			UpdateQuickBarDisplay();
		}

		/// <summary>
		/// 连接玩家（金币变化信号）。
		/// </summary>
		public void ConnectPlayer(SamplePlayer player)
		{
			_player = player;
			if (_player != null)
			{
				_player.GoldChanged -= OnPlayerGoldChanged;
				_player.GoldChanged += OnPlayerGoldChanged;
				UpdateGoldDisplay(_player.GetGold());
			}
		}

		/// <summary>
		/// 断开玩家（退订金币信号）。
		/// </summary>
		public void DisconnectPlayer(SamplePlayer player)
		{
			if (_player != null)
			{
				_player.GoldChanged -= OnPlayerGoldChanged;
				_player = null;
			}
		}

		private void CacheQuickBarLabels()
		{
			for (int i = 0; i < 5; i++)
			{
				_quickSlotLabels[i] = GetNodeOrNull<Label>($"QuickBarContainer/QuickSlot{i + 1}/QuickSlotLabel{i + 1}");
				_quickSlotPanels[i] = GetNodeOrNull<Panel>($"QuickBarContainer/QuickSlot{i + 1}");
				_quickSlotIcons[i] = GetNodeOrNull<TextureRect>($"QuickBarContainer/QuickSlot{i + 1}/QuickSlotIcon{i + 1}");
				_quickSlotFrames[i] = GetNodeOrNull<TextureRect>($"QuickBarContainer/QuickSlot{i + 1}/SlotFrame");

				if (_quickSlotLabels[i] == null)
					GD.PrintErr($"QuickBarPanel: Failed to find QuickSlotLabel{i + 1}");
				if (_quickSlotPanels[i] == null)
					GD.PrintErr($"QuickBarPanel: Failed to find QuickSlotPanel{i + 1}");
				if (_quickSlotIcons[i] == null)
					GD.PrintErr($"QuickBarPanel: Failed to find QuickSlotIcon{i + 1}");
				if (_quickSlotFrames[i] == null)
					GD.PrintErr($"QuickBarPanel: Failed to find QuickSlotFrame{i + 1}");
			}
		}

		private void ApplyCustomStyles()
		{
			// 快捷栏整体面板样式
			if (QuickBarPanelStyle != null)
			{
				var quickBarPanel = GetNodeOrNull<Panel>(".");
				if (quickBarPanel != null)
				{
					quickBarPanel.AddThemeStyleboxOverride("panel", QuickBarPanelStyle);
				}
			}

			// 金币文本样式
			if (GoldLabel != null && GoldLabelStyle != null)
			{
				GoldLabel.AddThemeStyleboxOverride("normal", GoldLabelStyle);
			}

			// 金币图标：使用 金币.png，点采样避免透明边灰圈
			var goldIcon = GetNodeOrNull<TextureRect>("GoldSection/GoldIcon");
			if (goldIcon != null)
			{
				goldIcon.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			}
		}

		public void UpdateQuickBarDisplay()
		{
			if (_quickBarContainer == null)
			{
				GD.PrintErr("UpdateQuickBarDisplay: QuickBarContainer is null");
				return;
			}

			for (int i = 0; i < 5; i++)
			{
				UpdateQuickBarSlot(i);
			}
		}

		private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
		{
			// 使用 CallDeferred 确保在下一帧更新，避免在信号处理过程中更新UI
			CallDeferred(MethodName.UpdateQuickBarSlot, slotIndex);
		}

		private void OnQuickBarChanged()
		{
			UpdateQuickBarDisplay();
		}

		public void UpdateQuickBarSlot(int slotIndex)
		{
			if (slotIndex < 0 || slotIndex >= 5) return;
			if (_quickBarContainer == null)
			{
				GD.PrintErr($"UpdateQuickBarSlot: QuickBarContainer is null for slot {slotIndex}");
				return;
			}

			var stack = _quickBarContainer.GetStack(slotIndex);
			bool isEmpty = stack == null || stack.IsEmpty;
			bool isEmptyItem = !isEmpty && stack!.Item.ItemId == "empty_item";

			// 更新标签文字
			if (_quickSlotLabels[slotIndex] != null)
			{
				if (isEmpty || isEmptyItem)
					_quickSlotLabels[slotIndex].Text = "";
				else
					_quickSlotLabels[slotIndex].Text = stack!.Item.DisplayName;
			}

			// 更新图标
			if (_quickSlotIcons[slotIndex] != null)
			{
				if (isEmpty || isEmptyItem)
				{
					_quickSlotIcons[slotIndex].Texture = null;
					_quickSlotIcons[slotIndex].Modulate = new Color(1, 1, 1, 0.3f);
				}
				else
				{
					_quickSlotIcons[slotIndex].Texture = stack!.Item.Icon;
					_quickSlotIcons[slotIndex].Modulate = Colors.White;
				}
			}

			// 更新投掷冷却遮罩（从槽位 stack 读取）
			UpdateThrowCooldownOverlay(slotIndex, stack);

			// 槽位内容变化（如放入武器）后同步刷新框架纹理，保证物品不被锁定遮罩盖住
			UpdateSlotFrames();
		}

		/// <summary>
		/// 更新左右手选择的快捷栏高亮（保存选中索引并刷新槽位框架纹理）
		/// </summary>
		/// <param name="leftHandSlotIndex">左手选择的槽位索引（0-4，-1表示未选择）</param>
		/// <param name="rightHandSlotIndex">右手选择的槽位索引（-1表示不高亮右手）</param>
		public void UpdateHandSlotHighlight(int leftHandSlotIndex, int rightHandSlotIndex = -1)
		{
			_leftHandSlotIndex = leftHandSlotIndex;
			_rightHandSlotIndex = rightHandSlotIndex;
			UpdateSlotFrames();
		}

		/// <summary>
		/// 刷新 5 个槽位的框架纹理（底图不含槽位图案，槽位背景由纹理层负责）：
		/// 锁定空槽 → 锁定遮罩（LockedFrameTexture）；锁定槽有物品 → 不遮掩（物品优先）；
		/// 解锁槽被选中 → 选中框（SelectedFrameTexture，替换空白方块）；
		/// 其余已解锁槽 → 空白方块（EmptySlotFrameTexture，持续显示在武器图标下方）。
		/// 解锁数量来自玩家 GetUnlockedWeaponSlots（初始 3，Build 每次升级 +1）。
		/// </summary>
		public void UpdateSlotFrames()
		{
			int unlocked = _player?.InventoryComponent?.GetUnlockedWeaponSlots() ?? 5;

			for (int i = 0; i < 5; i++)
			{
				var frame = _quickSlotFrames[i];
				if (frame == null) continue;

				bool hasItem = _player?.InventoryComponent?.QuickBar?.GetStack(i) is { } stack
					&& !stack.IsEmpty && stack.Item.ItemId != "empty_item";

				// 锁定遮罩只作用于空槽：有物品的槽位即使判定锁定也不遮掩
				if (i >= unlocked && !hasItem)
				{
					frame.Texture = LockedFrameTexture;
					continue;
				}

				bool selected = (i == _rightHandSlotIndex && _rightHandSlotIndex >= 0)
					|| (i == _leftHandSlotIndex && _leftHandSlotIndex >= 0);
				// 选中时用选中框替换空白方块，其余解锁槽持续显示空白方块
				frame.Texture = selected ? SelectedFrameTexture : EmptySlotFrameTexture;
			}
		}

		private void OnPlayerGoldChanged(int gold)
		{
			UpdateGoldDisplay(gold);
		}

		private void UpdateGoldDisplay(int gold)
		{
			if (GoldLabel != null)
			{
				GoldLabel.Text = $"{gold}";
			}
		}

		public override void _Process(double delta)
		{
			base._Process(delta);
			// 拖拽期间每帧强制抑制攻击：TrashBin 等 Button 的 MouseExited 会无条件置
			// IsMouseOverUI=false（UIManager 注册回调），拖拽中鼠标移出按钮时会被覆盖，需每帧兜底
			if (_draggingSlotIndex >= 0)
				UIManager.IsMouseOverUI = true;
			// 定期刷新投掷武器槽位 CD 遮罩
			_throwCooldownUpdateTimer -= (float)delta;
			if (_throwCooldownUpdateTimer <= 0f)
			{
				_throwCooldownUpdateTimer = ThrowCooldownUpdateInterval;
				for (int i = 0; i < 5; i++)
				{
					var qbStack = _player?.InventoryComponent?.QuickBar?.GetStack(i);
					if (qbStack != null)
						UpdateQuickBarSlot(i);
				}
			}
		}

		private void UpdateThrowCooldownOverlay(int slotIndex, InventoryItemStack? stack)
		{
			if (_quickSlotIcons[slotIndex] == null) return;

			if (stack != null && stack.IsThrowOnCooldown)
			{
				var overlay = GetOrCreateCooldownOverlay(slotIndex);
				float cd = stack.Item.ThrowWeaponCooldown;
				overlay.Progress = cd > 0f ? stack.ThrowCooldownRemaining / cd : 0f;
				overlay.Visible = true;
			}
			else
			{
				if (_quickSlotCooldownOverlays[slotIndex] != null)
					_quickSlotCooldownOverlays[slotIndex]!.Visible = false;
			}
		}

		/// <summary>
		/// 懒加载创建冷却遮罩节点（添加到图标节点的子节点，自动跟随尺寸）
		/// </summary>
		private ThrowCooldownOverlay GetOrCreateCooldownOverlay(int slotIndex)
		{
			if (_quickSlotCooldownOverlays[slotIndex] != null
				&& GodotObject.IsInstanceValid(_quickSlotCooldownOverlays[slotIndex]))
				return _quickSlotCooldownOverlays[slotIndex]!;

			var icon = _quickSlotIcons[slotIndex];
			var overlay = new ThrowCooldownOverlay();
			overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			overlay.OffsetLeft = 0; overlay.OffsetTop = 0;
			overlay.OffsetRight = 0; overlay.OffsetBottom = 0;
			icon.AddChild(overlay);
			_quickSlotCooldownOverlays[slotIndex] = overlay;
			return overlay;
		}
	}
}
