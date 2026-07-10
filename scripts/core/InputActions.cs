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
    }
}
