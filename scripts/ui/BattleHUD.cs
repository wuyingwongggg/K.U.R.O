using Godot;
using Kuros.Actors.Heroes;
using Kuros.Actors.Heroes.States;
using Kuros.Core;
using Kuros.Systems.Inventory;
using Kuros.Items;
using Kuros.Managers;

namespace Kuros.UI
{
	/// <summary>
	/// 战斗HUD - 显示玩家状态、分数等信息
	/// 通过信号系统与游戏逻辑解耦
	/// </summary>
	public partial class BattleHUD : Control
	{
	[ExportCategory("UI References")]
	[Export] public Label PlayerStatsLabel { get; private set; } = null!;
	[Export] public Label InstructionsLabel { get; private set; } = null!;
	[Export] public ProgressBar HealthBar { get; private set; } = null!;
	// 生命值条内部用于遮罩数值的进度条（新节点），不改变 HealthBar 本身的贴图
	[Export] public TextureProgressBar? HealthFillBar { get; private set; } = null!;
	// 虚血条（深红）：受伤时停留后缓慢下降；治疗时停留后缓慢上升
	[Export] public TextureProgressBar? HurtBar { get; private set; } = null!;
	// 恢复条（青色）：治疗时立即显示恢复量（露出青色段），受伤时与红条同步下降
	[Export] public TextureProgressBar? RecoveryBar { get; private set; } = null!;
	// 叠加在生命条上的数字显示
	[Export] public Label? HealthValueLabel { get; private set; } = null!;
	[Export] public Label ScoreLabel { get; private set; } = null!;
	// 经验条：按 Build 阈值曲线显示当前等级区间内的进度（0-100），升级时自动归零
	[Export] public TextureProgressBar? ExpFillBar { get; private set; } = null!;
	[Export] public Label? ExpValueLabel { get; private set; } = null!;
	[Export] public Button PauseButton { get; private set; } = null!;
	[Export] public Label GoldLabel { get; private set; } = null!;

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
		// 暂停键按钮样式（例如使用“暂停键底”资源）
		[Export] public StyleBox? PauseButtonStyle { get; set; }

		[ExportCategory("Status Message")]
		// 提示锚点（归一化坐标），(0.5, 0.5) 为屏幕中心。
		[Export] public Vector2 StatusMessageAnchorNormalized { get; set; } = new Vector2(0.5f, 0.28f);
		// 在锚点基础上的像素偏移，便于微调最终显示位置。
		[Export] public Vector2 StatusMessagePixelOffset { get; set; } = Vector2.Zero;

		[ExportCategory("Default Items")]
		[Export] public ItemDefinition? DefaultSwordItem { get; set; } // 默认小木剑物品定义
		[Export] public bool SpawnDefaultSwordInQuickBar { get; set; } = false;
		private const string DefaultSwordItemPath = "res://data/DefaultSwordItem.tres";

		// 当前显示的数据
		private int _currentHealth = 100;
		private int _maxHealth = 100;
		private int _score = 0;
		// 虚血组件（玩家根节点下的 GhostHealthComponent，驱动三层血条动画）
		private GhostHealthComponent? _ghostComponent;

		// 物品栏相关
		private InventoryWindow? _inventoryWindow;
		private InventoryContainer? _inventoryContainer;
		private InventoryContainer? _quickBarContainer;
		
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
		
		// 小地图相关
		private Vector2 _mapSize = new Vector2(2000, 1500); // 地图总大小（可以根据实际地图调整）
		private Vector2 _minimapSize = new Vector2(200, 200); // 小地图显示大小

		// 颜色定义（双手装备等）
		private static readonly Color LeftHandColor = new Color(0.2f, 0.5f, 1.0f, 1.0f); // 蓝色
		private static readonly Color RightHandColor = new Color(1.0f, 0.8f, 0.0f, 1.0f); // 黄色
		private static readonly Color DefaultColor = new Color(0.3f, 0.3f, 0.3f, 1.0f); // 默认灰色

