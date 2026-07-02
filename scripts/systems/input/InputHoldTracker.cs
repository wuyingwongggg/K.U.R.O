using Godot;
using System.Collections.Generic;

namespace Kuros.Systems.InputTracking
{
    /// <summary>
    /// 按键时长追踪器 —— 注册即用，区分短按/长按。
    /// 每个 action 独立追踪，阈值可单独配置。
    /// </summary>
    public class InputHoldTracker
    {
        private readonly Dictionary<string, float> _thresholds = new();
        private readonly Dictionary<string, float> _pressTimes = new();
        private readonly HashSet<string> _longPressTriggeredThisFrame = new();
        private readonly HashSet<string> _shortPressDetectedThisFrame = new();

        /// <summary>
        /// 注册一个需要追踪时长按的 action。未注册的 action 查询时永远返回 false。
        /// </summary>
        /// <param name="actionName">Input Map 中的 action 名称</param>
        /// <param name="longPressThreshold">长按判定阈值（秒），默认 0.4s</param>
        public void Register(string actionName, float longPressThreshold = 0.4f)
        {
            _thresholds[actionName] = longPressThreshold;
        }

        /// <summary>
        /// 取消注册。之后该 action 不再被追踪。
        /// </summary>
        public void Unregister(string actionName)
        {
            _thresholds.Remove(actionName);
            _pressTimes.Remove(actionName);
            _longPressTriggeredThisFrame.Remove(actionName);
            _shortPressDetectedThisFrame.Remove(actionName);
        }

        /// <summary>
        /// 每帧在 _Process 中调用一次。自动检测所有已注册 action 的按下/按住/松开状态。
        /// </summary>
        public void Process(float delta)
        {
            _longPressTriggeredThisFrame.Clear();
            _shortPressDetectedThisFrame.Clear();

            foreach (var (action, threshold) in _thresholds)
            {
                if (!_pressTimes.TryGetValue(action, out float elapsed))
                    elapsed = 0f;

                if (Godot.Input.IsActionJustPressed(action))
                {
                    _pressTimes[action] = 0f;
                }
                else if (Godot.Input.IsActionPressed(action))
                {
                    float newElapsed = elapsed + delta;
                    _pressTimes[action] = newElapsed;

                    if (newElapsed >= threshold && elapsed < threshold)
                    {
                        _longPressTriggeredThisFrame.Add(action);
                    }
                }
                else if (elapsed > 0f)
                {
                    // 刚松开
                    if (elapsed < threshold)
                        _shortPressDetectedThisFrame.Add(action);

                    _pressTimes[action] = 0f;
                }
            }
        }

        /// <summary>
        /// 长按持续中 —— 超过阈值后每帧为 true（Level-triggered）。
        /// 适用于：按住 Shift=奔跑
        /// </summary>
        public bool IsLongPressHeld(string actionName)
        {
            if (!_thresholds.TryGetValue(actionName, out float threshold)) return false;
            if (!_pressTimes.TryGetValue(actionName, out float elapsed)) return false;
            return elapsed >= threshold;
        }

        /// <summary>
        /// 长按刚触发 —— 仅在超过阈值的那一帧为 true（Edge-triggered，读后即焚）。
        /// 适用于：蓄力完成时触发一次技能
        /// </summary>
        public bool WasLongPressTriggered(string actionName)
        {
            return _longPressTriggeredThisFrame.Contains(actionName);
        }

        /// <summary>
        /// 短按 —— 在松开的帧为 true 且按住时长 &lt; 阈值（Edge-triggered）。
        /// 适用于：Shift 短按=闪避
        /// </summary>
        public bool WasShortPressed(string actionName)
        {
            return _shortPressDetectedThisFrame.Contains(actionName);
        }

        /// <summary>
        /// 当前按住时长（秒）。未按住时返回 0。
        /// </summary>
        public float GetHoldDuration(string actionName)
        {
            _pressTimes.TryGetValue(actionName, out float elapsed);
            return elapsed;
        }
    }
}
