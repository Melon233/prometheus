using UnityEngine;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 角色界面顶部角色列表的列表项原型：圆形头像加选中下划线。
    /// 角色数量会持续增长，列表由 LoopListView2 循环复用该 Prefab，因此 Order 必须小于 CharacterPanelProto。
    /// </summary>
    [UIPrototype("Assets/BundleResources/UI/Character/Prefabs/CharacterTabItem.prefab", Order = 0, DesignWidth = 104f, DesignHeight = 104f)]
    public sealed class CharacterTabItemProto : UIPrototypeDefinition
    {
        /// <summary>描述单个角色头像条目的结构。</summary>
        public override ProtoNode Build()
        {
            return ScrollProto.ListItemColumn("CharacterTabItem")
                .Size(104f, 104f)
                .Padding(6, 6, 4, 4)
                .Spacing(6f)
                .Align(ProtoAlign.Center)
                .Clickable()
                .Add(
                    Icon("TabAvatar", new Vector2(80f, 80f))
                        .Bg(UIProtoTheme.Accent),
                    Icon("TabSelectedMark", new Vector2(64f, 6f))
                        .Bg(UIProtoTheme.TextInverse, false));
        }
    }
}
