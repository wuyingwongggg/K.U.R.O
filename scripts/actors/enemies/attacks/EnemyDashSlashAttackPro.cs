using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// DashSlash 强化版（半血升级，参考 netAdmin MultiMelee 取代 SimpleMelee 的触发逻辑）：
    /// 冲刺阶段不再直追玩家，而是围绕玩家旋转 OrbitDurationSeconds 秒，
    /// 之后绕到玩家背后（玩家朝向的反方向）冲刺收束并挥砍。
    /// 其余行为（检测/命中/击退/冷却/残影动画）继承 EnemyDashSlashAttack。
    /// </summary>
    public partial class EnemyDashSlashAttackPro : EnemyDashSlashAttack
    {
        [ExportCategory("Orbit")]
        /// <summary>环绕半径（像素）：冲刺时与玩家保持的距离。</summary>
        [Export(PropertyHint.Range, "80,600,10")] public float OrbitRadius = 260f;
        /// <summary>环绕时长（秒）：持续 N 秒后转入背刺冲刺。</summary>
        [Export(PropertyHint.Range, "0.2,10,0.1")] public float OrbitDurationSeconds = 1.5f;
        /// <summary>环绕切向推力倍率（1 = 切向速度与冲刺速度一致；与径向弹簧合力归一化后生效）。</summary>
        [Export(PropertyHint.Range, "0.2,3,0.1")] public float OrbitSpeedFactor = 1f;
        /// <summary>背刺目标点：玩家背后（背向朝向）的偏移距离。</summary>
        [Export(PropertyHint.Range, "0,400,10")] public float BackStrikeOffset = 120f;
        /// <summary>进入背刺冲刺后，距离背刺点小于此值即收束冲刺。</summary>
        [Export(PropertyHint.Range, "10,200,5")] public float StrikeArriveDistance = 40f;

        private float _orbitClock;
        private float _orbitSign;
        private bool _strikingBack;
        // ── 临时调试（排查权重疲劳问题，确认后删除）──

        protected override void OnWarmupStarted()
        {
            base.OnWarmupStarted();

            // 环绕初始化：旋转方向随机
            _orbitSign = GD.Randf() > 0.5f ? 1f : -1f;
            _orbitClock = 0f;
            _strikingBack = false;
        }

        protected override void UpdateDashMovement(double delta)
        {
            if (!IsDashing || Enemy == null || Enemy.IsDeathSequenceActive || Enemy.IsDead) return;

            float dt = (float)delta;

            // 总时长兜底（继承语义：DashMaxDuration 超时收束冲刺）
            _orbitClock += dt;
            if (DashMaxDuration > 0f && _orbitClock >= DashMaxDuration)
            {
                FinishDash();
                return;
            }

            if (Enemy.PlayerTarget == null || !IsInstanceValid(Enemy.PlayerTarget)) return;

            Vector2 dashDir;
            if (!_strikingBack && _orbitClock < OrbitDurationSeconds)
            {
                // ── 环绕阶段：切向环绕 + 径向弹簧 ──
                // 不用"追沿圆运动的点"（追点式会径向振荡，导致朝向反复翻转抽搐），
                // 而是每帧按当前位置与玩家的关系直接合成方向：
                //   切向（绕圈）+ 径向回拉（偏离轨道半径时向圆内/外修正，限幅防振荡）
                Vector2 toPlayer = Enemy.PlayerTarget.GlobalPosition - Enemy.GlobalPosition;
                float dist = toPlayer.Length();
                Vector2 radialDir = dist > 0.01f ? toPlayer / dist : Vector2.Right;
                Vector2 tangent = new Vector2(-radialDir.Y, radialDir.X) * _orbitSign;

                float radialErr = (dist - OrbitRadius) / Mathf.Max(OrbitRadius, 1f);
                float radialPull = Mathf.Clamp(radialErr, -1f, 1f);

                dashDir = tangent * OrbitSpeedFactor + radialDir * radialPull;
            }
            else
            {
                // ── 背刺阶段：冲向玩家背后（玩家朝向的反方向）──
                _strikingBack = true;
                Vector2 backPoint = Enemy.PlayerTarget.GlobalPosition
                    - new Vector2(Enemy.PlayerTarget.FacingRight ? 1f : -1f, 0f) * BackStrikeOffset;
                dashDir = backPoint - Enemy.GlobalPosition;

                if (Enemy.GlobalPosition.DistanceTo(backPoint) <= StrikeArriveDistance
                    || IsPlayerInsideDashStopArea(Enemy.PlayerTarget))
                {
                    FinishDash();
                    return;
                }
            }

            if (dashDir.LengthSquared() <= 0.01f)
                return;

            dashDir = dashDir.Normalized();
            // 朝向死区：X 分量过小（轨道左右两端的竖直切向段）不翻转，避免朝向来回抽搐
            if (LockFacingDuringDash && Mathf.Abs(dashDir.X) > 0.3f)
                Enemy.FlipFacing(dashDir.X > 0);
            Enemy.Velocity = dashDir * DashSpeed;
        }
    }
}
