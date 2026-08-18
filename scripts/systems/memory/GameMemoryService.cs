using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Controllers;
using Kuros.Core;
using Kuros.Items;
using Kuros.Managers;

namespace Kuros.Systems.Memory
{
    /// <summary>
    /// 游戏记忆核心（autoload，跨场景存活）：
    /// - PersistentLayer：永久记录（击败敌人计数 / 获取武器计数 / 剧情标志），持久化委托 SaveManager 存档。
    ///   图鉴系统、剧情推进、P2 长期记忆等均从此层取数——事件只在此处接线一次。
    /// - SessionLayer：本局会话事件环形缓冲（短时效事实），供 P2 实时文本等短期消费。
    /// 外部系统通过 Record*/Query* API 读写，不各自重复订阅事件。
    /// </summary>
    [GlobalClass]
    public partial class GameMemoryService : Node
    {
        public static GameMemoryService? Instance { get; private set; }

        [Export(PropertyHint.Range, "4,64,1")] public int SessionCapacity { get; set; } = 16;
        /// <summary>调试打印（低频事件，验证事件接线/持久层读写用）。</summary>
        [Export] public bool DebugLogging { get; set; } = false;

        // ── 永久层（内存缓存 + 存档同步） ──
        private readonly Dictionary<string, int> _enemyDefeated = new();
        private readonly Dictionary<string, int> _weaponAcquired = new();
        private bool _persistentLoaded;
        private bool _warnedNoActiveSave;

        // ── 会话层（本局环形缓冲） ──
        private readonly Queue<SessionMemoryEntry> _sessionLog = new();

        // ── 实例事件绑定（玩家/波次管理器随场景重载，需动态换绑） ──
        private GameActor? _boundPlayer;
        private PlayerInventoryComponent? _boundInventory;
        private WaveSpawnManager? _boundWaveManager;
        private float _rebindAccum;
        private const float RebindIntervalSeconds = 1f;
        private const float SameEventMergeSeconds = 3f;

        /// <summary>会话记忆条目（短时效事实：击杀/受击/拾取/波次）。</summary>
        public sealed class SessionMemoryEntry
        {
            public string Type { get; }
            public string Text { get; }
            public ulong TimestampMs { get; }

            public SessionMemoryEntry(string type, string text)
            {
                Type = type;
                Text = text;
                TimestampMs = Time.GetTicksMsec();
            }
        }

        public override void _EnterTree()
        {
            Instance = this;
        }

        public override void _ExitTree()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            UnbindPlayer();
            UnbindWaveManager();
        }

        public override void _Ready()
        {
            // 静态事件：一次订阅全局生效（GameActor 死亡/受击广播）
            GameActor.DeathFinalized += OnActorDeathFinalized;
            GameActor.AnyDamageTaken += OnAnyDamageTaken;
        }

        public override void _Process(double delta)
        {
            _rebindAccum += (float)delta;
            if (_rebindAccum < RebindIntervalSeconds)
            {
                return;
            }
            _rebindAccum = 0f;
            RebindInstanceEvents();
        }

        // ── 事件接线 ──

        private void OnActorDeathFinalized(GameActor actor)
        {
            if (actor == null || !IsInstanceValid(actor)) return;
            if (!actor.IsInGroup("enemies")) return;
            RecordEnemyDefeated(actor);
        }

        private void OnAnyDamageTaken(GameActor victim, GameActor? attacker, int damage)
        {
            if (victim == null || !IsInstanceValid(victim)) return;
            if (!victim.IsInGroup("player")) return;
            string attackerName = attacker != null && IsInstanceValid(attacker)
                ? attacker.GetType().Name
                : "未知攻击";
            RecordSessionEvent("player_hit", $"玩家被{attackerName}击中");
        }

        /// <summary>首次获得任意物品时触发（AddItemSmart → ItemFirstAcquired）；只记录武器类。</summary>
        private void OnItemFirstAcquired(ItemDefinition item)
        {
            if (item == null) return;
            bool isWeapon = item.IsThrowWeapon
                || string.Equals(item.Category, "Weapon", StringComparison.OrdinalIgnoreCase);
            if (!isWeapon) return;
            RecordWeaponAcquired(item);
        }

