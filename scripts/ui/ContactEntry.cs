using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 联系方式条目——数据驱动"联系我们"窗口的内容。
    /// 在编辑器中创建 ContactEntry 资源并填入 ContactWindow.Entries 数组即可扩展，无需改代码。
    /// </summary>
    [GlobalClass]
    public partial class ContactEntry : Resource
    {
        public enum ContactKind
        {
            /// <summary>二维码图片（如 QQ 群），窗口内直接显示供扫码。</summary>
            QrCode,
            /// <summary>网页链接（如 Discord 邀请、Bug 提交网站），点击打开系统浏览器。</summary>
            WebLink,
            /// <summary>纯文本说明（如群号、邮箱），仅展示不可点击。</summary>
            TextOnly,
        }

        [ExportCategory("基本信息")]
        /// <summary>显示标题，如 "QQ交流群" / "Discord" / "Bug反馈"。</summary>
        [Export] public string Title { get; set; } = "";

        /// <summary>联系方式类型，决定窗口内如何渲染。</summary>
        [Export] public ContactKind Kind { get; set; } = ContactKind.TextOnly;

        [ExportCategory("内容")]
        /// <summary>Kind=QrCode 时的二维码图片。</summary>
        [Export] public Texture2D? QrTexture { get; set; }

        /// <summary>Kind=WebLink 时的目标网址（如 https://discord.gg/xxx）。</summary>
        [Export] public string Url { get; set; } = "";

        /// <summary>附加说明（如群号、备注），显示在标题下方，可留空。</summary>
        [Export(PropertyHint.MultilineText)] public string Detail { get; set; } = "";
    }
}
