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
        /// <summary>标题节点（TextureRect/Label 均可），用于延时淡入 + 呼吸闪烁效果。</summary>
        [Export] public Control TitleNode { get; private set; } = null!;

        private bool _canProceed;

        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            PressAnyKeyLabel ??= GetNodeOrNull<Label>("%PressAnyKeyLabel");
            TitleNode ??= GetNodeOrNull<Control>("CenterContainer/VBox/Title");

            // 标题入场效果：延时 0.5s → 淡入 → 循环呼吸闪烁
            PlayTitleIntro();

            // Brief delay to prevent accidental skip from startup input
            GetTree().CreateTimer(0.5f).Timeout += () => _canProceed = true;
        }

        /// <summary>标题入场：先隐藏，0.5s 延时后淡入（0.6s），随后进入循环呼吸闪烁。</summary>
        private void PlayTitleIntro()
        {
            if (TitleNode == null) return;

            TitleNode.Modulate = new Color(1, 1, 1, 0);
            var intro = CreateTween();
            intro.TweenInterval(0.5f);
            intro.TweenProperty(TitleNode, "modulate:a", 1f, 0.6f);
            intro.TweenCallback(Callable.From(StartTitlePulse));
        }

        /// <summary>标题循环呼吸闪烁（透明度 1 → 0.7 → 1 循环）。</summary>
        private void StartTitlePulse()
        {
            if (TitleNode == null) return;
            var pulse = CreateTween();
            pulse.SetLoops(); // 无限循环
            pulse.TweenProperty(TitleNode, "modulate:a", 0.7f, 1.0f);
            pulse.TweenProperty(TitleNode, "modulate:a", 1f, 1.0f);
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
