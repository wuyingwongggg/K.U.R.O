using Godot;
using Kuros.Systems.Cutscene;

namespace Kuros.Objects
{
    /// <summary>
    /// 通用过场动画控制器：处理 skip 跳转和动画完成后的自动切换。
    /// 挂载到任何有 AnimationPlayer 子节点的节点上即可使用。
    /// </summary>
    [GlobalClass]
    public partial class CutsceneAnimationController : Node
    {
        /// <summary>Intro 播放完成后自动切换到的循环动画</summary>
        [Export] public string LoopAnimation { get; set; } = "";

        /// <summary>Skip 触发时跳转到的动画（通常与 LoopAnimation 相同）</summary>
        [Export] public string SkipAnimation { get; set; } = "";

        /// <summary>Intro 动画名称（可选，为空则不自动播放）</summary>
        [Export] public string IntroAnimation { get; set; } = "";

        private AnimationPlayer? _animPlayer;
        private CutsceneManager? _cutsceneManager;
        private bool _hasSkipped;

        public override void _Ready()
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

            var managers = GetTree().GetNodesInGroup("cutscene_manager");
            if (managers.Count > 0)
                _cutsceneManager = managers[0] as CutsceneManager;

            if (_animPlayer != null && !string.IsNullOrEmpty(IntroAnimation))
            {
                if (_animPlayer.HasAnimation(IntroAnimation))
                {
                    _animPlayer.AnimationFinished += OnAnimationFinished;
                    _animPlayer.Play(IntroAnimation);
                }
                else
                {
                    GD.PushWarning($"[CutsceneAnimationController] {Name}: AnimationPlayer 缺少 '{IntroAnimation}' 动画。");
                }
            }
        }

        public override void _Process(double delta)
        {
            if (_hasSkipped || _cutsceneManager == null || !_cutsceneManager.IsSkipRequested)
                return;

            _hasSkipped = true;
            JumpToSkipAnimation();
        }

        private void OnAnimationFinished(StringName animName)
        {
            if (animName == IntroAnimation && !string.IsNullOrEmpty(LoopAnimation))
            {
                if (_animPlayer != null && _animPlayer.HasAnimation(LoopAnimation))
                    _animPlayer.Play(LoopAnimation);
            }
        }

        public void JumpToSkipAnimation()
        {
            if (_animPlayer == null) return;

            string target = !string.IsNullOrEmpty(SkipAnimation)
                ? SkipAnimation
                : LoopAnimation;

            if (!string.IsNullOrEmpty(target) && _animPlayer.HasAnimation(target))
                _animPlayer.Play(target);
        }
    }
}
