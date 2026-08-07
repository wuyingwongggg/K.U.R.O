using Godot;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 激光笔专属攻击：长按进入 Active 阶段循环播放期间，按玩家移动方向实时切换
    /// 站立 / 前进(fwd) / 后退(bwd) 三种 Spine 动画。三动画阶段时长一致，仅动作不同。
    /// 方向规则：移动方向 == 面朝方向 → fwd；反向 → bwd；X 在 deadzone 内 → 站立。
    /// 全程不翻转角色（Active 期间状态机无翻转源，天然保持面朝）。
    /// 切换动画时必须同步 Spine hit 事件匹配名，否则新动画的 hit 事件被过滤、伤害消失。
    /// Active 期间移动时按移动方向接管位移：移动速度 = 当前实际速度 × MoveSpeedPercent%。
    /// </summary>
    public partial class PlayerLaserPointerAttack : PlayerBasicMeleeAttack
    {
        [Export] public string ForwardAnimationName = "Weapon_Stab_LaserPointer_fwd";
        [Export] public string BackwardAnimationName = "Weapon_Stab_LaserPointer_bwd";
        [Export(PropertyHint.Range, "0,0.5,0.01")] public float InputDeadzone = 0.1f;
        /// <summary>Active 期间移动时的移动速度倍率（当前实际速度的 N%）。100 = 不变，150 = ×1.5，0 = 不动。</summary>
        [Export(PropertyHint.Range, "0,300,0.01")] public float MoveSpeedPercent = 100f;

        private string _lastDirectionalAnimation = string.Empty; // 已播放的方向动画（去重，同名不重播）
        private bool _loopActive;                                 // Active 循环接管标志

        protected override void OnActivePhase()
        {
            base.OnActivePhase();
            if (Player is not MainCharacter) return; // 仅 Spine 主角色路径有 hit 事件循环机制

            _loopActive = true;
            _lastDirectionalAnimation = string.Empty;
            PlayDirectional(ResolveDirectionalAnimation());
        }

        protected override void OnTick(double delta)
        {
            base.OnTick(delta);
            if (!_loopActive) return;
            if (!IsInActivePhase)
            {
                _loopActive = false; // 退出 Active 停止接管，Warmup/Recovery 走基类原有逻辑
                return;
            }

            UpdateMoveVelocity();

            string target = ResolveDirectionalAnimation();
            if (target == _lastDirectionalAnimation) return; // 动画名未变：不重播
            PlayDirectional(target);
            _lastDirectionalAnimation = target;
        }

        protected override void OnAttackFinished()
        {
            // 攻击结束/被打断后停止接管位移，Velocity 归零（状态机不再 Tick 本模板）
            _loopActive = false;
            Player.Velocity = Vector2.Zero;
            base.OnAttackFinished();
        }

        /// <summary>Active 期间直接接管 Velocity（同 PlayerBrawlRiotBracerAttack 的位移模式：
        /// 攻击状态下玩家移动不读 Speed 属性，必须由模板每帧设置 Velocity 供状态机 MoveAndSlide）。
        /// 仅 X 轴移动（Y 输入忽略，与 fwd/bwd 动画判定的 X 轴规则一致）；
        /// 有 X 输入 → 以「当前实际速度 ×N%」沿水平方向移动；无 X 输入 → 停下。</summary>
        private void UpdateMoveVelocity()
        {
            if (Player == null) return;
            Vector2 input = Player.GetControlledMovementInput();
            input.Y = 0f; // 限制仅 X 轴
            if (input.LengthSquared() <= 0.01f)
            {
                Player.Velocity = Vector2.Zero;
                return;
            }

            float speed = Player.Speed * (MoveSpeedPercent / 100f);
            Player.Velocity = input.Normalized() * speed;
        }

        /// <summary>播放方向动画：若目标已是当前播放的动画则跳过（防重播，站立动画进 Active 时保持原播放位置）；
        /// 否则用 PlaySpineAnimationFrom 跳帧到 Warmup 段结束处（= Active 段起点），
        /// 三动画共用同一 timing，从开头播放会重复前摇动作、与站立动画的 Active 段错位。
        /// timeScale 必须传当前 Active 阶段速度，否则播放速度被重置为 1×，与阶段计时错位（加减攻速后尤甚）。</summary>
        private void PlayDirectional(string target)
        {
            if (Player is not MainCharacter mc) return;
            if (target == mc.CurrentAnimationName) return; // 已在该动画：不重播
            mc.PlaySpineAnimationFrom(target, ResolveWarmupAnimationTime(), loop: true,
                ResolveActiveAnimationSpeed());
            SetSpineAttackAnimation(target);
        }

        /// <summary>按面朝与移动输入解析目标动画：同向 → fwd，反向 → bwd，静止 → 站立动画。</summary>
        private string ResolveDirectionalAnimation()
        {
            float x = Player.GetControlledMovementInput().X;
            bool forward = Player.FacingRight ? x > InputDeadzone : x < -InputDeadzone;
            bool backward = Player.FacingRight ? x < -InputDeadzone : x > InputDeadzone;
            if (forward) return ForwardAnimationName;
            if (backward) return BackwardAnimationName;
            return _resolvedAnimationName;
        }
    }
}