		// 信号：用于通知外部系统
		[Signal] public delegate void HUDReadyEventHandler();
		[Signal] public delegate void BattleMenuRequestedEventHandler();

		public override void _Ready()
		{
			// 添加到 "ui" 组，方便其他脚本查找
			AddToGroup("ui");
			// 如果没有在编辑器中分配，尝试自动查找
			if (PlayerStatsLabel == null)
			{
				PlayerStatsLabel = GetNodeOrNull<Label>("PlayerStats");
			}

			if (InstructionsLabel == null)
			{
				InstructionsLabel = GetNodeOrNull<Label>("Instructions");
			}

			if (HealthBar == null)
			{
				HealthBar = GetNodeOrNull<ProgressBar>("HealthBar");
			}

			if (HealthFillBar == null)
			{
				HealthFillBar = GetNodeOrNull<TextureProgressBar>("HealthBar/HealthFillBar");
			}

			if (HurtBar == null)
			{
				HurtBar = GetNodeOrNull<TextureProgressBar>("HealthBar/HurtBar");
			}

			if (RecoveryBar == null)
			{
				RecoveryBar = GetNodeOrNull<TextureProgressBar>("HealthBar/RecoveryBar");
			}

			ConfigureHealthFillBar();
			ConfigureGhostBars();

			if (HealthValueLabel == null)
			{
				HealthValueLabel = GetNodeOrNull<Label>("HealthBar/HealthValueLabel");
			}

			if (ScoreLabel == null)
			{
				ScoreLabel = GetNodeOrNull<Label>("ScoreLabel");
			}

			if (ExpFillBar == null)
			{
				ExpFillBar = GetNodeOrNull<TextureProgressBar>("TextureProgressBar/ExpFillBar");
			}

			if (ExpValueLabel == null)
			{
				ExpValueLabel = GetNodeOrNull<Label>("TextureProgressBar/ExpValueLabel");
			}

			if (PauseButton == null)
			{
				PauseButton = GetNodeOrNull<Button>("PauseButton");
			}

			if (GoldLabel == null)
			{
				GoldLabel = GetNodeOrNull<Label>("GoldContainer/GoldLabel");
			}

			// 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
			if (PauseButton != null)
			{
				var callable = new Callable(this, nameof(OnPauseButtonPressed));
				if (!PauseButton.IsConnected(Button.SignalName.Pressed, callable))
				{
					PauseButton.Connect(Button.SignalName.Pressed, callable);
				}
			}

			// 缓存快捷栏Label引用（必须在初始化物品栏之前）
			CacheQuickBarLabels();

			// 应用可自定义样式（快捷物品栏、金币、暂停键）
			ApplyCustomStyles();

			// 初始化物品栏
			InitializeInventory();

			// 初始化UI显示
			UpdateDisplay();

			// 延迟更新快捷栏显示，确保所有节点都已准备好
			CallDeferred(MethodName.UpdateQuickBarDisplay);

			// 尝试自动连接玩家（如果场景中已有玩家）
			CallDeferred(MethodName.TryAutoConnectPlayer);

			// 缓存 Dash 充能点指示器节点引用（节点本身在 BattleHUD.tscn 中布局）
			_dashIcon = GetNodeOrNull<DashIconControl>("DashIcon");

			// 发出就绪信号
			EmitSignal(SignalName.HUDReady);
		}

		private void TryAutoConnectPlayer()
		{
			// 尝试在场景树中查找玩家
			var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
			if (player != null)
			{
				ConnectPlayerInventory(player);
			}
		}

		private void CacheQuickBarLabels()
		{
			for (int i = 0; i < 5; i++)
			{
				_quickSlotLabels[i] = GetNodeOrNull<Label>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}/QuickSlotLabel{i + 1}");
				_quickSlotPanels[i] = GetNodeOrNull<Panel>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}");
				_quickSlotIcons[i] = GetNodeOrNull<TextureRect>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}/QuickSlotIcon{i + 1}");
				_quickSlotFrames[i] = GetNodeOrNull<TextureRect>($"QuickBarPanel/QuickBarContainer/QuickSlot{i + 1}/SlotFrame");

