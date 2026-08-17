using System.Collections.Generic;
using Godot;
using Kuros.Actors.Enemies;
using Kuros.Actors.Heroes;
using Kuros.Core;

namespace Kuros.UI
{
    /// <summary>
    /// Boss HUD 血条：每帧扫描 enemies 组中存活 IsBoss 敌人，动态实例化 BossBarRow 模板生成血条行。
    /// - 行模板（BossBarRow.tscn）含名字 + 三层条（HP红色=真实、HP深红=虚血、HP青色=恢复）+ 数值，
    ///   贴图/尺寸/样式全部在模板场景中手动调整；本脚本只实例化并按节点路径绑定。
    /// - 虚血由敌人身上的 GhostHealthComponent 驱动（组件通用 GameActor 实现）；未挂组件时回退单层等效。
    /// - Boss 死亡/失效自动移除行；无 Boss 时整体隐藏。
    /// </summary>
    public partial class BossHUD : Control
    {
        /// <summary>单个 Boss 的动态血条行（模板实例 + 节点引用）。</summary>
        private sealed class BossBarRow
        {
            public HBoxContainer Root = null!;
            public TextureRect Icon = null!;                // Boss 图标（无配置时隐藏）
            public Label NameLabel = null!;
            public TextureProgressBar FillBar = null!;      // 红：真实血量（组件 FillValue）
            public TextureProgressBar HurtBar = null!;      // 深红：虚血（组件 HurtDisplay）
            public TextureProgressBar RecoveryBar = null!;  // 青：当前血（恢复段）
            public Label ValueLabel = null!;
        }

        [Export] public NodePath BossListPath { get; set; } = new("BossList");
        /// <summary>血条行模板场景（BossBarRow.tscn）——样式在模板中编辑。</summary>
        [Export] public PackedScene? BossRowScene { get; set; }

        private VBoxContainer? _bossList;
        private readonly Dictionary<GameActor, BossBarRow> _rows = new();

        public override void _Ready()
        {
            _bossList = BossListPath != null && !BossListPath.IsEmpty
                ? GetNodeOrNull<VBoxContainer>(BossListPath) : null;
            BossRowScene ??= GD.Load<PackedScene>("res://scenes/ui/hud/BossBarRow.tscn");
        }

        public override void _Process(double delta)
        {
            if (_bossList == null) return;

            // 移除死亡/失效 Boss 行
            foreach (var pair in new List<KeyValuePair<GameActor, BossBarRow>>(_rows))
            {
                var boss = pair.Key;
                if (!IsInstanceValid(boss) || !boss.IsInsideTree()
                    || boss.IsDead || boss.IsDeathSequenceActive)
                {
                    UnbindBoss(boss);
                }
            }

            // 新增 Boss 行
            foreach (Node node in GetTree().GetNodesInGroup("enemies"))
            {
                if (node is not SampleEnemy enemy) continue;
                if (!enemy.IsBoss) continue;
                if (enemy.IsDead || enemy.IsDeathSequenceActive) continue;
                if (_rows.ContainsKey(enemy)) continue;

                BindBoss(enemy);
            }

            // 每帧刷新显示值（虚血动画来自组件，需每帧读组件动画值）
            foreach (var (boss, row) in _rows)
            {
                UpdateRow(boss, row);
            }

            Visible = _rows.Count > 0;
        }

        private void BindBoss(GameActor boss)
        {
            if (BossRowScene == null) return;

            var root = BossRowScene.Instantiate<HBoxContainer>();
            if (root == null) return;

            var row = new BossBarRow
            {
                Root = root,
                Icon = root.GetNodeOrNull<TextureRect>("NameSlot/BossIcon")!,
                NameLabel = root.GetNodeOrNull<Label>("NameSlot/BossNameLabel")!,
                HurtBar = root.GetNodeOrNull<TextureProgressBar>("BarHost/HurtBar")!,
                RecoveryBar = root.GetNodeOrNull<TextureProgressBar>("BarHost/RecoveryBar")!,
                FillBar = root.GetNodeOrNull<TextureProgressBar>("BarHost/FillBar")!,
                ValueLabel = root.GetNodeOrNull<Label>("BossValueLabel")!,
            };

            // 图标与名字互为取代（同一 NameSlot 位置）：有 BossIcon 显示图标、否则显示 DisplayName（空则不显示）
            if (boss is SampleEnemy se && se.BossIcon != null)
            {
                if (IsInstanceValid(row.Icon))
                {
                    row.Icon.Texture = se.BossIcon;
                    row.Icon.Visible = true;
                }
                row.NameLabel.Visible = false;
            }
            else
            {
                row.NameLabel.Text = boss is SampleEnemy se2 ? se2.DisplayName ?? string.Empty : string.Empty;
                if (IsInstanceValid(row.Icon))
                {
                    row.Icon.Visible = false;
                }
            }

            _bossList!.AddChild(root);
            boss.HealthChanged += OnBossHealthChanged;
            _rows[boss] = row;
            UpdateRow(boss, row);
        }

        private void OnBossHealthChanged(int current, int max)
        {
            // 事件更新无需额外处理——_Process 每帧统一刷新（含虚血动画值）
        }

        private void UpdateRow(GameActor boss, BossBarRow row)
        {
            float maxH = Mathf.Max(1f, boss.MaxHealth);
            float currentH = Mathf.Max(0f, boss.CurrentHealth);

            // 虚血：敌人挂 GhostHealthComponent 时读动画值，否则回退当前血（单层等效）
            var ghost = boss.GetNodeOrNull<GhostHealthComponent>("GhostHealthComponent");
            float fillValue = ghost != null ? ghost.FillValue : currentH;
            float hurtValue = ghost != null ? ghost.HurtDisplay : currentH;

            row.FillBar.MaxValue = maxH;
            row.FillBar.Value = fillValue;
            row.HurtBar.MaxValue = maxH;
            row.HurtBar.Value = hurtValue;
            row.RecoveryBar.MaxValue = maxH;
            row.RecoveryBar.Value = currentH;
            row.ValueLabel.Text = $"{Mathf.CeilToInt(currentH)}/{Mathf.CeilToInt(maxH)}";
        }

        private void UnbindBoss(GameActor boss)
        {
            if (_rows.Remove(boss, out var row))
            {
                boss.HealthChanged -= OnBossHealthChanged;
                if (IsInstanceValid(row.Root))
                {
                    row.Root.QueueFree();
                }
            }
        }
    }
}
