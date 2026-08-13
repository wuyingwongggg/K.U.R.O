using System.Collections.Generic;
using Godot;

namespace Kuros.Managers
{
    /// <summary>
    /// 武器电量管理器（Autoload 单例）：维护各武器独立的电量状态。
    /// 电量状态与效果实例解耦（Passive 技能效果卸武器即销毁、Active 技能效果每次攻击重新实例化，
    /// 电量若放效果内会丢失），以武器 SkillId 为 key 持久存储——切换武器后各自继续恢复。
    /// 恢复规则：距上次消耗 ≥ RecoveryDelaySeconds 后按 RecoveryPerSecond 缓慢回充至 Max。
    /// </summary>
    public partial class WeaponBatteryManager : Node
    {
        private static WeaponBatteryManager? _instance;

        public static WeaponBatteryManager Instance => _instance!;

        /// <summary>单个武器的电量状态。</summary>
        private sealed class BatteryState
        {
            public float MaxCharge = 100f;
            public float ConsumePerAttack = 10f;
            public float RecoveryDelaySeconds = 1f;
            public float RecoveryPerSecond = 20f;
            public float CurrentCharge;
            public ulong LastConsumeAtMs;
        }

        private readonly Dictionary<string, BatteryState> _batteries = new();

        public override void _Ready()
        {
            if (_instance != null && _instance != this)
            {
                QueueFree();
                return;
            }

            _instance = this;
        }

        public override void _ExitTree()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 注册武器电量（幂等）：已注册时只更新配置参数，不重置 CurrentCharge——
        /// 保证切换武器/重新装备后电量保留并继续按原状态恢复。
        /// </summary>
        public void RegisterWeapon(string skillId, float maxCharge, float consumePerAttack,
            float recoveryDelaySeconds, float recoveryPerSecond)
        {
            if (string.IsNullOrEmpty(skillId)) return;

            if (_batteries.TryGetValue(skillId, out var state))
            {
                state.MaxCharge = Mathf.Max(1f, maxCharge);
                state.ConsumePerAttack = Mathf.Max(0f, consumePerAttack);
                state.RecoveryDelaySeconds = Mathf.Max(0f, recoveryDelaySeconds);
                state.RecoveryPerSecond = Mathf.Max(0f, recoveryPerSecond);
                state.CurrentCharge = Mathf.Min(state.CurrentCharge, state.MaxCharge);
                return;
            }

            _batteries[skillId] = new BatteryState
            {
                MaxCharge = Mathf.Max(1f, maxCharge),
                ConsumePerAttack = Mathf.Max(0f, consumePerAttack),
                RecoveryDelaySeconds = Mathf.Max(0f, recoveryDelaySeconds),
                RecoveryPerSecond = Mathf.Max(0f, recoveryPerSecond),
                CurrentCharge = Mathf.Max(1f, maxCharge),
                LastConsumeAtMs = 0,
            };
        }

        /// <summary>当前电量是否足够一次攻击消耗。未注册的武器（无电量机制）恒为 true。</summary>
        public bool CanAfford(string? skillId)
        {
            if (string.IsNullOrEmpty(skillId) || !_batteries.TryGetValue(skillId, out var state))
            {
                return true;
            }

            return state.CurrentCharge >= state.ConsumePerAttack;
        }

        /// <summary>
        /// 尝试消耗一次攻击电量：成功扣电并刷新"距上次消耗"计时；未注册武器恒成功（无电量机制）。
        /// 电量不足返回 false（攻击应被阻止）。
        /// </summary>
        public bool TryConsume(string? skillId)
        {
            if (string.IsNullOrEmpty(skillId) || !_batteries.TryGetValue(skillId, out var state))
            {
                return true;
            }

            if (state.CurrentCharge < state.ConsumePerAttack)
            {
                return false;
            }

            state.CurrentCharge -= state.ConsumePerAttack;
            state.LastConsumeAtMs = Time.GetTicksMsec();
            return true;
        }

        /// <summary>电量比例（0..1），供 bar 显示。未注册武器返回 1（满）。</summary>
        public float GetChargeRatio(string? skillId)
        {
            if (string.IsNullOrEmpty(skillId) || !_batteries.TryGetValue(skillId, out var state))
            {
                return 1f;
            }

            return state.MaxCharge > 0f ? state.CurrentCharge / state.MaxCharge : 1f;
        }

        /// <summary>当前电量数值。未注册武器返回 -1（调用方自行判断是否显示）。</summary>
        public float GetCharge(string? skillId)
        {
            if (string.IsNullOrEmpty(skillId) || !_batteries.TryGetValue(skillId, out var state))
            {
                return -1f;
            }

            return state.CurrentCharge;
        }

        /// <summary>最大电量。未注册武器返回 -1。</summary>
        public float GetMaxCharge(string? skillId)
        {
            if (string.IsNullOrEmpty(skillId) || !_batteries.TryGetValue(skillId, out var state))
            {
                return -1f;
            }

            return state.MaxCharge;
        }

        public override void _Process(double delta)
        {
            if (_batteries.Count == 0) return;

            ulong now = Time.GetTicksMsec();
            foreach (var state in _batteries.Values)
            {
                // 恢复条件：未满电 且 距上次消耗 ≥ 延迟（耗尽与部分消耗一视同仁，停止攻击即开始计时）
                if (state.CurrentCharge >= state.MaxCharge || state.RecoveryPerSecond <= 0f)
                {
                    continue;
                }

                if (state.LastConsumeAtMs != 0 &&
                    now - state.LastConsumeAtMs < (ulong)(state.RecoveryDelaySeconds * 1000f))
                {
                    continue;
                }

                state.CurrentCharge = Mathf.Min(
                    state.MaxCharge,
                    state.CurrentCharge + state.RecoveryPerSecond * (float)delta);
            }
        }
    }
}
