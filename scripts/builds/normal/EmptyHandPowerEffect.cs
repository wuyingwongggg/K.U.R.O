using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core.Effects;

namespace Kuros.Builds.Normal
{
    /// <summary>
    /// 空载增幅（BuildNormal_C_003）：当前未携带任何武器时,基础伤害提升 TierValues 对应层百分比
    /// （层1 25%、层2 50%——替换式）。条件动态:每 tick 判定,中途拿到武器立即取消加成。
    /// 实现:写 SamplePlayer.BasicAttackMultiplier（两处 PerformAttackCheck 共用的基础倍率）。
    /// </summary>
    [GlobalClass]
    public partial class EmptyHandPowerEffect : ActorEffect
    {
        [Export] public float[] TierValues { get; set; } = { 25f, 50f };

        private MainCharacter? _player;
        private int _tier;

        private float CurrentPercent => _tier < TierValues.Length ? TierValues[_tier] : TierValues[^1];

        protected override void OnApply()
        {
            _player = Actor as MainCharacter;
            _tier = 0;
            Evaluate();
        }

        protected override void OnStackRefreshed()
        {
            _tier = Mathf.Min(_tier + 1, TierValues.Length - 1);
            Evaluate();
        }

        protected override void OnTick(double delta)
        {
            // 条件随携带武器变化动态生效/取消
            if (_player == null || !IsInstanceValid(_player))
                return;
            Evaluate();
        }

        private void Evaluate()
        {
            if (_player == null || !IsInstanceValid(_player)) return;

            var inventory = _player.InventoryComponent;
            bool empty = inventory == null || inventory.GetCarriedWeaponCount() == 0;
            float mult = empty ? 1f + CurrentPercent / 100f : 1f;
            _player.BasicAttackMultiplier = mult;
        }

        public override void OnRemoved()
        {
            if (_player != null && IsInstanceValid(_player))
            {
                // 单效果独占基础倍率;未来多张增伤卡共存时改为聚合注册
                _player.BasicAttackMultiplier = 1f;
            }
            _player = null;
            base.OnRemoved();
        }
    }
}