				if (_quickSlotLabels[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotLabel{i + 1}");
				}

				if (_quickSlotPanels[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotPanel{i + 1}");
				}

				if (_quickSlotIcons[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotIcon{i + 1}");
				}

				if (_quickSlotFrames[i] == null)
				{
					GD.PrintErr($"CacheQuickBarLabels: Failed to find QuickSlotFrame{i + 1}");
				}
			}
		}

		private void InitializeInventory()
		{
			// 创建物品栏容器
			_inventoryContainer = new InventoryContainer
			{
				Name = "PlayerInventory",
				SlotCount = 16
			};
			AddChild(_inventoryContainer);

			// 创建快捷栏容器
			_quickBarContainer = new InventoryContainer
			{
				Name = "QuickBar",
				SlotCount = 5
			};
			AddChild(_quickBarContainer);

		// 连接快捷栏变化信号
		// 注意：_quickBarContainer 在此方法中刚刚创建，且 _Ready() 只调用一次，因此无需检查重复订阅
		_quickBarContainer.SlotChanged += OnQuickBarSlotChanged;
		_quickBarContainer.InventoryChanged += OnQuickBarChanged;

			if (SpawnDefaultSwordInQuickBar)
			{
				// 可选：在快捷栏1（索引0）放置默认小木剑占位符
				ItemDefinition? swordItem = DefaultSwordItem;

				// 如果未设置，尝试加载默认资源
				if (swordItem == null)
				{
					swordItem = GD.Load<ItemDefinition>(DefaultSwordItemPath);
				}

				if (swordItem != null)
				{
					_quickBarContainer.TryAddItemToSlot(swordItem, 1, 0);

					// 立即更新快捷栏1的显示
					CallDeferred(MethodName.UpdateQuickBarSlot, 0);
				}
				else
				{
					GD.PrintErr("InitializeInventory: DefaultSwordItem is null and could not load from resource file. Please set DefaultSwordItem in the inspector or create the resource file.");
				}
			}
			
			// 初始化空白道具：填充快捷栏和物品栏的空槽位
			CallDeferred(MethodName.InitializeEmptyItems);

			// 通过UIManager加载物品栏窗口（放在GameUI层，在HUD之上）
			LoadInventoryWindow();
			UIManager.RegisterInteractiveChildren(this);
		}

		/// <summary>
		/// 加载物品栏窗口
		/// </summary>
		private void LoadInventoryWindow()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("BattleHUD: UIManager未初始化！");
				return;
			}

		_inventoryWindow = UIManager.Instance.LoadInventoryWindow();
		
