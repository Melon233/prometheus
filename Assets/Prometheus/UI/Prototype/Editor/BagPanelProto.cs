using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 背包界面原型，参考原神的背包面板：顶部分类页签与容量统计，左侧为道具网格，右侧为选中道具详情，底部为排序与筛选。
    /// 背包容量以千计，道具网格使用 LoopGridView 循环复用条目，而不是一次性铺开全部道具。
    /// 该原型只描述结构与占位内容，不接入任何游戏数据。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Bag/Prefabs/BagPanel.prefab", Order = 10, GeneratePanelCode = true)]
    public sealed class BagPanelProto : UIPrototypeDefinition
    {
        private const string ItemPrefabPath = "Assets/BundleResources/UI/Bag/Prefabs/BagItem.prefab";
        private const int GridColumns = 8;

        private static readonly string[] CategoryIds = { "Weapon", "Artifact", "Material", "Food", "Gadget", "Quest", "Precious", "Furnishing" };
        private static readonly string[] CategoryLabels = { "武器", "圣遗物", "材料", "食物", "小道具", "任务", "贵重", "洞天" };

        /// <summary>描述整个背包界面的结构。</summary>
        public override ProtoNode Build()
        {
            return Panel("BagPanel")
                .Bg(UIProtoTheme.SurfaceDark, false)
                .Add(
                    Column("Root")
                        .Flex()
                        .Padding(24, 24, 20, 20)
                        .Spacing(16f)
                        .Add(BuildTopBar(), BuildBody(), BuildBottomBar()));
        }

        /// <summary>顶部：分类标识、分类页签、容量统计与关闭。</summary>
        private static ProtoNode BuildTopBar()
        {
            ProtoNode tabs = Row("CategoryTabs")
                .Spacing(12f)
                .VAlign(ProtoVAlign.Middle);

            for (int index = 0; index < CategoryIds.Length; index++)
            {
                ProtoNode tab = Button("Tab" + CategoryIds[index], CategoryLabels[index])
                    .Size(78f, 66f)
                    .FontSize(17f);
                if (index == 0) tab.Bg(UIProtoTheme.Accent);
                tabs.Add(tab);
            }

            return Row("TopBar")
                .Height(96f)
                .Spacing(14f)
                .VAlign(ProtoVAlign.Middle)
                .Add(
                    Icon("BagIcon", new Vector2(60f, 60f))
                        .Bg(UIProtoTheme.Accent),
                    Label("CategoryLabel", "武器").Bind()
                        .Width(120f)
                        .FontSize(28f)
                        .TextColor(UIProtoTheme.TextInverse)
                        .Truncate(),
                    Button("PrevCategoryBtn", "Q")
                        .Size(52f, 52f)
                        .FontSize(20f),
                    tabs,
                    Button("NextCategoryBtn", "E")
                        .Size(52f, 52f)
                        .FontSize(20f),
                    Spacer("TopBarSpacer"),
                    Label("CapacityLabel", "武器 272/2000").Bind()
                        .Width(280f)
                        .FontSize(24f)
                        .TextColor(UIProtoTheme.TextInverse)
                        .Align(ProtoAlign.Right),
                    Button("CloseBtn", "X")
                        .Size(64f, 64f)
                        .FontSize(28f));
        }

        /// <summary>主体：左侧道具网格与右侧详情面板。</summary>
        private static ProtoNode BuildBody()
        {
            return Row("Body")
                .Flex()
                .Spacing(20f)
                .Add(
                    // 背包容量以千计，这里必须是循环网格；五个示例条目按灰绿蓝紫金五种品质着色，仅供编辑期查看效果。
                    ScrollProto.GridView(
                            "BagGrid",
                            ItemPrefabPath,
                            GridColumns,
                            new Vector2(BagItemProto.Width, BagItemProto.Height),
                            new Vector2(10f, 10f),
                            examples: UIProtoTheme.Qualities.Length,
                            decorateExample: TintExampleByQuality)
                        .Flex(),
                    BuildDetailPanel());
        }

        /// <summary>把示例条目的底色改成对应品质，用于在编辑器中确认五种品质的观感。</summary>
        private static void TintExampleByQuality(GameObject example, int index)
        {
            Transform background = example.transform.Find(BagItemProto.BackgroundNodeName);
            if (background == null) return;
            Image image = background.GetComponent<Image>();
            if (image != null) image.color = UIProtoTheme.Qualities[index % UIProtoTheme.Qualities.Length];
        }

        /// <summary>右侧详情：品质色头部、等级与精炼、效果说明、装备者与详情按钮。</summary>
        private static ProtoNode BuildDetailPanel()
        {
            return Column("DetailPanel")
                .Width(520f)
                .Bg(UIProtoTheme.PanelBgAlt)
                .Spacing(0f)
                .Add(
                    Column("DetailHeader").BindBackground()
                        .Bg(UIProtoTheme.QualityGold)
                        .Padding(20, 20, 16, 16)
                        .Spacing(6f)
                        .Add(
                            Label("DetailName", "阿莫斯之弓").Bind()
                                .Height(48f)
                                .FontSize(32f)
                                .TextColor(UIProtoTheme.TextInverse)
                                .Truncate(),
                            Label("DetailType", "弓").Bind()
                                .Height(28f)
                                .FontSize(20f)
                                .TextColor(UIProtoTheme.TextInverse),
                            Label("DetailMainStatLabel", "攻击力").Bind()
                                .Height(24f)
                                .FontSize(18f)
                                .TextColor(UIProtoTheme.TextInverse),
                            Label("DetailMainStatValue", "49.6%").Bind()
                                .Height(34f)
                                .FontSize(26f)
                                .TextColor(UIProtoTheme.TextInverse),
                            Label("DetailBaseAtkLabel", "基础攻击力")
                                .Height(24f)
                                .FontSize(18f)
                                .TextColor(UIProtoTheme.TextInverse),
                            Label("DetailBaseAtkValue", "608").Bind()
                                .Height(42f)
                                .FontSize(34f)
                                .TextColor(UIProtoTheme.TextInverse),
                            BuildDetailStarRow()),
                    Column("DetailBody")
                        .Flex()
                        .Padding(20, 20, 16, 16)
                        .Spacing(12f)
                        .Add(
                            Row("DetailLevelRow")
                                .Height(44f)
                                .Spacing(10f)
                                .VAlign(ProtoVAlign.Middle)
                                .Add(
                                    Label("DetailLevel", "Lv.90 / 90").Bind()
                                        .Width(160f)
                                        .FontSize(24f),
                                    Spacer("DetailLevelSpacer"),
                                    Button("LockBtn", "锁")
                                        .Size(44f, 44f)
                                        .FontSize(18f)),
                            Row("DetailRefineRow")
                                .Height(34f)
                                .Spacing(10f)
                                .VAlign(ProtoVAlign.Middle)
                                .Add(
                                    Label("RefineRank", "1").Bind()
                                        .Width(30f)
                                        .FontSize(18f)
                                        .Align(ProtoAlign.Center),
                                    Label("RefineLabel", "精炼1阶").Bind()
                                        .Flex()
                                        .FontSize(22f)),
                            // 武器效果与背景故事长度不固定，放进滚动容器由内容撑高。
                            ScrollBox("DetailTextScroll")
                                .Flex()
                                .Spacing(10f)
                                .Add(
                                    Label("EffectTitle", "矢志不忘").Bind()
                                        .Height(32f)
                                        .FontSize(22f)
                                        .VAlign(ProtoVAlign.Top),
                                    Label("EffectDesc", "普通攻击与重击造成的伤害提升12%；普通攻击与重击的箭矢发射后每经过0.1秒，伤害还会提升8%，至多提升5次。").Bind()
                                        .FontSize(20f)
                                        .VAlign(ProtoVAlign.Top),
                                    Label("FlavorText", "极为古老的弓。即使原主不复存在，其中蕴藏之力依旧——那种力量无主，却在万物当中。距离心系之物越是遥远，那种力量愈是剧烈。").Bind()
                                        .FontSize(19f)
                                        .TextColor(UIProtoTheme.TextSecondary)
                                        .VAlign(ProtoVAlign.Top)),
                            Row("EquippedRow").Bind()
                                .Height(60f)
                                .Padding(12, 12, 0, 0)
                                .Spacing(12f)
                                .Bg(UIProtoTheme.ItemBg)
                                .VAlign(ProtoVAlign.Middle)
                                .Add(
                                    Icon("EquippedAvatar", new Vector2(40f, 40f)).Bind()
                                        .Bg(UIProtoTheme.Accent),
                                    Label("EquippedLabel", "安柏已装备").Bind()
                                        .Flex()
                                        .FontSize(22f)
                                        .Truncate()),
                            Row("DetailActions")
                                .Height(64f)
                                .Spacing(12f)
                                .VAlign(ProtoVAlign.Middle)
                                .Add(
                                    Spacer("DetailActionsSpacer"),
                                    Button("DetailHintBtn", "F")
                                        .Size(52f, 52f)
                                        .FontSize(20f),
                                    Button("DetailBtn", "详情")
                                        .Size(200f, 60f)
                                        .FontSize(26f)
                                        .Bg(UIProtoTheme.ButtonPrimary)
                                        .TextColor(UIProtoTheme.TextInverse))));
        }

        /// <summary>详情头部的五星星级行。</summary>
        private static ProtoNode BuildDetailStarRow()
        {
            ProtoNode row = Row("DetailStarRow")
                .Height(32f)
                .Spacing(6f)
                .VAlign(ProtoVAlign.Middle);

            for (int index = 1; index <= 5; index++)
                row.Add(Icon("DetailStar" + index, new Vector2(28f, 28f)).Bind().Bg(UIProtoTheme.TextInverse));

            return row.Add(Spacer("DetailStarSpacer"));
        }

        /// <summary>底部：整理、排序方式与筛选。</summary>
        private static ProtoNode BuildBottomBar()
        {
            return Row("BottomBar")
                .Height(84f)
                .Spacing(14f)
                .VAlign(ProtoVAlign.Middle)
                .Add(
                    Button("DiscardBtn", "整理")
                        .Size(64f, 64f)
                        .FontSize(20f),
                    Button("SortModeBtn", "品质顺序").BindText()
                        .Size(300f, 64f)
                        .FontSize(24f),
                    Button("SortOrderBtn", "升降")
                        .Size(64f, 64f)
                        .FontSize(20f),
                    Button("LockFilterBtn", "筛选")
                        .Size(64f, 64f)
                        .FontSize(20f),
                    Spacer("BottomBarSpacer"));
        }
    }
}
