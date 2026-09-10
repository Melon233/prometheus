using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 角色界面原型，参考原神的角色面板：顶部为可横向循环的角色列表，左侧为分页标签，中间为角色模型预览，右侧为属性面板，底部为行动按钮。
    /// 角色数量会持续增长，顶部列表使用 LoopListView2 循环复用条目，而不是一次性铺开全部角色。
    /// 该原型只描述结构与占位内容，不接入任何游戏数据。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Character/Prefabs/CharacterPanel.prefab", Order = 10, GeneratePanelCode = true)]
    public sealed class CharacterPanelProto : UIPrototypeDefinition
    {
        /// <summary>右侧属性面板中的一项占位数据。</summary>
        private readonly struct Stat
        {
            /// <summary>创建一项属性描述。</summary>
            public Stat(string id, string label, string value)
            {
                Id = id;
                Label = label;
                Value = value;
            }

            /// <summary>获取用于拼接节点名称的稳定标识。</summary>
            public string Id { get; }

            /// <summary>获取属性名称。</summary>
            public string Label { get; }

            /// <summary>获取属性数值。</summary>
            public string Value { get; }
        }

        private static readonly Stat[] Stats =
        {
            new Stat("Hp", "生命值上限", "31,467"),
            new Stat("Atk", "攻击力", "1,444"),
            new Stat("Def", "防御力", "1,105"),
            new Stat("Mastery", "元素精通", "77"),
            new Stat("Stamina", "体力上限", "240")
        };

        private static readonly string[] TabIds = { "Attribute", "Weapon", "Artifact", "Constellation", "Talent", "Profile" };
        private static readonly string[] TabLabels = { "属性", "武器", "圣遗物", "命之座", "天赋", "资料" };

        /// <summary>描述整个角色界面的结构。</summary>
        public override ProtoNode Build()
        {
            return Panel("CharacterPanel")
                .Bg(UIProtoTheme.SurfaceDark, false)
                .Add(
                    Column("Root")
                        .Flex()
                        .Padding(24, 24, 20, 20)
                        .Spacing(16f)
                        .Add(BuildTopBar(), BuildBody(), BuildBottomBar()));
        }

        /// <summary>顶部：元素与角色名、可横向循环的角色列表、关闭按钮。</summary>
        private static ProtoNode BuildTopBar()
        {
            return Row("TopBar")
                .Height(104f)
                .Spacing(16f)
                .VAlign(ProtoVAlign.Middle)
                .Add(
                    Icon("ElementBadge", new Vector2(68f, 68f)).Bind()
                        .Bg(UIProtoTheme.Alert),
                    Label("ElementLabel", "火元素 / 胡桃").Bind()
                        .Width(260f)
                        .FontSize(26f)
                        .TextColor(UIProtoTheme.TextInverse)
                        .Truncate(),
                    Button("PrevCharacterBtn", "Q")
                        .Size(56f, 56f)
                        .FontSize(22f),
                    // 角色数量持续增长，这里必须是循环列表而不是把全部角色铺开。
                    ScrollProto.HList("CharacterList", "Assets/BundleResources/UI/Character/Prefabs/CharacterTabItem.prefab", 10f, examples: 3)
                        .Flex(),
                    Button("NextCharacterBtn", "E")
                        .Size(56f, 56f)
                        .FontSize(22f),
                    Button("CloseBtn", "X")
                        .Size(68f, 68f)
                        .FontSize(28f));
        }

        /// <summary>主体：左侧分页标签、中间模型预览、右侧属性面板。</summary>
        private static ProtoNode BuildBody()
        {
            return Row("Body")
                .Flex()
                .Spacing(20f)
                .Add(BuildTabColumn(), BuildPreviewArea(), BuildInfoPanel());
        }

        /// <summary>左侧分页标签列。</summary>
        private static ProtoNode BuildTabColumn()
        {
            ProtoNode column = Column("TabColumn")
                .Width(280f)
                .Spacing(10f)
                .Add(
                    Label("MoveKeyHint", "W / S")
                        .Height(40f)
                        .FontSize(20f)
                        .TextColor(UIProtoTheme.TextSecondary));

            for (int index = 0; index < TabIds.Length; index++)
                column.Add(BuildTab(TabIds[index], TabLabels[index], index == 0, index == TabIds.Length - 1));

            return column.Add(Spacer("TabColumnSpacer"));
        }

        /// <summary>构造一个分页标签；选中项使用强调底色，末项带未读红点。</summary>
        private static ProtoNode BuildTab(string id, string label, bool selected, bool hasBadge)
        {
            ProtoNode tab = Row("Tab" + id)
                .Height(64f)
                .Padding(14, 14, 0, 0)
                .Spacing(12f)
                .VAlign(ProtoVAlign.Middle)
                .Clickable()
                .Add(
                    Icon("Tab" + id + "Mark", new Vector2(18f, 18f))
                        .Bg(UIProtoTheme.Accent),
                    Label("Tab" + id + "Label", label)
                        .Flex()
                        .FontSize(30f)
                        .TextColor(UIProtoTheme.TextInverse)
                        .Truncate());

            if (selected) tab.Bg(UIProtoTheme.ButtonPrimary);
            if (hasBadge) tab.Add(Icon("Tab" + id + "Badge", new Vector2(18f, 18f)).Bind().Bg(UIProtoTheme.Alert));
            return tab;
        }

        /// <summary>中间的角色模型预览占位区。</summary>
        private static ProtoNode BuildPreviewArea()
        {
            return Column("PreviewArea")
                .Flex()
                .Align(ProtoAlign.Center)
                .VAlign(ProtoVAlign.Middle)
                .Add(
                    Label("PreviewPlaceholder", "角色模型预览")
                        .Height(48f)
                        .FontSize(28f)
                        .TextColor(UIProtoTheme.TextSecondary)
                        .Align(ProtoAlign.Center));
        }

        /// <summary>右侧属性面板：姓名、星级、等级、属性列表、好感与角色简介。</summary>
        private static ProtoNode BuildInfoPanel()
        {
            ProtoNode panel = Column("InfoPanel")
                .Width(460f)
                .Bg(UIProtoTheme.PanelBgAlt)
                .Padding(24, 24, 20, 20)
                .Spacing(12f)
                .Add(
                    Label("CharacterName", "胡桃").Bind()
                        .Height(56f)
                        .FontSize(40f)
                        .Truncate(),
                    BuildStarRow(),
                    Row("LevelRow")
                        .Height(44f)
                        .Spacing(10f)
                        .VAlign(ProtoVAlign.Middle)
                        .Add(
                            Label("LevelLabel", "等级90 / 90").Bind()
                                .Flex()
                                .FontSize(28f),
                            Icon("LevelHint", new Vector2(36f, 36f))
                                .Bg(UIProtoTheme.Accent)),
                    Divider());

            foreach (Stat stat in Stats)
                panel.Add(BuildStatRow(stat));

            return panel.Add(
                Button("DetailBtn", "详细信息")
                    .Height(56f)
                    .FontSize(24f),
                Row("FriendshipRow")
                    .Height(40f)
                    .Spacing(10f)
                    .VAlign(ProtoVAlign.Middle)
                    .Add(
                        Icon("FriendshipIcon", new Vector2(26f, 26f))
                            .Bg(UIProtoTheme.Accent),
                        Label("FriendshipLabel", "好感")
                            .Flex()
                            .FontSize(24f),
                        Label("FriendshipValue", "10").Bind()
                            .Width(90f)
                            .FontSize(26f)
                            .Align(ProtoAlign.Right)),
                Bar("FriendshipBar").Bind()
                    .Height(12f)
                    .Fill(0.62f),
                // 角色简介长度不固定，放进滚动容器由内容撑高。
                ScrollBox("DescriptionScroll")
                    .Flex()
                    .Add(
                        Label("DescriptionLabel", "「往生堂」七十七代堂主，年纪轻轻就已主掌璃月的葬仪事务。").Bind()
                            .FontSize(21f)
                            .TextColor(UIProtoTheme.TextSecondary)
                            .VAlign(ProtoVAlign.Top)));
        }

        /// <summary>构造五颗星的星级行。</summary>
        private static ProtoNode BuildStarRow()
        {
            ProtoNode row = Row("StarRow")
                .Height(34f)
                .Spacing(6f)
                .VAlign(ProtoVAlign.Middle);

            for (int index = 1; index <= 5; index++)
                row.Add(Icon("Star" + index, new Vector2(30f, 30f)).Bind().Bg(UIProtoTheme.Accent));

            return row.Add(Spacer("StarRowSpacer"));
        }

        /// <summary>构造一行「图标 — 属性名 — 数值」。</summary>
        private static ProtoNode BuildStatRow(Stat stat)
        {
            return Row("Stat" + stat.Id + "Row")
                .Height(42f)
                .Spacing(10f)
                .VAlign(ProtoVAlign.Middle)
                .Add(
                    Icon("Stat" + stat.Id + "Icon", new Vector2(26f, 26f))
                        .Bg(UIProtoTheme.Accent),
                    Label("Stat" + stat.Id + "Label", stat.Label)
                        .Flex()
                        .FontSize(23f),
                    Label("Stat" + stat.Id + "Value", stat.Value).Bind()
                        .Width(140f)
                        .FontSize(25f)
                        .Align(ProtoAlign.Right));
        }

        /// <summary>底部：提升指南与上限突破。</summary>
        private static ProtoNode BuildBottomBar()
        {
            return Row("BottomBar")
                .Height(84f)
                .Spacing(16f)
                .VAlign(ProtoVAlign.Middle)
                .Add(
                    Button("LayoutToggleBtn", "田")
                        .Size(72f, 72f)
                        .FontSize(26f),
                    Button("GuideBtn", "提升指南")
                        .Size(260f, 72f)
                        .FontSize(26f),
                    Spacer("BottomBarSpacer"),
                    Button("AscendHintBtn", "R")
                        .Size(72f, 72f)
                        .FontSize(22f),
                    Button("AscendBtn", "上限突破")
                        .Size(280f, 72f)
                        .FontSize(26f)
                        .Bg(UIProtoTheme.ButtonPrimary)
                        .TextColor(UIProtoTheme.TextInverse));
        }
    }
}