		if (_inventoryWindow != null && _inventoryContainer != null && _quickBarContainer != null)
		{
			_inventoryWindow.SetInventoryContainer(_inventoryContainer, _quickBarContainer);
			_inventoryWindow.HideWindow();
		}
		else if (_inventoryWindow != null)
		{
			GD.PrintErr("BattleHUD: 无法设置物品栏容器，_inventoryContainer 或 _quickBarContainer 为 null");
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

		private void UpdateQuickBarSlot(int slotIndex)
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


			// u66f4u65b0u6807u7b7eu6587u5b57
			if (_quickSlotLabels[slotIndex] != null)
			{
				if (isEmpty || isEmptyItem)
					_quickSlotLabels[slotIndex].Text = "";
				else
					_quickSlotLabels[slotIndex].Text = stack!.Item.DisplayName;
			}

			// u66f4u65b0u56feu6807
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

			// u66f4u65b0u6295u63b7u51b7u5374u906eu7f69uff08u4eceu69fdu4f4d stack u8bfbu53d6uff09
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
		private void UpdateSlotFrames()
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

		/// <summary>
		/// 初始化空白道具：填充快捷栏和物品栏的空槽位
		/// </summary>
		private void InitializeEmptyItems()
		{
			var emptyItem = GD.Load<ItemDefinition>("res://data/EmptyItem.tres");
			if (emptyItem == null)
			{
				GD.PrintErr("BattleHUD.InitializeEmptyItems: Failed to load EmptyItem.tres");
				return;
			}
			
			// 填充快捷栏空槽位。若启用默认小木剑则跳过索引0。
			if (_quickBarContainer != null)
			{
				int startIndex = SpawnDefaultSwordInQuickBar ? 1 : 0;
				for (int i = startIndex; i < 5; i++)
				{
					var stack = _quickBarContainer.GetStack(i);
					if (stack == null || stack.IsEmpty)
					{
						_quickBarContainer.TryAddItemToSlot(emptyItem, 1, i);
					}
				}
			}
			
			// 填充物品栏空槽位
			if (_inventoryContainer != null)
			{
				for (int i = 0; i < _inventoryContainer.SlotCount; i++)
				{
					var stack = _inventoryContainer.GetStack(i);
					if (stack == null || stack.IsEmpty)
					{
						_inventoryContainer.TryAddItemToSlot(emptyItem, 1, i);
					}
				}
			}
		}
		
		/// <summary>
		/// 连接玩家物品栏组件，设置快捷栏引用
		/// </summary>
		public void ConnectPlayerInventory(SamplePlayer player)
		{
			if (player?.InventoryComponent != null && _quickBarContainer != null)
			{
				player.InventoryComponent.SetQuickBar(_quickBarContainer);
				
				// 确保玩家连接快捷栏信号，以便左手物品与选中槽位严格对应
				player.ConnectQuickBarSignals();
				
				// 初始化左手选择（只在还没有选中时才初始化，避免覆盖用户选择）
				// 注意：使用 CallDeferred 确保在快捷栏连接完成后再初始化
				player.CallDeferred(SamplePlayer.MethodName.InitializeLeftHandSelection);
				
				// 延迟更新高亮，保留未选择状态（-1），避免把第一格误高亮。
				int highlightIndex = (player.LeftHandSlotIndex >= 0 && player.LeftHandSlotIndex < 5)
					? player.LeftHandSlotIndex
					: -1;
				CallDeferred(MethodName.UpdateHandSlotHighlight, highlightIndex, -1);
			}
			else
			{
				GD.PrintErr($"BattleHUD.ConnectPlayerInventory: Failed - Player={player != null}, InventoryComponent={player?.InventoryComponent != null}, QuickBarContainer={_quickBarContainer != null}");
			}
		}

		/// <summary>
		/// 供外部或UI控件调用以请求打开战斗菜单
		/// </summary>
		public void RequestBattleMenu()
		{
			EmitSignal(SignalName.BattleMenuRequested);
		}

		/// <summary>
		/// 暂停按钮点击处理
		/// </summary>
		private void OnPauseButtonPressed()
		{
			RequestBattleMenu();
		}

		/// <summary>
		/// 更新玩家状态
		/// </summary>
		public void UpdateStats(int health, int maxHealth, int score)
		{
			_currentHealth = health;
			_maxHealth = maxHealth;
			_score = score;
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			int safeMaxHealth = Mathf.Max(1, _maxHealth);
			int safeHealth = Mathf.Clamp(_currentHealth, 0, safeMaxHealth);

			if (HealthBar != null)
			{
				// 保持 HealthBar 作为你在编辑器中布置好的容器/图标，不去修改其贴图
				HealthBar.MaxValue = safeMaxHealth;
				HealthBar.Value = safeHealth;
			}

			// 使用单独的遮罩进度条来表现生命值长度（动画值由 GhostHealthComponent 驱动，此处只同步基线）
			if (HealthFillBar != null)
			{
				HealthFillBar.MinValue = 0;
				HealthFillBar.MaxValue = safeMaxHealth;
				HealthFillBar.Value = _ghostComponent != null ? _ghostComponent.FillValue : safeHealth;
			}

			// 在生命条中央叠加数值显示
			if (HealthValueLabel != null)
			{
				HealthValueLabel.Text = $"{safeHealth}/{safeMaxHealth}";
			}
			if (ScoreLabel != null)
			{
				// 只显示数字，便于与图标/背景组合
				ScoreLabel.Text = $"{_score}";
			}
			UpdateExpDisplay(_score);
			// Build 升级（分数跨阈值）时刷新武器槽解锁框
			UpdateSlotFrames();
			// PlayerStatsLabel 保留原逻辑用于调试/回退需要
		}

		/// <summary>
		/// 按 Build 阈值曲线更新经验条：显示当前等级区间内的进度（0-100），
		/// 跨越阈值时等级 +1 且进度自动归零（新的等级区间起点）。
		/// 曲线取 BuildSelectionManager 的 ThresholdCurve，与 Build 三选一的触发曲线一致。
		/// </summary>
		private void UpdateExpDisplay(int score)
		{
			if (ExpFillBar == null) return;

			var curve = BuildSelectionManager.Instance?.ThresholdCurve;
			if (curve == null)
			{
				ExpFillBar.Value = 0f;
				return;
			}

			int triggerCount = curve.GetTriggerCount(score);      // 已触发的次数（累计阈值已达标数）
			int level = triggerCount + 1;                         // 当前等级（第 1 次触发前为 LV 1）
			int start = curve.GetCumulativeScore(triggerCount);   // 本等级起点总分
			int end = curve.GetCumulativeScore(triggerCount + 1); // 升级所需总分
			float progress = end > start
				? Mathf.Clamp((score - start) / (float)(end - start), 0f, 1f)
				: 0f;

			ExpFillBar.MaxValue = 100f;
			ExpFillBar.Value = progress * 100f;

			if (ExpValueLabel != null)
			{
				ExpValueLabel.Text = $"LV {level}";
			}
		}

		private void ConfigureHealthFillBar()
		{
			if (HealthFillBar == null)
			{
				return;
			}

			HealthFillBar.FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight;
			HealthFillBar.MinValue = 0;
			HealthFillBar.MaxValue = Mathf.Max(1, _maxHealth);
			HealthFillBar.Value = Mathf.Clamp(_currentHealth, 0, Mathf.Max(1, _maxHealth));
		}

		/// <summary>初始化虚血条/恢复条（LeftToRight + 初始 0 值，防止 tscn 默认 100 闪出错误全条）。</summary>
		private void ConfigureGhostBars()
		{
			if (HurtBar != null)
			{
				HurtBar.FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight;
				HurtBar.MinValue = 0;
				HurtBar.MaxValue = 1;
				HurtBar.Value = 0;
			}

			if (RecoveryBar != null)
			{
				RecoveryBar.FillMode = (int)TextureProgressBar.FillModeEnum.LeftToRight;
				RecoveryBar.MinValue = 0;
				RecoveryBar.MaxValue = 1;
				RecoveryBar.Value = 0;
			}
		}

		/// <summary>
		/// 应用战斗 UI 的可自定义样式：快捷物品栏、金币文本、暂停键按钮等。
		/// 仅在对应 StyleBox 被设置时才覆盖，避免破坏你在编辑器中已有的视觉配置。
		/// </summary>
		private void ApplyCustomStyles()
		{
			// 快捷物品栏整体面板：武器栏底（与金币/暂停同样方式，默认贴图）
			var quickBarPanel = GetNodeOrNull<Panel>("QuickBarPanel");
			if (quickBarPanel != null)
			{
				StyleBox? panelStyle = QuickBarPanelStyle;
				if (panelStyle == null)
				{
					var tex = GD.Load<Texture2D>("res://resources/ui/武器栏底.png");
					if (tex != null)
					{
						var stb = new StyleBoxTexture();
						stb.Texture = tex;
						panelStyle = stb;
					}
				}
				if (panelStyle != null)
					quickBarPanel.AddThemeStyleboxOverride("panel", panelStyle);
			}

			// 槽位框架（解锁框/选中框）由 CacheQuickBarLabels 加载、UpdateSlotFrames 刷新，此处不再设置面板样式

			// 金币文本样式
			if (GoldLabel != null && GoldLabelStyle != null)
			{
				GoldLabel.AddThemeStyleboxOverride("normal", GoldLabelStyle);
			}

			// 金币图标：使用 金币.png，点采样避免透明边灰圈
			var goldIcon = GetNodeOrNull<TextureRect>("GoldContainer/GoldIcon");
			if (goldIcon != null)
			{
				goldIcon.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
			}

			// 暂停键：透明 StyleBox + 用 TextureRect 显示 暂停.png 并设点采样，去掉透明边缘的灰圈
			if (PauseButton != null)
			{
				Texture2D? pauseTex = null;
				if (PauseButtonStyle is StyleBoxTexture st && st.Texture != null)
				{
					pauseTex = st.Texture;
				}
				else
				{
					pauseTex = GD.Load<Texture2D>("res://resources/ui/暂停.png");
				}

				var transparentStyle = new StyleBoxFlat();
				transparentStyle.BgColor = new Color(0, 0, 0, 0);
				transparentStyle.BorderWidthLeft = 0;
				transparentStyle.BorderWidthTop = 0;
				transparentStyle.BorderWidthRight = 0;
				transparentStyle.BorderWidthBottom = 0;

				PauseButton.AddThemeStyleboxOverride("normal", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("hover", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("pressed", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("disabled", transparentStyle);
				PauseButton.AddThemeStyleboxOverride("focus", transparentStyle);

				if (pauseTex != null)
				{
					// 不再改按钮的尺寸和位置，完全沿用场景里 PauseButton 的布局（= 节点预览中的大小）
					PauseButton.Icon = null;
					var iconRect = PauseButton.GetNodeOrNull<TextureRect>("PauseIconRect");
					if (iconRect == null)
					{
						iconRect = new TextureRect
						{
							Name = "PauseIconRect",
							MouseFilter = Control.MouseFilterEnum.Ignore,
							ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
							StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
						};
						PauseButton.AddChild(iconRect);
					}
					iconRect.Texture = pauseTex;
					iconRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
					iconRect.OffsetLeft = 0;
					iconRect.OffsetTop = 0;
					iconRect.OffsetRight = 0;
					iconRect.OffsetBottom = 0;
					iconRect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
				}
			}
		}

		/// <summary>
		/// 连接到任意 GameActor（可选实现 IPlayerStatsSource）
		/// </summary>
		public void AttachActor(GameActor actor)
		{
			if (actor is SamplePlayer samplePlayer)
			{
				_player = samplePlayer;
				_ghostComponent = samplePlayer.GetNodeOrNull<GhostHealthComponent>("GhostHealthComponent");

				// 使用 C# 事件驱动血量/分数更新，避免仅依赖未触发的 Godot 信号。
				samplePlayer.StatsUpdated -= OnPlayerStatsUpdated;
				samplePlayer.StatsUpdated += OnPlayerStatsUpdated;

				// 订阅底层 HealthChanged（RestoreHealth 只触发此事件，不触发 StatsUpdated）
				samplePlayer.HealthChanged -= OnHealthChanged;
				samplePlayer.HealthChanged += OnHealthChanged;
				
				// 连接玩家状态变化信号
				if (!samplePlayer.IsConnected(SamplePlayer.SignalName.StatsChanged, new Callable(this, MethodName.OnPlayerStatsChanged)))
				{
					samplePlayer.StatsChanged += OnPlayerStatsChanged;
				}
				
				// 连接玩家金币变化信号
				if (!samplePlayer.IsConnected(SamplePlayer.SignalName.GoldChanged, new Callable(this, MethodName.OnPlayerGoldChanged)))
				{
					samplePlayer.GoldChanged += OnPlayerGoldChanged;
				}
				
				// 初始化金币显示
				UpdateGoldDisplay(samplePlayer.GetGold());

				// 绑定时立即刷新一次生命值，避免必须等待下一次事件才更新。
				UpdateStats(samplePlayer.CurrentHealth, samplePlayer.MaxHealth, samplePlayer.Score);
				
				// 连接玩家物品栏组件
				ConnectPlayerInventory(samplePlayer);
				_dashState = samplePlayer.StateMachine?.GetNodeOrNull<PlayerDashState>("Dash");
			}
		}

		/// <summary>
		/// 断开与玩家的连接
		/// </summary>
		public void DisconnectFromPlayer(GameActor player)
		{
			if (player is SamplePlayer samplePlayer)
			{
				samplePlayer.StatsUpdated -= OnPlayerStatsUpdated;
				samplePlayer.HealthChanged -= OnHealthChanged;

				if (samplePlayer.IsConnected(SamplePlayer.SignalName.StatsChanged, new Callable(this, MethodName.OnPlayerStatsChanged)))
				{
					samplePlayer.StatsChanged -= OnPlayerStatsChanged;
				}
				
				if (samplePlayer.IsConnected(SamplePlayer.SignalName.GoldChanged, new Callable(this, MethodName.OnPlayerGoldChanged)))
				{
					samplePlayer.GoldChanged -= OnPlayerGoldChanged;
				}
			}
		}

		/// <summary>
		/// 断开与角色的连接（别名方法，用于兼容性）
		/// </summary>
		public void DetachActor(GameActor actor)
		{
			DisconnectFromPlayer(actor);
		}

		/// <summary>
		/// 设置回退状态（当没有连接玩家时使用）
		/// </summary>
		public void SetFallbackStats()
		{
			UpdateStats(100, 100, 0);
		}

		/// <summary>
		/// 在屏幕中央偏上位置动态弹出一条短暂状态消息，向上漂移后淡出消失。
		/// 不依赖 InstructionsLabel 是否在场景中绑定。
		/// </summary>
		public void ShowStatusMessage(string message, float durationSeconds = 2.0f)
		{
			// 若 InstructionsLabel 已连接，同步写入（兼容旧行为）
			if (InstructionsLabel != null)
			{
				InstructionsLabel.Text = message;
				InstructionsLabel.Modulate = Colors.White;

				var tweenLabel = CreateTween();
				tweenLabel.TweenInterval(Mathf.Max(0.1f, durationSeconds - 0.4f));
				tweenLabel.TweenProperty(InstructionsLabel, "modulate", new Color(1f, 1f, 1f, 0f), 0.4f);
				tweenLabel.TweenCallback(Callable.From(() =>
				{
					if (IsInstanceValid(InstructionsLabel))
						InstructionsLabel.Text = string.Empty;
				}));
			}

			// 动态创建浮动提示标签
			var popup = new Label
			{
				Text = message,
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(1f, 0.95f, 0.3f, 1f),   // 醒目黄色
				MouseFilter = Control.MouseFilterEnum.Ignore,
				ZIndex = 9999,
			};
			popup.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.3f, 1f));
			popup.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.95f));
			popup.AddThemeFontSizeOverride("font_size", 18);
			popup.AddThemeConstantOverride("outline_size", 4);

			AddChild(popup);

			// 定位到可配置锚点位置
			popup.ResetSize();
			var vpSize = GetViewport().GetVisibleRect().Size;
			var anchor = new Vector2(
				Mathf.Clamp(StatusMessageAnchorNormalized.X, 0f, 1f),
				Mathf.Clamp(StatusMessageAnchorNormalized.Y, 0f, 1f));
			var anchorPos = new Vector2(vpSize.X * anchor.X, vpSize.Y * anchor.Y);
			popup.Position = new Vector2(anchorPos.X - popup.Size.X * 0.5f, anchorPos.Y - popup.Size.Y * 0.5f) + StatusMessagePixelOffset;

			var tween = CreateTween();
			// 先停留，再向上漂移同时淡出
			tween.TweenInterval(Mathf.Max(0.05f, durationSeconds - 0.6f));
			tween.Parallel().TweenProperty(popup, "position", popup.Position + new Vector2(0f, -36f), 0.6f);
			tween.Parallel().TweenProperty(popup, "modulate", new Color(1f, 0.95f, 0.3f, 0f), 0.6f);
			tween.TweenCallback(Callable.From(() =>
			{
				if (IsInstanceValid(popup))
					popup.QueueFree();
			}));
		}

