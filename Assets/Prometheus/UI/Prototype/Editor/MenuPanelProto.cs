using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 主菜单界面原型，参考原神的菜单面板：最左侧常驻功能栏，右侧主面板上方为玩家资料卡，下方为可滚动的功能入口网格。
    /// 面板只占屏幕左侧，右侧留空以露出游戏画面。该原型只描述结构与占位内容，不接入任何游戏数据。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Menu/Prefabs/MenuPanel.prefab", Order = 10, GeneratePanelCode = true)]
    public sealed class MenuPanelProto : UIPrototypeDefinition
    {
        /// <summary>描述一个功能入口格子的占位内容。</summary>
        private readonly struct Entry
        {
            /// <summary>创建一个功能入口描述。</summary>
            public Entry(string id, string label, bool badge)
            {
                Id = id;
                Label = label;
                Badge = badge;
            }

            /// <summary>获取用于拼接节点名称的稳定标识。</summary>
            public string Id { get; }

            /// <summary>获取入口显示名称。</summary>
            public string Label { get; }

            /// <summary>获取该入口是否显示未读红点。</summary>
            public bool Badge { get; }
        }

        private static readonly Entry[] Entries =
        {
            new Entry("Shop", "商城", true),
            new Entry("Party", "队伍配置", false),
            new Entry("Friends", "好友", false),
            new Entry("Achievement", "成就", true),
            new Entry("Archive", "图鉴", false),
            new Entry("CharacterArchive", "角色图鉴", false),
            new Entry("Character", "角色", false),
            new Entry("Guide", "提升指南", false),
            new Entry("Bag", "背包", false),
            new Entry("Quest", "任务", false),
            new Entry("Map", "地图", false),
            new Entry("Event", "活动", false),
            new Entry("Handbook", "冒险之证", false),
            new Entry("Wish", "祈愿", false),
            new Entry("BattlePass", "纪行", false),
            new Entry("Coop", "多人游戏", false),
            new Entry("SpecialEvent", "特典活动", true),
            new Entry("Community", "官方社区", true),
            new Entry("Highlights", "版本热点", true),
            new Entry("Feedback", "反馈", false)
        };

        /// <summary>描述整个菜单界面的结构。</summary>
        public override ProtoNode Build()
        {
            return Panel("MenuPanel")
                .Add(
                    Row("Root")
                        .Flex()
                        .Add(
                            BuildSideRail(),
                            BuildMainPanel(),
                            // 右侧不铺满，露出游戏画面，与原神菜单一致。
                            Spacer("WorldSpacer")));
        }

        /// <summary>最左侧常驻功能栏：顶部返回，中部快捷入口，底部设置。</summary>
        private static ProtoNode BuildSideRail()
        {
            return Column("SideRail")
                .Width(110f)
                .Bg(UIProtoTheme.SurfaceDark)
                .Padding(18, 18, 20, 20)
                .Spacing(20f)
                .Align(ProtoAlign.Center)
                .Add(
                    Button("BackBtn", "<")
                        .Size(74f, 74f)
                        .FontSize(30f)
                        .TextColor(UIProtoTheme.TextInverse),
                    Button("CameraBtn", "拍照")
                        .Size(74f, 74f)
                        .FontSize(18f)
                        .TextColor(UIProtoTheme.TextInverse),
                    Button("GiftBtn", "纪念品")
                        .Size(74f, 74f)
                        .FontSize(18f)
                        .TextColor(UIProtoTheme.TextInverse),
                    Button("MailBtn", "邮件")
                        .Size(74f, 74f)
                        .FontSize(18f)
                        .TextColor(UIProtoTheme.TextInverse),
                    Button("CompassBtn", "指南")
                        .Size(74f, 74f)
                        .FontSize(18f)
                        .TextColor(UIProtoTheme.TextInverse),
                    Spacer("RailSpacer"),
                    Button("SettingsBtn", "设置")
                        .Size(74f, 74f)
                        .FontSize(18f)
                        .TextColor(UIProtoTheme.TextInverse));
        }

        /// <summary>主面板：上方资料卡，下方可滚动的功能入口网格。</summary>
        private static ProtoNode BuildMainPanel()
        {
            return Column("MainPanel")
                .Width(700f)
                .Bg(UIProtoTheme.PanelBg)
                .Padding(20, 20, 20, 20)
                .Spacing(16f)
                .Add(BuildProfileCard(), BuildMenuGrid());
        }

        /// <summary>玩家资料卡：头像、昵称签名、UID 与四项账号数据。</summary>
        private static ProtoNode BuildProfileCard()
        {
            return Column("ProfileCard")
                .Bg(UIProtoTheme.ProfileBg)
                .Padding(20, 20, 18, 18)
                .Spacing(10f)
                .Add(
                    Row("ProfileTop")
                        .Height(120f)
                        .Spacing(20f)
                        .Add(
                            Icon("AvatarIcon", new Vector2(110f, 110f)).Bind()
                                .Bg(UIProtoTheme.Accent),
                            Column("NameArea")
                                .Flex()
                                .Spacing(6f)
                                .VAlign(ProtoVAlign.Middle)
                                .Add(
                                    Label("PlayerName", "雨祈").Bind()
                                        .Height(50f)
                                        .FontSize(38f)
                                        .Truncate(),
                                    Label("Signature", "让刻晴再次伟大").Bind()
                                        .Height(32f)
                                        .FontSize(22f)
                                        .TextColor(UIProtoTheme.TextSecondary)
                                        .Truncate()),
                            Button("EditProfileBtn", "编辑")
                                .Size(56f, 56f)
                                .FontSize(18f)),
                    Row("UidRow")
                        .Height(40f)
                        .Spacing(12f)
                        .Add(
                            Label("UidLabel", "UID 115727092").Bind()
                                .Flex()
                                .FontSize(22f),
                            Button("CopyUidBtn", "复制")
                                .Width(110f)
                                .FontSize(20f)),
                    BuildStatRow("AdventureRank", "冒险等阶", "59", true),
                    Row("ExpRow")
                        .Height(36f)
                        .Spacing(12f)
                        .Add(
                            Label("ExpLabel", "冒险阅历")
                                .Width(150f)
                                .FontSize(20f)
                                .TextColor(UIProtoTheme.TextSecondary),
                            Bar("ExpBar").Bind()
                                .Flex()
                                .Height(16f)
                                .Fill(0.02f),
                            Label("ExpValue", "5997 / 340125").Bind()
                                .Width(190f)
                                .FontSize(18f)
                                .TextColor(UIProtoTheme.TextSecondary)
                                .Align(ProtoAlign.Right)),
                    BuildStatRow("WorldLevel", "世界等级", "8", true),
                    BuildStatRow("Birthday", "生日", "9月11日", false));
        }

        /// <summary>构造资料卡中一行「名称 — 数值 — 提示图标」。</summary>
        private static ProtoNode BuildStatRow(string id, string label, string value, bool hasHint)
        {
            ProtoNode row = Row(id + "Row")
                .Height(44f)
                .Spacing(10f)
                .Add(
                    Label(id + "Label", label)
                        .Flex()
                        .FontSize(24f),
                    Label(id + "Value", value).Bind()
                        .Width(200f)
                        .FontSize(26f)
                        .Align(ProtoAlign.Right));

            if (hasHint)
                row.Add(Icon(id + "Hint", new Vector2(30f, 30f)).Bg(UIProtoTheme.Accent));

            return row;
        }

        /// <summary>功能入口网格；条目总高超过视口，因此整体放进滚动容器。</summary>
        private static ProtoNode BuildMenuGrid()
        {
            List<ProtoNode> tiles = new List<ProtoNode>();
            foreach (Entry entry in Entries)
                tiles.Add(BuildTile(entry));

            return ScrollBox("MenuScroll")
                .Flex()
                .Add(
                    Grid("MenuGrid", 4, new Vector2(156f, 150f))
                        .Spacing(12f)
                        .Add(tiles.ToArray()));
        }

        /// <summary>构造一个功能入口格子：右上角红点、居中图标、底部名称，整块可点击。</summary>
        private static ProtoNode BuildTile(Entry entry)
        {
            ProtoNode badgeRow = Row("Badge" + entry.Id + "Row")
                .Height(16f)
                .Add(Spacer("Badge" + entry.Id + "Spacer"));

            if (entry.Badge)
                badgeRow.Add(Icon("Badge" + entry.Id, new Vector2(16f, 16f)).Bind().Bg(UIProtoTheme.Alert));

            return Column("Tile" + entry.Id)
                .Bg(UIProtoTheme.ButtonPrimary)
                .Padding(10, 10, 10, 10)
                .Spacing(6f)
                .Align(ProtoAlign.Center)
                .Clickable()
                .Add(
                    badgeRow,
                    Icon("Icon" + entry.Id, new Vector2(70f, 70f))
                        .Bg(UIProtoTheme.TextInverse),
                    Label("Label" + entry.Id, entry.Label)
                        .Height(28f)
                        .FontSize(21f)
                        .TextColor(UIProtoTheme.TextInverse)
                        .Align(ProtoAlign.Center)
                        .Truncate());
        }
    }
}
