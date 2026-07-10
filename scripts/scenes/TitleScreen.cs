using Godot;

namespace Kuros.Scenes
{
    /// <summary>
    /// 游戏启动画面 —— "按任意键开始"。
    ///
    /// 流程：project.godot → TitleScreen → (任意键) → NextScenePath（默认 MainMenu.tscn）
    ///
    /// 0.5 秒延迟（_canProceed）防止启动误触跳过。
    /// "按任意键开始" 文字通过正弦波透明度实现闪烁效果。
    /// 支持键盘、鼠标、手柄任意输入触发场景切换。
    /// </summary>
    public partial class TitleScreen : Control
    {
        [Export] public string NextScenePath = "res://scenes/ui/menus/MainMenu.tscn";
        [Export] public Label PressAnyKeyLabel { get; private set; } = null!;

        private bool _canProceed;

        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            PressAnyKeyLabel ??= GetNodeOrNull<Label>("%PressAnyKeyLabel");

            // Brief delay to prevent accidental skip from startup input
            GetTree().CreateTimer(0.5f).Timeout += () => _canProceed = true;
        }

        public override void _Process(double delta)
        {
            if (PressAnyKeyLabel == null) return;
            // Blink effect: toggle visibility every ~0.6s
            float alpha = Mathf.Abs(Mathf.Sin((float)Time.GetTicksMsec() / 600f));
            PressAnyKeyLabel.Modulate = new Color(1, 1, 1, alpha);
        }

        public override void _Input(InputEvent @event)
        {
            if (!_canProceed || !IsVisibleInTree()) return;

            bool shouldProceed = @event switch
            {
                InputEventKey key when key.Pressed => true,
                InputEventMouseButton mouse when mouse.Pressed => true,
                InputEventJoypadButton joy when joy.Pressed => true,
                _ => false,
            };

            if (shouldProceed)
            {
                GetTree().ChangeSceneToFile(NextScenePath);
            }
        }
    }
}
