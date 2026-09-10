using UnityEngine;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 邮件界面原型，参考原神的邮件面板：顶部标题与分页，左侧无限滚动邮件列表，右侧邮件详情与附件，底部一键领取。
    /// 该原型只描述结构与占位内容，不接入任何游戏数据。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Mail/Prefabs/MailPanel.prefab", Order = 10, GeneratePanelCode = true)]
    public sealed class MailPanelProto : UIPrototypeDefinition
    {
        /// <summary>描述整个邮件界面的结构。</summary>
        public override ProtoNode Build()
        {
            return Panel("MailPanel")
                .Bg(UIProtoTheme.Dim, false)
                .Padding(56, 56, 40, 40)
                .Add(
                    Column("Frame")
                        .Flex()
                        .Bg(UIProtoTheme.PanelBg)
                        .Padding(36, 36, 28, 28)
                        .Spacing(20f)
                        .Add(BuildHeader(), BuildBody(), BuildFooter()));
        }

        /// <summary>顶部栏：返回、标题、分页与关闭。</summary>
        private static ProtoNode BuildHeader()
        {
            return Row("Header")
                .Height(76f)
                .Spacing(16f)
                .Add(
                    Button("BackBtn", "<")
                        .Width(76f)
                        .FontSize(32f),
                    Label("TitleLabel", "邮件")
                        .Width(220f)
                        .FontSize(40f),
                    Spacer("HeaderSpacer"),
                    Button("UnreadTabBtn", "未读 12").BindText()
                        .Width(200f)
                        .FontSize(24f)
                        .Bg(UIProtoTheme.Accent),
                    Button("ClaimedTabBtn", "已领取")
                        .Width(200f)
                        .FontSize(24f),
                    Button("CloseBtn", "X")
                        .Width(76f)
                        .FontSize(28f));
        }

        /// <summary>主体：左侧列表与右侧详情。</summary>
        private static ProtoNode BuildBody()
        {
            return Row("Body")
                .Flex()
                .Spacing(20f)
                .Add(BuildListSide(), BuildDetailSide());
        }

        /// <summary>左侧：无限滚动邮件列表与列表操作。</summary>
        private static ProtoNode BuildListSide()
        {
            return Column("ListSide")
                .Width(560f)
                .Spacing(12f)
                .Add(
                    ScrollProto.VList("MailList", "Assets/BundleResources/UI/Mail/Prefabs/MailListItem.prefab", examples: 3)
                        .Flex()
                        .Bg(UIProtoTheme.PanelBgAlt),
                    Row("ListActions")
                        .Height(68f)
                        .Spacing(12f)
                        .Add(
                            Button("SelectAllBtn", "全选")
                                .Flex()
                                .FontSize(24f),
                            Button("DeleteBtn", "删除")
                                .Flex()
                                .FontSize(24f)));
        }

        /// <summary>右侧：邮件正文、附件与领取。</summary>
        private static ProtoNode BuildDetailSide()
        {
            return Column("DetailSide")
                .Flex()
                .Bg(UIProtoTheme.PanelBgAlt)
                .Padding(28, 28, 24, 24)
                .Spacing(14f)
                .Add(
                    Label("DetailTitle", "征讨领域奖励").Bind()
                        .Height(48f)
                        .FontSize(32f),
                    Row("DetailMeta")
                        .Height(32f)
                        .Spacing(16f)
                        .Add(
                            Label("DetailSender", "发件人：派蒙").Bind()
                                .Flex()
                                .FontSize(22f)
                                .TextColor(UIProtoTheme.TextSecondary),
                            Label("DetailTime", "2026/09/08 12:00").Bind()
                                .Width(260f)
                                .FontSize(22f)
                                .TextColor(UIProtoTheme.TextSecondary)
                                .Align(ProtoAlign.Right)),
                    Divider(),
                    // 邮件正文长度不可控，必须放进滚动容器由内容撑高，而不是用 Flex 占满剩余空间后被截断。
                    ScrollBox("DetailBodyScroll")
                        .Flex()
                        .Spacing(12f)
                        .Add(
                            Label("DetailBody", "旅行者你好：\n\n感谢你完成本周的征讨领域挑战，这里是你应得的奖励，请注意查收。本次征讨领域的挑战难度较以往有所提升，你能够顺利通关实属不易。作为对你努力的认可，我们额外附赠了一份纪念礼物，希望你会喜欢。\n\n另外，提瓦特大陆近期将开启新一轮的秘境轮换，届时会有更多稀有材料产出，建议你提前做好准备。若在挑战过程中遇到任何问题，欢迎随时联系冒险家协会。\n\n愿风神护佑你的旅途。").Bind()
                                .FontSize(24f)
                                .VAlign(ProtoVAlign.Top)),
                    Label("AttachLabel", "附件")
                        .Height(32f)
                        .FontSize(22f)
                        .TextColor(UIProtoTheme.TextSecondary),
                    Grid("AttachGrid", 6, new Vector2(104f, 104f))
                        .Height(104f)
                        .Spacing(12f)
                        .Add(
                            Icon("AttachSlot1", new Vector2(104f, 104f)).Bind(),
                            Icon("AttachSlot2", new Vector2(104f, 104f)).Bind(),
                            Icon("AttachSlot3", new Vector2(104f, 104f)).Bind(),
                            Icon("AttachSlot4", new Vector2(104f, 104f)).Bind()),
                    Row("DetailActions")
                        .Height(76f)
                        .Spacing(16f)
                        .Add(
                            Spacer("DetailActionsSpacer"),
                            Button("ClaimBtn", "领取")
                                .Width(240f)
                                .FontSize(26f)
                                .Bg(UIProtoTheme.ButtonPrimary)
                                .TextColor(UIProtoTheme.TextInverse)));
        }

        /// <summary>底部：过期提示与一键领取。</summary>
        private static ProtoNode BuildFooter()
        {
            return Row("Footer")
                .Height(84f)
                .Spacing(16f)
                .Add(
                    Label("ExpireTip", "邮件保存 30 天后将自动删除")
                        .Flex()
                        .FontSize(22f)
                        .TextColor(UIProtoTheme.TextSecondary),
                    Button("ClaimAllBtn", "全部领取")
                        .Width(320f)
                        .FontSize(28f)
                        .Bg(UIProtoTheme.ButtonPrimary)
                        .TextColor(UIProtoTheme.TextInverse));
        }
    }
}
