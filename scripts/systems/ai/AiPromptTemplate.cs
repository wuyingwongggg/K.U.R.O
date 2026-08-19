namespace Kuros.Systems.AI
{
    /// <summary>
    /// AI 决策提示词模板（手写文本段的可配置容器）：
    /// 由 AiDecisionBridge 的导出字段组装后传给 BuildGameStatePrompt——
    /// 所有手写提示词统一在 AiDecisionBridge 节点 Inspector 维护，代码只负责拼接（含动态段 Situation/GameState JSON）。
    /// </summary>
    public sealed class AiPromptTemplate
    {
        public const string DefaultRoleLine = "You are an in-game decision model for a fast-paced action game.";
        public const string DefaultTaskLine = "Return ONE executable decision for the current game state.";

        /// <summary>决策策略（每行一条规则）。</summary>
        public const string DefaultPolicy =
            "- Enemies present and hp not critical: prefer attack or use_skill.\n" +
            "- Retreat only when hp is very low or overwhelmed while under attack.\n" +
            "- Do not loot while enemies threaten the player.\n" +
            "- Avoid repeating the same intent when situation is unchanged.\n" +
            "- The reason must match Situation: distance wording and who is approaching must be consistent. Never say enemies are far when they are close.\n" +
            "- The reason must be immersive: refer to the enemy by its concrete features from Situation (type, name, description) instead of raw numbers. Never repeat hp percentages or pixel distances in the reason.";

        /// <summary>输出格式说明（含 intent/target 枚举说明）。</summary>
        public const string DefaultOutputFormat =
            "Output: strict JSON only, no markdown. Keys: intent, target, urgency(low/medium/high/critical), duration_seconds, reason.\n" +
            "intent: attack, retreat, reposition, loot, switch_weapon, use_skill. target: nearest_enemy, lowest_hp_enemy, safe_position, nearby_loot, none.";

        /// <summary>输出示例（单行 JSON）。</summary>
        public const string DefaultExample =
            "{\"intent\":\"attack\",\"target\":\"nearest_enemy\",\"urgency\":\"high\",\"duration_seconds\":1.2,\"reason\":\"Enemy in range, not under lethal threat.\"}";

        public string RoleLine { get; set; } = DefaultRoleLine;
        public string TaskLine { get; set; } = DefaultTaskLine;
        public string Policy { get; set; } = DefaultPolicy;
        public string OutputFormat { get; set; } = DefaultOutputFormat;
        public string Example { get; set; } = DefaultExample;
    }
}
