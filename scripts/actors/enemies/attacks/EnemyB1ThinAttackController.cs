using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyB1ThinAttackController : EnemyAttackController
    {
        [Export] public string FreezeAttackName { get; set; } = "FreezeMeleeAttack";
        [Export] public string ComboAttackName { get; set; } = "SimpleMeleeAttack";
        [Export] public string HeavyAttackName { get; set; } = "ChargeEscapeAttack";

		public string CurrentAttackName { get; private set; } = string.Empty;
        public string MeleeAttackName => ComboAttackName;
        public string ChargeAttackName => HeavyAttackName;

        private bool _freezeAttackTriggered;

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);

            if (attack != null)
            {
                CurrentAttackName = attack.Name;
            }

            if (_freezeAttackTriggered)
            {
                return;
            }

            if (attack?.Name != FreezeAttackName)
            {
                return;
            }

            _freezeAttackTriggered = true;
            TrySetAttackWeight(FreezeAttackName, 0f);
            TrySetAttackWeight(ComboAttackName, 1f);
        }

    		protected override void OnAttackFinished()
    		{
    			base.OnAttackFinished();
    			CurrentAttackName = string.Empty;
    		}
    }
}


