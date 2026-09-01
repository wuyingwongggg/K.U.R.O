using Godot;

namespace Kuros.Environments
{
    /// <summary>
    /// Gate 作为带血量的"伪敌人"驱动（伤害接收走 DamageDispatcher 统一管线）：
    ///
    /// 状态流程：
    ///   Normal  → gate_idle（循环）
    ///   受击          → gate_hit → gate_idle
    ///   HP ≤ BrokenThreshold → gate_broken → gate_broken_idle（循环）
    ///   Broken 受击   → gate_broken_hit → gate_broken_idle
    ///   HP = 0        → gate_knockback（终态，不再响应）
    ///
    /// 伤害接收：实现 TakeDamage(float)，由玩家攻击的可破坏目标通道驱动
    /// （SamplePlayer.DealDamageToDestructiblesViaShape → DamageDispatcher → Call("TakeDamage")）——
    /// 命中范围 = 玩家武器攻击范围（AttackArea），伤害 = 玩家攻击伤害（含武器倍率）。
    /// 挂载位置必须是 Gate 场景根节点（DamageDispatcher 只沿父链向上找伤害接收者）。
    /// 受击伤害信号由 DamageDispatcher.DealViaCall 广播（GameActor.AnyDamageTaken → 相机抖动/击打特效）。
    /// 一次性动画播放期间不接受新命中（_animLocked），避免动画被打断。
    /// </summary>
    public partial class GateController : CharacterBody2D
    {
        [ExportCategory("Health")]
        [Export(PropertyHint.Range, "1,100,1")] public int MaxHealth { get; set; } = 6;
        /// <summary>HP 降至此值时切换到 Broken 状态。</summary>
        [Export(PropertyHint.Range, "0,100,1")] public int BrokenThreshold { get; set; } = 3;

        [ExportCategory("Paths")]
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath("AnimationPlayer");

        private enum GatePhase { Normal, Broken, Dead }

        private AnimationPlayer? _animPlayer;
        private int _hp;
        private GatePhase _phase = GatePhase.Normal;
        private bool _animLocked;         // 一次性动画播放中，禁止注册新命中

        public override void _Ready()
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);

            if (_animPlayer == null)
            {
                GD.PushWarning($"[GateController] 未找到 AnimationPlayer，路径：{AnimationPlayerPath}");
                return;
            }

            _hp = MaxHealth;
            _animPlayer.AnimationFinished += OnAnimationFinished;
            PlayAnim("gate_idle");
        }

        public override void _ExitTree()
        {
            if (_animPlayer != null)
                _animPlayer.AnimationFinished -= OnAnimationFinished;
            base._ExitTree();
        }

        /// <summary>
        /// 伤害接收入口（DamageDispatcher 统一管线：玩家攻击命中可破坏目标时 Call 调用）。
        /// 命中范围 = 玩家武器攻击范围；伤害 = 玩家攻击伤害（含武器伤害倍率）。
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (_animLocked || _phase == GatePhase.Dead) return;

            _hp = Mathf.Max(0, _hp - Mathf.Max(1, Mathf.RoundToInt(damage)));

            // 伤害信号由 DamageDispatcher.DealViaCall 广播（相机抖动/击打特效），此处不重复

            if (_hp <= 0)
            {
                _phase = GatePhase.Dead;
                PlayAnim("gate_knockback");
                return;
            }

            if (_phase == GatePhase.Normal && _hp <= BrokenThreshold)
            {
                _phase = GatePhase.Broken;
                _animLocked = true;
                PlayAnim("gate_broken");
                return;
            }

            _animLocked = true;
            PlayAnim(_phase == GatePhase.Broken ? "gate_broken_hit" : "gate_hit");
        }

        // ── 动画结束回调 ──────────────────────────────────────────

        private void OnAnimationFinished(StringName animName)
        {
            _animLocked = false;
            switch (animName.ToString())
            {
                case "gate_hit":
                    PlayAnim("gate_idle");
                    break;
                case "gate_broken":
                case "gate_broken_hit":
                    PlayAnim("gate_broken_idle");
                    break;
                case "gate_knockback":
                    break;
            }
        }

        private void PlayAnim(string animName)
        {
            if (_animPlayer == null || !_animPlayer.HasAnimation(animName)) return;
            _animPlayer.Stop();
            _animPlayer.Play(animName);
        }
    }
}
