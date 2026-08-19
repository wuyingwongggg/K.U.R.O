namespace Kuros.Core
{
    /// <summary>
    /// 输入动作名称常量。
    ///
    /// 目的：消除代码中散落的硬编码输入动作字符串（如 "core_skill"）。
    /// 新增按键时在此加常量，同时在 project.godot 的 [input] 段添加映射。
    ///
    /// 使用方式：
    ///   if (Input.IsActionPressed(InputActions.CoreSkill)) { ... }
    ///
    /// 当前使用处：
    ///   MachineCoreEffect — 监听核心技能键（F 键）触发过热释放
    ///   ThrowCoreEffect  — 监听核心技能键触发家具投掷
    ///   WaiterCoreEffect — 监听核心技能键触发药物使用
    /// </summary>
    public static class InputActions
    {
        /// <summary>核心技能键，默认映射到 F 键（project.godot 中 physical_keycode=70）。</summary>
        public const string CoreSkill = "core_skill";

        /// <summary>
        /// 设置菜单可改键的动作白名单（排除系统动作 ui_cancel / Return / dialogic_default_action）。
        /// take_up = 右键拾取武器（短按）/长按放置（长按），长短按由 InputHoldTracker 阈值区分，改键自动跟随。
        /// 每个动作：(动作名, 显示中文名)。
        /// </summary>
        public static readonly (string Action, string DisplayName)[] RebindableActions =
        {
            ("move_left", "向左移动"),
            ("move_right", "向右移动"),
            ("move_forward", "向上移动"),
            ("move_back", "向下移动"),
            ("run", "奔跑"),
            ("attack", "攻击"),
            ("throw", "投掷"),
            ("take_up", "拾取"),
            ("place", "放置"),
            ("interact", "交互"),
            ("dash", "闪避"),
            ("item_select_left", "物品栏左选"),
            ("item_select_right", "物品栏右选"),
            ("item_use", "使用物品"),
            ("open_inventory", "打开背包"),
            (CoreSkill, "核心技能"),
        };

        /// <summary>获取动作的中文显示名（白名单外返回原动作名）。</summary>
        public static string GetDisplayName(string action)
        {
            foreach (var (act, name) in RebindableActions)
            {
                if (act == action) return name;
            }
            return action;
        }
    }
}
