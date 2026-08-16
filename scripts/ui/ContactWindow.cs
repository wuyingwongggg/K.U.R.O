using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// "联系我们"弹窗：根据 Entries（ContactEntry 资源数组）动态生成联系方式列表。
    /// - QrCode 条目：显示二维码图片（手机扫码）
    /// - WebLink 条目：点击按钮打开系统浏览器
    /// - TextOnly 条目：纯文本展示
    /// 可复用到任意场景（封面、主菜单、暂停菜单），新增联系方式只需在编辑器里加条目。
    /// </summary>
    [GlobalClass]
    public partial class ContactWindow : Control
    {
        /// <summary>联系方式条目列表（编辑器里添加 ContactEntry 资源）。</summary>
        [Export] public Godot.Collections.Array<ContactEntry> Entries { get; set; } = new();

        [Export] public NodePath EntryListPath { get; set; } = new NodePath("Center/Panel/VBox/EntryList");

        private VBoxContainer? _entryList;
        private bool _built;

        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always; // 暂停时也可用（供暂停菜单复用）
            Visible = false;
            ZIndex = 100;

            GetNodeOrNull<Button>("Center/Panel/VBox/CloseButton")?.Connect(Button.SignalName.Pressed, Callable.From(Close));
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible) return;
            if (@event.IsActionPressed("ui_cancel"))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>打开弹窗（每次打开重建列表，条目资源热改后立即生效）。</summary>
        public void Open()
        {
            RebuildEntries();
            Visible = true;
        }

        public void Close()
        {
            Visible = false;
        }

        private void RebuildEntries()
        {
            if (!_built)
            {
                _entryList = GetNodeOrNull<VBoxContainer>(EntryListPath);
                _built = true;
            }
            if (_entryList == null) return;

            // 清空旧行
            foreach (Node child in _entryList.GetChildren())
                child.QueueFree();

            if (Entries.Count == 0)
            {
                var empty = new Label
                {
                    Text = "暂无联系方式",
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                empty.AddThemeFontSizeOverride("font_size", 14);
                empty.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                _entryList.AddChild(empty);
                return;
            }

            bool first = true;
            foreach (var entry in Entries)
            {
                if (entry == null) continue;

                // 条目之间加淡色分隔线（首条之前、末条之后不加）
                if (!first)
                    _entryList.AddChild(CreateEntrySeparator());
                first = false;

                _entryList.AddChild(BuildEntryRow(entry));
            }
        }

        /// <summary>条目之间的淡色分隔线（上方 8px 留白 + 1px 半透明白线）。</summary>
        private static Control CreateEntrySeparator()
        {
            var separator = new HSeparator();
            separator.AddThemeStyleboxOverride("separator", new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),           // 填充透明
                BorderWidthBottom = 1,                     // 仅底部 1px 线
                BorderColor = new Color(1, 1, 1, 0.18f),   // 淡白线
                ContentMarginTop = 8f,                     // 线上方留白
                ContentMarginBottom = 1f,
            });
            return separator;
        }

        /// <summary>按条目类型生成对应的显示行。</summary>
        private Control BuildEntryRow(ContactEntry entry)
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var title = new Label
            {
                Text = entry.Title,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Arbitrary, // 中文按字符断行（导出模板 Word 断词异常，见 BuildCard 排查记录）
            };
            title.AddThemeFontSizeOverride("font_size", 18);
            title.AddThemeColorOverride("font_color", Colors.White);
            row.AddChild(title);

            switch (entry.Kind)
            {
                case ContactEntry.ContactKind.QrCode:
                    if (entry.QrTexture != null)
                    {
                        var qr = new TextureRect
                        {
                            Texture = entry.QrTexture,
                            CustomMinimumSize = new Vector2(160, 160),
                            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                        };
                        row.AddChild(qr);
                    }
                    break;

                case ContactEntry.ContactKind.WebLink:
                    var openBtn = new Button
                    {
                        Text = "打开链接",
                        Disabled = string.IsNullOrEmpty(entry.Url),
                        TooltipText = entry.Url,   // 实际网址显示在悬停提示里
                        SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                    };
                    openBtn.AddThemeFontSizeOverride("font_size", 16);
                    string url = entry.Url;
                    openBtn.Pressed += () => OS.ShellOpen(url);
                    row.AddChild(openBtn);
                    break;

                case ContactEntry.ContactKind.TextOnly:
                default:
                    break;
            }

            if (!string.IsNullOrEmpty(entry.Detail))
            {
                var detail = new Label
                {
                    Text = entry.Detail,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    AutowrapMode = TextServer.AutowrapMode.Arbitrary,
                };
                detail.AddThemeFontSizeOverride("font_size", 13);
                detail.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
                row.AddChild(detail);
            }

            return row;
        }
    }
}
