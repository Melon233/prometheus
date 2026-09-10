using UnityEngine;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 背包格子原型：品质底色、左上角数量与锁定标记、右上角持有角色、中间道具图标、底部星级与等级。
    /// 背包条目上千，由 LoopGridView 循环复用该 Prefab，因此 Order 必须小于 BagPanelProto。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Bag/Prefabs/BagItem.prefab", Order = 0, DesignWidth = 150f, DesignHeight = 160f)]
    public sealed class BagItemProto : UIPrototypeDefinition
    {
        /// <summary>格子宽度，供 BagPanel 配置网格条目尺寸时复用。</summary>
        public const float Width = 150f;

        /// <summary>格子高度，供 BagPanel 配置网格条目尺寸时复用。</summary>
        public const float Height = 160f;

        /// <summary>底色节点名称，供 BagPanel 生成示例条目时按品质改色。</summary>
        public const string BackgroundNodeName = "Bg";

        /// <summary>描述单个背包格子的结构。</summary>
        public override ProtoNode Build()
        {
            return ScrollProto.GridItemColumn("BagItem")
                .With(go => go.AddComponent<BagItemMono>())
                .Size(Width, Height)
                .Bg(UIProtoTheme.QualityPurple)
                .Padding(6, 6, 6, 6)
                .Spacing(4f)
                .Align(ProtoAlign.Center)
                .Clickable()
                .Add(
                    Row("ItemMarks")
                        .Height(24f)
                        .Spacing(4f)
                        .Add(
                            Label("ItemCount", "1")
                                .Width(26f)
                                .FontSize(16f)
                                .Align(ProtoAlign.Center)
                                .TextColor(UIProtoTheme.TextInverse),
                            Icon("ItemLock", new Vector2(20f, 20f))
                                .Bg(UIProtoTheme.Alert),
                            Spacer("ItemMarksSpacer"),
                            Icon("ItemOwner", new Vector2(24f, 24f))
                                .Bg(UIProtoTheme.TextInverse)),
                    Icon("ItemIcon", new Vector2(72f, 72f))
                        .Bg(UIProtoTheme.TextInverse),
                    Row("ItemStars")
                        .Height(16f)
                        .Spacing(2f)
                        .Align(ProtoAlign.Center)
                        .Add(
                            Icon("ItemStar1", new Vector2(14f, 14f)).Bg(UIProtoTheme.Accent),
                            Icon("ItemStar2", new Vector2(14f, 14f)).Bg(UIProtoTheme.Accent),
                            Icon("ItemStar3", new Vector2(14f, 14f)).Bg(UIProtoTheme.Accent),
                            Icon("ItemStar4", new Vector2(14f, 14f)).Bg(UIProtoTheme.Accent),
                            Icon("ItemStar5", new Vector2(14f, 14f)).Bg(UIProtoTheme.Accent)),
                    Label("ItemLevel", "Lv.90")
                        .Height(24f)
                        .FontSize(18f)
                        .Align(ProtoAlign.Center)
                        .TextColor(UIProtoTheme.TextInverse)
                        .Truncate());
        }
    }
}
