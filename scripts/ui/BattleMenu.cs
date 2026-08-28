using System;
using Godot;
using Kuros.Managers;
using Kuros.Utils;

namespace Kuros.UI
{
    /// <summary>
    /// 战斗菜单 - 暂停菜单
    /// 通过ESC键打开/关闭
    /// </summary>
    public partial class BattleMenu : Control
    {
        private const string CompendiumScenePath = "res://scenes/ui/windows/CompendiumWindow.tscn";

        // 信号
        [Signal] public delegate void ResumeRequestedEventHandler();
        [Signal] public delegate void SettingsRequestedEventHandler();
        [Signal] public delegate void QuitRequestedEventHandler();
        [Signal] public delegate void ExitGameRequestedEventHandler();

        [ExportCategory("UI References")]
        [Export] public Button ResumeButton { get; private set; } = null!;
        [Export] public Button SettingsButton { get; private set; } = null!;
        [Export] public Button CompendiumButton { get; private set; } = null!;
        [Export] public Button QuitButton { get; private set; } = null!;
        [Export] public Button ExitButton { get; private set; } = null!;

        private bool _isOpen = false;
        private CompendiumWindow? _compendiumWindow;
        private PackedScene? _compendiumScene;

        // 缓存的窗口引用，避免每次ESC都遍历场景树
        private CompendiumWindow? _cachedCompendiumWindow;
        private SkillDetailWindow? _cachedSkillDetailWindow;
        private EnemySpawnConsoleWindow? _cachedEnemySpawnConsoleWindow;

        public bool IsOpen => _isOpen;

        /// <summary>
        /// 使用 Godot 原生 Connect 方法连接按钮信号
        /// 这种方式在导出版本中比 C# 委托方式更可靠
        /// </summary>
        private void ConnectButtonSignal(Button? button, string methodName)
        {
            if (button == null) return;
            
            // 使用 Godot 的 Connect 方法，这在导出版本中更可靠
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
            {
                button.Connect(Button.SignalName.Pressed, callable);
            }
        }

        public override void _Ready()
        {
            // 暂停时也要接收输入
            ProcessMode = ProcessModeEnum.Always;

            // 自动查找节点引用
            ResumeButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/ResumeButton");
            SettingsButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/SettingsButton");
            CompendiumButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/CompendiumButton");
            QuitButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/QuitButton");
            ExitButton ??= GetNodeOrNull<Button>("Window/WindowMargin/WindowVBox/ExitButton");

            // 使用 Godot 原生 Connect 方法连接信号，在导出版本中更可靠
            ConnectButtonSignal(ResumeButton, nameof(OnResumePressed));
            ConnectButtonSignal(SettingsButton, nameof(OnSettingsPressed));
            ConnectButtonSignal(CompendiumButton, nameof(OnCompendiumPressed));
            ConnectButtonSignal(QuitButton, nameof(OnQuitPressed));
            ConnectButtonSignal(ExitButton, nameof(OnExitGamePressed));

            LoadCompendiumWindow();

            // 缓存窗口引用，避免每次ESC都遍历场景树
            CacheWindowReferences();

            // 延迟确保隐藏（在UIManager设置可见之后）
            CallDeferred(MethodName.EnsureHidden);
        }