		// Dash 充能点指示器（节点在 BattleHUD.tscn 中布局，这里只缓存引用）
	private DashIconControl? _dashIcon;
	private PlayerDashState? _dashState;
	private float _dashUpdateTimer;

	private SamplePlayer? _player;
		
		/// <summary>
		/// 设置玩家引用（用于获取最大生命值等属性）
		/// </summary>
		public void SetPlayer(SamplePlayer playerRef)
		{
			_player = playerRef;
			if (_player != null)
			{
				int maxHealth = _player.MaxHealth;
				int score = _player is IPlayerStatsSource statsSource ? statsSource.Score : _score;
				UpdateStats(_player.CurrentHealth, maxHealth, score);
			}
		}

		/// <summary>
		/// 处理玩家状态变化信号
		/// </summary>
		private void OnPlayerStatsChanged(int health, int score)
		{
			// 从玩家获取最大生命值
			int maxHealth = _player?.MaxHealth ?? 100;
			UpdateStats(health, maxHealth, score);
		}

		private void OnPlayerStatsUpdated(int health, int maxHealth, int score)
		{
			UpdateStats(health, maxHealth, score);
		}

		/// <summary>
		/// 处理 GameActor.HealthChanged（RestoreHealth 只触发此事件）
		/// </summary>
		private void OnHealthChanged(int health, int maxHealth)
		{
			UpdateStats(health, maxHealth, _score);
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
			UpdateGhostHealthBars();
			UpdateDashDisplay((float)delta);
			// u5b9au671fu5237u65b0u6295u63b7u6b66u5668u69fdu4f4d CD u906eu7f69
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

		/// <summary>
		/// 每帧驱动三层血条：HealthFillBar（红，虚血组件动画值）、RecoveryBar（青，当前血量）、
		/// HurtBar（深红，虚血显示值）。RecoveryBar 治疗时立即=当前血量露出恢复段；无组件时回退为当前血量。
		/// </summary>
		private void UpdateGhostHealthBars()
		{
			if (_player == null) return;

			float maxH = Mathf.Max(1f, _player.MaxHealth);
			float currentH = Mathf.Max(0f, _player.CurrentHealth);

			if (HealthFillBar != null)
			{
				HealthFillBar.MaxValue = maxH;
				HealthFillBar.Value = _ghostComponent != null ? _ghostComponent.FillValue : currentH;
			}

			if (RecoveryBar != null)
			{
				RecoveryBar.MaxValue = maxH;
				RecoveryBar.Value = currentH;
			}

			if (HurtBar != null)
			{
				HurtBar.MaxValue = maxH;
				HurtBar.Value = _ghostComponent != null ? _ghostComponent.HurtDisplay : currentH;
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

		public override void _UnhandledInput(InputEvent @event)
		{
			if (@event.IsActionPressed("open_inventory"))
			{
				if (_inventoryWindow != null)
				{
					if (_inventoryWindow.Visible)
					{
						_inventoryWindow.HideWindow();
					}
					else
					{
						_inventoryWindow.ShowWindow();
					}
					GetViewport().SetInputAsHandled();
				}
			}
		}
	private void UpdateDashDisplay(float delta)
	{
		if (_dashState == null || _dashIcon == null) return;

		_dashUpdateTimer -= delta;
		if (_dashUpdateTimer > 0f) return;
		_dashUpdateTimer = 0.05f;

		// 充能点：左侧 Charges 个亮色（可用），其余暗色（CD），从右往左变化
		_dashIcon.SetCharges(_dashState.Charges, _dashState.MaxCharges);
	}
	}
}
