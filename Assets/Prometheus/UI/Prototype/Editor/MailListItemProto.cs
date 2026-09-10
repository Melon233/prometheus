using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 邮件列表项原型：左侧邮件图标、中间标题与发件信息、右侧未读与附件标记。
    /// 该 Prefab 由 MailPanel 的无限列表引用，因此 Order 必须小于 MailPanelProto。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Mail/Prefabs/MailListItem.prefab", Order = 0, DesignWidth = 544f, DesignHeight = 128f)]
    public sealed class MailListItemProto : UIPrototypeDefinition
    {
        /// <summary>描述单个邮件列表项的结构。</summary>
        public override ProtoNode Build()
        {
            return ScrollProto.ListItemRow("MailListItem")
                .Size(544f, 128f)
                .Bg(UIProtoTheme.ItemBg)
                .Padding(16, 16, 14, 14)
                .Spacing(14f)
                .Add(
                    Icon("ItemIcon", new UnityEngine.Vector2(92f, 92f))
                        .Bg(UIProtoTheme.Accent),
                    Column("ItemTexts")
                        .Flex()
                        .Spacing(4f)
                        .Add(
                            // 列表项高度固定，标题与发件人长度不可控，因此显式允许截断。
                            Label("ItemTitle", "征讨领域奖励")
                                .Height(36f)
                                .FontSize(26f)
                                .Truncate(),
                            Label("ItemSender", "发件人：派蒙")
                                .Height(28f)
                                .FontSize(20f)
                                .TextColor(UIProtoTheme.TextSecondary)
                                .Truncate(),
                            Label("ItemTime", "剩余 29 天")
                                .Height(26f)
                                .FontSize(18f)
                                .TextColor(UIProtoTheme.TextSecondary)),
                    Column("ItemMarks")
                        .Width(28f)
                        .Spacing(8f)
                        .Add(
                            Icon("UnreadDot", new UnityEngine.Vector2(24f, 24f))
                                .Bg(UIProtoTheme.Alert),
                            Icon("AttachMark", new UnityEngine.Vector2(24f, 24f))
                                .Bg(UIProtoTheme.Accent)));
        }
    }
}