        public override void _Input(InputEvent @event)
        {
            // 如果对话正在进行，完全不处理任何输入，让对话窗口处理
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            {
                // 重要：不要调用 SetInputAsHandled()，让输入继续传播到 DialogueWindow
                // 直接返回，不处理任何输入
                return;
            }
            
            // 如果是 ESC 键，先检查物品栏是否打开
            bool isEscKey = false;
            if (@event.IsActionPressed("ui_cancel"))
            {
                isEscKey = true;
            }
            else if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
            {
                isEscKey = true;
            }
            
            if (isEscKey)
            {
                // 检查物品获得弹窗是否打开
                var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
                if (itemPopup != null && itemPopup.Visible)
                {
                    // 物品获得弹窗打开时，让弹窗自己处理ESC键
                    GD.Print("BattleMenu._Input: 物品获得弹窗打开，ESC键由弹窗处理，不拦截");
                    return;
                }
                
                // 检查设置菜单是否打开
                var settingsMenu = Kuros.Managers.UIManager.Instance?.GetUI<SettingsMenu>("SettingsMenu");
                if (settingsMenu != null && settingsMenu.Visible)
                {
                    // 设置菜单打开时，让设置菜单处理ESC键
                    GD.Print("BattleMenu._Input: 设置菜单打开，ESC键由设置菜单处理，不拦截");
                    return;
                }
                
                // 检查存档选择界面是否打开
                var saveSlotSelection = Kuros.Managers.UIManager.Instance?.GetUI<SaveSlotSelection>("SaveSlotSelection");
                if (saveSlotSelection != null && saveSlotSelection.Visible)
                {
                    // 存档选择界面打开时，让存档选择界面处理ESC键
                    GD.Print("BattleMenu._Input: 存档选择界面打开，ESC键由存档选择界面处理，不拦截");
                    return;
                }
                
                // 检查图鉴窗口是否打开
                bool compendiumOpen = IsCompendiumWindowOpen();
                
                if (compendiumOpen)
                {
                    // 图鉴窗口打开时，ESC键会被图鉴窗口处理，这里不处理
                    GD.Print("BattleMenu._Input: 图鉴窗口打开，ESC键由图鉴窗口处理，不拦截");
                    return; // 不处理，也不调用SetInputAsHandled，让图鉴窗口处理
                }
                
                // 检查技能详情窗口是否打开
                bool skillDetailOpen = IsSkillDetailWindowOpen();
                
                if (skillDetailOpen)
                {
                    // 技能详情窗口打开时，ESC键会被技能详情窗口处理，这里不处理
                    GD.Print("BattleMenu._Input: 技能详情窗口打开，ESC键由技能详情窗口处理，不拦截");
                    return; // 不处理，也不调用SetInputAsHandled，让技能详情窗口处理
                }
                
                // 检查敌人生成控制台窗口是否打开
                bool enemySpawnConsoleOpen = IsEnemySpawnConsoleWindowOpen();
                
                if (enemySpawnConsoleOpen)
                {
                    // 敌人生成控制台窗口打开时，ESC键由该窗口处理，这里不处理
                    GD.Print("BattleMenu._Input: 敌人生成控制台窗口打开，ESC键由该窗口处理，不拦截");
                    return;
                }
            }
            
            // 处理Return键（Enter）和ui_cancel（ESC）来打开/关闭菜单
            if (@event.IsActionPressed("Return") || isEscKey)
            {
                ToggleMenu();
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>
        /// 缓存窗口引用，避免每次ESC都遍历场景树
        /// </summary>
        private void CacheWindowReferences()
        {
            var root = GetTree().Root;
            if (root == null) return;

            // CompendiumWindow 已经在 _compendiumWindow 中缓存了
            _cachedCompendiumWindow = _compendiumWindow;

            // 查找 SkillDetailWindow
            var skillDetailWindows = FindAllNodesOfType<SkillDetailWindow>(root);
            _cachedSkillDetailWindow = skillDetailWindows.Count > 0 ? skillDetailWindows[0] : null;
        }

        /// <summary>
        /// 通用的泛型方法：在场景树中查找所有指定类型的节点
        /// </summary>
        /// <typeparam name="T">要查找的节点类型</typeparam>
        /// <param name="root">根节点</param>
        /// <param name="filter">可选的过滤条件</param>
        /// <returns>找到的所有节点列表</returns>
        private System.Collections.Generic.List<T> FindAllNodesOfType<T>(Node root, System.Func<T, bool>? filter = null) where T : class
        {
            var result = new System.Collections.Generic.List<T>();
            
            if (root == null) return result;

            // Check current node safely
            try
            {
                if (root is T node)
                {
                    if (filter == null || filter(node))
                    {
                        result.Add(node);
                    }
                }
            }
            catch (Exception ex) 
            { 
                GD.Print($"FindAllNodesOfType: Type check error for {root.Name}: {ex.Message}");
            }
            
            // Recursively check children safely
            // Use index loop instead of foreach to catch exceptions per child fetch
            int childCount = root.GetChildCount();
            for (int i = 0; i < childCount; i++)
            {
                Node? child = null;
                try
                {
                    child = root.GetChild(i);
                }
                catch
                {
                    // Failed to fetch child wrapper (e.g. SpineSprite without C# binding), skip it
                    continue;
                }

                if (child != null)
                {
                    try
                    {
                        result.AddRange(FindAllNodesOfType<T>(child, filter));
                    }
                    catch { /* Ignore recursive errors */ }
                }
            }
            
            return result;
        }

        /// <summary>
        /// <summary>
        /// 检查图鉴窗口是否打开（使用缓存）
        /// </summary>
        private bool IsCompendiumWindowOpen()
        {
            // CompendiumWindow 已经在 _compendiumWindow 中缓存了
            if (_cachedCompendiumWindow != null && IsInstanceValid(_cachedCompendiumWindow) && _cachedCompendiumWindow.Visible)
            {
                GD.Print($"BattleMenu.IsCompendiumWindowOpen: 找到打开的图鉴窗口，Visible={_cachedCompendiumWindow.Visible}");
                return true;
            }
            
            GD.Print("BattleMenu.IsCompendiumWindowOpen: 未找到打开的图鉴窗口");
            return false;
        }

        /// <summary>
        /// 检查技能详情窗口是否打开（使用缓存）
        /// </summary>
        private bool IsSkillDetailWindowOpen()
        {
            // 如果缓存无效，尝试重新查找
            if (_cachedSkillDetailWindow == null || !IsInstanceValid(_cachedSkillDetailWindow))
            {
                var root = GetTree().Root;
                if (root != null)
                {
                    var skillDetailWindows = FindAllNodesOfType<SkillDetailWindow>(root);
                    _cachedSkillDetailWindow = skillDetailWindows.Count > 0 ? skillDetailWindows[0] : null;
                }
            }

            if (_cachedSkillDetailWindow != null && _cachedSkillDetailWindow.Visible && _cachedSkillDetailWindow.IsOpen)
            {
                GD.Print($"BattleMenu.IsSkillDetailWindowOpen: 找到打开的技能详情窗口，Visible={_cachedSkillDetailWindow.Visible}, IsOpen={_cachedSkillDetailWindow.IsOpen}");
                return true;
            }
            
            GD.Print("BattleMenu.IsSkillDetailWindowOpen: 未找到打开的技能详情窗口");
            return false;
        }

        /// <summary>
        /// 检查敌人生成控制台窗口是否打开（使用缓存）
        /// </summary>
        private bool IsEnemySpawnConsoleWindowOpen()
        {
            if (_cachedEnemySpawnConsoleWindow == null || !IsInstanceValid(_cachedEnemySpawnConsoleWindow))
            {
                _cachedEnemySpawnConsoleWindow = UIManager.Instance?.GetUI<EnemySpawnConsoleWindow>("EnemySpawnConsoleWindow");
            }

            return _cachedEnemySpawnConsoleWindow != null && _cachedEnemySpawnConsoleWindow.Visible;
        }

        private void LoadCompendiumWindow()
        {
            _compendiumScene ??= GD.Load<PackedScene>(CompendiumScenePath);
            if (_compendiumScene == null)
            {
                GD.PrintErr("无法加载图鉴窗口场景：", CompendiumScenePath);
                return;
            }

            _compendiumWindow = _compendiumScene.Instantiate<CompendiumWindow>();
            AddChild(_compendiumWindow);
            // HideWindow() is called in CompendiumWindow._Ready(), so no need to call it here
        }


        public void OpenMenu()
        {
            if (_isOpen) return;

            Visible = true;
            _isOpen = true;
            
            // 请求暂停游戏
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PushPause();
            }

            // 确保菜单在弹窗之上显示
            EnsureMenuOnTop();
        }

        /// <summary>
        /// 确保菜单在弹窗之上显示
        /// </summary>
        private void EnsureMenuOnTop()
        {
            // 设置较高的ZIndex，确保菜单在弹窗之上
            ZIndex = 1000;

            // 将菜单移到父节点的最后，确保在场景树中也在所有其他UI之后
            // 这样即使ZIndex相同，菜单也会渲染在最上层
            var parent = GetParent();
            if (parent != null)
            {
                var currentIndex = GetIndex();
                var lastIndex = parent.GetChildCount() - 1;
                
                // 如果菜单不在最后，移到最后
                if (currentIndex < lastIndex)
                {
                    parent.MoveChild(this, lastIndex);
                    GD.Print("BattleMenu: 已将菜单移到最上层");
                }
            }
        }

        public void CloseMenu()
        {
            if (!_isOpen) return;

            Visible = false;
            _isOpen = false;
            
            // 取消暂停请求
            if (PauseManager.Instance != null)
            {
                PauseManager.Instance.PopPause();
            }
        }

        public void ToggleMenu()
        {
            if (_isOpen)
                CloseMenu();
            else
                OpenMenu();
        }

        private void EnsureHidden()
        {
            if (!_isOpen)
            {
                Visible = false;
            }
        }

        private void OnResumePressed()
        {
            EmitSignal(SignalName.ResumeRequested);
            CloseMenu();
        }

        private void OnSettingsPressed()
        {
            EmitSignal(SignalName.SettingsRequested);
        }

        private void OnQuitPressed()
        {
            // 先关闭菜单并取消暂停
            CloseMenu();
            EmitSignal(SignalName.QuitRequested);
        }

        private void OnExitGamePressed()
        {
            EmitSignal(SignalName.ExitGameRequested);
            GetTree().Quit();
        }

        private void OnCompendiumPressed()
        {
            if (_compendiumWindow == null)
            {
                GD.PrintErr("图鉴窗口未创建");
                return;
            }

            if (_compendiumWindow.Visible)
            {
                _compendiumWindow.HideWindow();
            }
            else
            {
                _compendiumWindow.ShowWindow();
            }
        }

    }
}
