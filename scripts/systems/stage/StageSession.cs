using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Kuros.Managers;
using Kuros.Utils;

namespace Kuros.Systems.Stage
{
    /// <summary>
    /// 关卡会话：持有全部注册的 StageConfig，跟踪当前关卡与所在层（Floor）。
    /// 电梯选关选项 = 下一层（Floor+1）的所有分支 + 已解锁彩蛋关；出舱时应用所选配置（world 内换关）。
    /// </summary>
    [GlobalClass]
    public partial class StageSession : Node
    {
        [ExportCategory("Refs")]
        [Export] public NodePath GeneratorPath { get; set; } = new NodePath("../StageGeneratorManager");
        [Export] public NodePath CameraPath { get; set; } = new NodePath("../World/Camera2D");

        [ExportCategory("Stage Registry")]
        /// <summary>全部注册关卡（层级推导与调试遍历的数据源）。</summary>
        [Export] public Array<StageConfig> AllConfigs { get; set; } = new();
        /// <summary>起始关卡（null = AllConfigs[0]）。</summary>
        [Export] public StageConfig? StartStage { get; set; }

        /// <summary>F1/F2 调试键在全部关卡间遍历（数字键已归电梯选关）。</summary>
        [Export] public bool DebugKeysEnabled { get; set; } = true;

        private StageGeneratorManager? _generator;
        private StageConfig? _currentConfig;
        private int _debugIndex = 0;
        private int _pendingIndex = -1;

        /// <summary>已激活的彩蛋关（条件触发后出现在电梯面板）。</summary>
        private readonly HashSet<StageConfig> _activatedEggs = new();
        /// <summary>已进入过的彩蛋关（一次性：消费后不再出现，防重复触发）。</summary>
        private readonly HashSet<StageConfig> _consumedEggs = new();

        /// <summary>进入彩蛋前的所在层（彩蛋是两层间的隐藏停靠站——离开彩蛋后继续入口层的行程）。</summary>
        private StageConfig? _eggEntryStage;

        public override void _Ready()
        {
            AddToGroup("stage_session");

            _generator = GetNodeOrNull<StageGeneratorManager>(GeneratorPath);
            if (_generator == null)
            {
                GameLogger.Warn(nameof(StageSession), $"未找到 StageGeneratorManager（路径：{GeneratorPath}）。");
                return;
            }
            _generator.StageGenerated += OnStageGenerated;

            // 首关（配合 StageGeneratorManager.GenerateOnReady=false）
            var first = StartStage ?? (AllConfigs.Count > 0 ? AllConfigs[0] : null);
            if (first != null)
            {
                _currentConfig = first;
                GameLogger.Info(nameof(StageSession), $"首关：{first.StageId}（Floor {first.Floor}）");
                _generator.Regenerate(first, relocateActors: true);
            }
        }

        public override void _ExitTree()
        {
            if (_generator != null)
                _generator.StageGenerated -= OnStageGenerated;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!DebugKeysEnabled || _generator == null || AllConfigs.Count == 0) return;
            if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

            if (key.Keycode == Key.F1 || key.Keycode == Key.F2)
                CycleDebugConfig();
            if (key.Keycode == Key.F3)
                ActivateAllEasterEggsDebug();
        }

        /// <summary>调试：激活全部已注册彩蛋（替代机关/剧情激活入口，验证彩蛋流程用）。</summary>
        private void ActivateAllEasterEggsDebug()
        {
            int count = 0;
            foreach (var config in AllConfigs)
            {
                if (config?.EasterEgg == true && _activatedEggs.Add(config))
                    count++;
            }
            GameLogger.Info(nameof(StageSession), $"调试激活 {count} 个彩蛋关");
        }

        // ── 电梯会话接口（ElevatorController 调用）─────────────────

        /// <summary>当前电梯可选关卡数（下一层分支 + 已解锁彩蛋）。</summary>
        public int GetOptionCount() => GetOptions().Count;

        /// <summary>拼装选关提示："1=餐厅a区  2=餐厅b区"。</summary>
        public string DescribeOptions()
        {
            var options = GetOptions();
            if (options.Count == 0) return "前方没有可前往的楼层";
            var parts = new List<string>();
            for (int i = 0; i < options.Count; i++)
            {
                var config = options[i];
                string label = !string.IsNullOrEmpty(config?.DisplayName)
                    ? config.DisplayName
                    : config?.StageId ?? "?";
                parts.Add($"{i + 1}={label}");
            }
            return string.Join("  ", parts);
        }

        /// <summary>玩家在电梯里选定目标关卡（出舱时才真正生效）。</summary>
        public void SelectStage(int index)
        {
            var options = GetOptions();
            if (index < 0 || index >= options.Count) return;
            _pendingIndex = index;
            GameLogger.Info(nameof(StageSession), $"电梯选定：{options[index]?.StageId ?? "(null)"}");
        }