        private void OnWaveCleared(int waveIndex)
        {
            RecordSessionEvent("wave_cleared", $"第{waveIndex}波敌人被清空");
        }

        private void RebindInstanceEvents()
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer) || !_boundPlayer.IsInsideTree())
            {
                UnbindPlayer();
                var player = GetTree().GetFirstNodeInGroup("player") as GameActor;
                if (player != null && IsInstanceValid(player))
                {
                    var inventory = FindInventoryComponent(player);
                    if (inventory != null)
                    {
                        inventory.ItemFirstAcquired += OnItemFirstAcquired;
                        _boundPlayer = player;
                        _boundInventory = inventory;
                    }
                }
            }

            if (_boundWaveManager == null || !IsInstanceValid(_boundWaveManager) || !_boundWaveManager.IsInsideTree())
            {
                UnbindWaveManager();
                var waveManager = FindWaveSpawnManager(GetTree().CurrentScene);
                if (waveManager != null && IsInstanceValid(waveManager))
                {
                    waveManager.WaveCleared += OnWaveCleared;
                    _boundWaveManager = waveManager;
                }
            }
        }

        private void UnbindPlayer()
        {
            if (_boundInventory != null && IsInstanceValid(_boundInventory))
            {
                _boundInventory.ItemFirstAcquired -= OnItemFirstAcquired;
            }
            _boundPlayer = null;
            _boundInventory = null;
        }

        private void UnbindWaveManager()
        {
            if (_boundWaveManager != null && IsInstanceValid(_boundWaveManager))
            {
                _boundWaveManager.WaveCleared -= OnWaveCleared;
            }
            _boundWaveManager = null;
        }

        private static PlayerInventoryComponent? FindInventoryComponent(Node root)
        {
            foreach (Node child in root.GetChildren())
            {
                if (child is PlayerInventoryComponent inv)
                {
                    return inv;
                }
                var nested = FindInventoryComponent(child);
                if (nested != null)
                {
                    return nested;
                }
            }
            return null;
        }

        private static WaveSpawnManager? FindWaveSpawnManager(Node? root)
        {
            if (root == null) return null;
            if (root is WaveSpawnManager wsm) return wsm;
            foreach (Node child in root.GetChildren())
            {
                var found = FindWaveSpawnManager(child);
                if (found != null) return found;
            }
            return null;
        }

        // ── 写入 API ──

        public void RecordEnemyDefeated(GameActor enemy)
        {
            EnsurePersistentLoaded();
            string typeName = enemy.GetType().Name;
            _enemyDefeated[typeName] = (_enemyDefeated.TryGetValue(typeName, out int n) ? n : 0) + 1;
            SavePersistent();
            RecordSessionEvent("enemy_defeated", $"击败了{typeName}");
        }

        public void RecordWeaponAcquired(ItemDefinition item)
        {
            EnsurePersistentLoaded();
            string itemId = item.ItemId;
            _weaponAcquired[itemId] = (_weaponAcquired.TryGetValue(itemId, out int n) ? n : 0) + 1;
            SavePersistent();
            RecordSessionEvent("weapon_acquired", $"获得了武器{item.DisplayName}");
        }

        /// <summary>剧情标志（永久层）：写入存档 StoryFlags，幂等。</summary>
        public void RecordStoryFlag(string flagId)
        {
            var data = SaveManager.Instance?.CurrentGameData;
            if (data == null || string.IsNullOrWhiteSpace(flagId)) return;
            if (data.StoryFlags.Contains(flagId)) return;
            data.StoryFlags.Add(flagId);
        }

        /// <summary>会话事件（本局环形缓冲）：合并窗口内已有同类型同文本条目时跳过，防高频刷屏。</summary>
        public void RecordSessionEvent(string type, string text)
        {
            ulong now = Time.GetTicksMsec();
            ulong windowMs = (ulong)(SameEventMergeSeconds * 1000f);
            foreach (var entry in _sessionLog)
            {
                if (entry.Type == type && entry.Text == text && now - entry.TimestampMs < windowMs)
                {
                    return;
                }
            }

            _sessionLog.Enqueue(new SessionMemoryEntry(type, text));
            while (_sessionLog.Count > Mathf.Max(4, SessionCapacity))
            {
                _sessionLog.Dequeue();
            }

            if (DebugLogging)
            {
                GD.Print($"[Memory] session event: [{type}] {text}");
            }
        }

        // ── 查询 API ──

        public int EnemyDefeatCount(string enemyType)
        {
            EnsurePersistentLoaded();
            return _enemyDefeated.TryGetValue(enemyType, out int n) ? n : 0;
        }

        public int WeaponAcquiredCount(string itemId)
        {
            EnsurePersistentLoaded();
            return _weaponAcquired.TryGetValue(itemId, out int n) ? n : 0;
        }

        public bool IsStoryFlagSet(string flagId)
        {
            var data = SaveManager.Instance?.CurrentGameData;
            return data != null && data.StoryFlags.Contains(flagId);
        }

        public List<string> LatestSessionTexts(int count)
        {
            var result = new List<string>();
            foreach (var entry in _sessionLog)
            {
                result.Add($"[{entry.Type}] {entry.Text}");
                if (result.Count >= count)
                {
                    break;
                }
            }
            return result;
        }

        /// <summary>L0 持久记忆摘要（单行，AI prompt 用）：通关次数/击败总数/获取武器总数/剧情标志数。</summary>
        public string PersistentSummaryText()
        {
            EnsurePersistentLoaded();
            var data = SaveManager.Instance?.CurrentGameData;
            int clearCount = data?.ClearCount ?? 0;
            int enemyTotal = 0;
            foreach (var kvp in _enemyDefeated) enemyTotal += kvp.Value;
            int weaponTotal = 0;
            foreach (var kvp in _weaponAcquired) weaponTotal += kvp.Value;
            int storyFlagCount = data?.StoryFlags.Count ?? 0;
            return $"clear_count={clearCount}, enemies_defeated_total={enemyTotal}, weapons_acquired_total={weaponTotal}, story_flags={storyFlagCount}";
        }

        // ── 持久化（委托 SaveManager 存档） ──

        private void EnsurePersistentLoaded()
        {
            if (_persistentLoaded) return;
            var data = SaveManager.Instance?.CurrentGameData;
            if (data == null)
            {
                // 无档时保持未加载状态：同一进程内后续读档/新游戏建档后允许重新加载。
                // 提示只打一次，避免无档环境下每次快照都刷屏。
                if (DebugLogging && !_warnedNoActiveSave)
                {
                    _warnedNoActiveSave = true;
                    GD.Print("[Memory] persistent loaded: no active save (CurrentGameData is null)");
                }
                return;
            }
            _persistentLoaded = true;
            foreach (var kvp in data.EnemyDefeatedCounts) _enemyDefeated[kvp.Key] = kvp.Value;
            foreach (var kvp in data.WeaponAcquiredCounts) _weaponAcquired[kvp.Key] = kvp.Value;
            if (DebugLogging)
            {
                GD.Print($"[Memory] persistent loaded: {PersistentSummaryText()}");
                foreach (var kvp in _enemyDefeated) GD.Print($"  enemy {kvp.Key} x{kvp.Value}");
                foreach (var kvp in _weaponAcquired) GD.Print($"  weapon {kvp.Key} x{kvp.Value}");
            }
        }

        private void SavePersistent()
        {
            var data = SaveManager.Instance?.CurrentGameData;
            if (data == null) return;
            data.EnemyDefeatedCounts = ToGodotDict(_enemyDefeated);
            data.WeaponAcquiredCounts = ToGodotDict(_weaponAcquired);
        }

        private static Godot.Collections.Dictionary<string, int> ToGodotDict(Dictionary<string, int> source)
        {
            var result = new Godot.Collections.Dictionary<string, int>();
            foreach (var kvp in source) result[kvp.Key] = kvp.Value;
            return result;
        }
    }
}
