namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 滑槽磁铁机械臂的"投送"攻击（挂在 Attack 状态的 SimpleMeleeAttack 节点上）。
    /// 它不造成伤害（DamageMultiplier = 0），只承载罐子的生命周期；**Warmup 与 Active 两段都会被挂起**，
    /// 直到机械臂抵达玩家位置才继续——这样"搬运段"放在哪一段都自洽：
    ///   · 配法 A（搬运放 Warmup）：携带罐子 SpawnTiming = OnWarmup + LifecycleBinding = OnWarmupEnd，
    ///     释放特效 SpawnTiming = OnActive → 抵达即销毁手上罐子、进 Active 播开箱/投放。
    ///   · 配法 B（搬运放 Active）：携带罐子 SpawnTiming = OnWarmup/OnActive + LifecycleBinding = OnActiveEnd，
    ///     释放特效 SpawnTiming = OnRecovery → 抵达即 Active 结束销毁罐子、Recovery 生成释放特效。
    /// 注意：挂起会把 Warmup/Active 的计时一并拖住，所以绑定在 OnWarmupEnd/OnActiveEnd 的特效寿命 = 整段搬运，
    /// 而不是配置表里的秒数（这正是"挂到抵达为止"的实现方式）。
    /// 另外重写 CanStart：不看朝向角度、也不要求玩家在攻击盒内——何时投送完全由机械臂的相位决定。
    /// </summary>
    public partial class EnemyMagnetJarAttack : EnemySimpleMeleeAttack
    {
        public override bool CanStart()
        {
            // 不检查玩家引用/朝向角度/攻击盒：投送只由机械臂相位驱动，
            // 玩家引用缺失（生成早于玩家解析）也不该让循环停摆。
            if (Enemy == null) return false;
            if (Enemy.IsDeathSequenceActive || Enemy.IsDead) return false;
            return !IsRunning && !IsOnCooldown;
        }

        protected override bool ShouldHoldWarmupPhase() => ShouldHoldJar();

        protected override bool ShouldHoldActivePhase() => ShouldHoldJar();

        /// <summary>机械臂尚未抵达玩家位置（且处于进场相位）→ 两段都继续挂起。</summary>
        private bool ShouldHoldJar()
            => (Enemy as Kuros.Actors.Enemies.EnemyF1RogueAIMagnet)?.ShouldHoldJarPhase == true;
    }
}
