using Godot;
using System;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyB1FatAttackController : EnemyAttackController
    {
        [Export] public string Skill1AttackName { get; set; } = "ChargeEscapeAttack";
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
        [Export(PropertyHint.Range, "1,10,1")] public int MeleeCountBeforeCharge { get; set; } = 2;

        public string CurrentAttackName { get; private set; } = string.Empty;

        private int _meleeCountSinceCharge;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _meleeCountSinceCharge = 0;
            ConfigureNextAttack(forceCharge: false);
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);
            CurrentAttackName = attack.Name;

            if (IsAttack(attack.Name, MeleeAttackName))
            {
                _meleeCountSinceCharge++;
                int threshold = Mathf.Max(1, MeleeCountBeforeCharge);
                ConfigureNextAttack(forceCharge: _meleeCountSinceCharge >= threshold);
                return;
            }

            if (IsAttack(attack.Name, Skill1AttackName))
            {
                _meleeCountSinceCharge = 0;
                ConfigureNextAttack(forceCharge: false);
            }
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            // 保留上一次攻击名，供动画控制器在冷却/收尾阶段判定 skill3。
            // 下一次攻击开始时会在 OnChildAttackStarted 中覆盖。
        }

        private void ConfigureNextAttack(bool forceCharge)
        {
            if (forceCharge)
            {
                TrySetAttackWeight(Skill1AttackName, 1f);
                TrySetAttackWeight(MeleeAttackName, 0f);
                return;
            }

            TrySetAttackWeight(Skill1AttackName, 0f);
            TrySetAttackWeight(MeleeAttackName, 1f);
        }

        private static bool IsAttack(string attackName, string expectedName)
        {
            return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        }
    }
}