        /// <summary>出舱动作：应用选中的关卡配置（world 内换关，玩家传送到新关起点）。</summary>
        public void CommitPending()
        {
            var options = GetOptions();
            if (_generator == null || _pendingIndex < 0 || _pendingIndex >= options.Count)
                return;

            var config = options[_pendingIndex];
            _pendingIndex = -1;
            ApplyStage(config);
        }

        /// <summary>直接跳转指定关卡（隐藏入口/机关/剧情切关）：同步换关，不经电梯骑行。
        /// landingPosition = 玩家落点（世界坐标，密道出口用）；null = 新关起点。</summary>
        public void GoToStage(StageConfig config, Vector2? landingPosition = null)
        {
            if (config == null || _generator == null) return;
            ApplyStage(config, landingPosition);
        }

        /// <summary>激活彩蛋关（隐藏机关/剧情事件等条件达成时调用）——此后出现在电梯面板尾部。
        /// 进入该彩蛋关后自动消费（一次性，不再出现）。</summary>
        public void ActivateEasterEgg(StageConfig config)
        {
            if (config?.EasterEgg != true) return;
            if (_activatedEggs.Add(config))
                GameLogger.Info(nameof(StageSession), $"彩蛋已激活：{config.StageId}");
        }

        // ── 调试/内部 ─────────────────────────────────────────────

        private void CycleDebugConfig()
        {
            if (_generator == null || AllConfigs.Count == 0) return;
            _debugIndex = (_debugIndex + 1) % AllConfigs.Count;
            ApplyStage(AllConfigs[_debugIndex]);
        }

        /// <summary>应用关卡配置：记录当前关并重新生成（玩家传送到新关起点或指定落点）。
        /// 进入彩蛋关即消费（_consumedEggs，防重复）并记录入口层（_eggEntryStage，供彩蛋层电梯推导行程）。</summary>
        private void ApplyStage(StageConfig config, Vector2? landingPosition = null)
        {
            if (config.EasterEgg)
            {
                _eggEntryStage = _currentConfig;
                _consumedEggs.Add(config);
            }
            else
            {
                _eggEntryStage = null;
            }
            _currentConfig = config;
            GameLogger.Info(nameof(StageSession), $"前往：{config.StageId}（Floor {config.Floor}）");
            _generator!.Regenerate(config, relocateActors: true, landingPosition: landingPosition);
        }

        /// <summary>
        /// 可选关卡：
        ///   推导基准 = 当前层；若当前在彩蛋层则用**入口层**（彩蛋是两层间的隐藏停靠站——离开后继续入口层的行程）；
        ///   普通分支 = 上行目标层（UpFloorTarget，0 时默认 Floor+1）+ 下行目标层（DownFloorTarget，0=无）；
        ///   彩蛋分支只出现在非彩蛋层的电梯（彩蛋层不再显示彩蛋）；
        ///   排除当前关自身。
        /// </summary>
        private List<StageConfig> GetOptions()
        {
            var list = new List<StageConfig>();
            var current = _currentConfig;
            if (current == null) return list;

            var baseConfig = current.EasterEgg && _eggEntryStage != null ? _eggEntryStage : current;
            int upFloor = baseConfig.UpFloorTarget != 0 ? baseConfig.UpFloorTarget : baseConfig.Floor + 1;
            int downFloor = baseConfig.DownFloorTarget;

            foreach (var config in AllConfigs)
            {
                if (config == null || config == current) continue;

                if (!config.EasterEgg)
                {
                    if (config.Floor == upFloor || (downFloor != 0 && config.Floor == downFloor))
                        list.Add(config);
                }
                else if (!current.EasterEgg && IsEggVisible(config))
                {
                    list.Add(config);
                }
            }
            return list;
        }

        /// <summary>
        /// 彩蛋可见条件（运行时三态）：
        ///   ① 已消费（进入过）→ 永不可见（防重复触发）
        ///   ② ActivateEasterEgg 显式激活 → 可见
        ///   ③ RequiredStoryFlag 非空且存档旗标满足 → 可见
        ///   未激活且无旗标 → 不可见（彩蛋默认关闭，不再"始终可选"）
        /// </summary>
        private bool IsEggVisible(StageConfig config)
        {
            if (!config.EasterEgg || _consumedEggs.Contains(config)) return false;
            if (_activatedEggs.Contains(config)) return true;
            if (string.IsNullOrEmpty(config.RequiredStoryFlag)) return false;
            var flags = SaveManager.Instance?.CurrentGameData?.StoryFlags;
            return flags != null && flags.Contains(config.RequiredStoryFlag);
        }

        private void OnStageGenerated()
        {
            // 玩家被传送到新关起点，相机直接跟随（避免平滑滑过整关）
            var camera = GetNodeOrNull<CameraFollow>(CameraPath);
            if (camera != null && GodotObject.IsInstanceValid(camera))
                camera.SnapToTarget();
        }
    }
}
