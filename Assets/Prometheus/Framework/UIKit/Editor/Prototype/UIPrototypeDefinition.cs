using System;
using UnityEngine;

namespace Xuan.Prometheus.Editor.Prototype
{
    /// <summary>
    /// 所有 UI 原型定义的基类。子类只描述界面结构，不接触 GameObject、RectTransform 或任何 Unity 场景 API。
    /// </summary>
    public abstract class UIPrototypeDefinition
    {
        /// <summary>返回该界面的完整结构树；实现中只允许使用本类提供的工厂方法。</summary>
        public abstract ProtoNode Build();

        /// <summary>创建面板根节点，纵向排布子节点，实例化时挂载 UIComponentBinder 与 RaycastBlocker。</summary>
        public static ProtoNode Panel(string name) => new ProtoNode(name, ProtoRole.Panel, ProtoLayout.Column);

        /// <summary>创建可复用子 Prefab 的根节点，横向排布子节点。</summary>
        public static ProtoNode ItemRow(string name) => new ProtoNode(name, ProtoRole.Item, ProtoLayout.Row);

        /// <summary>创建可复用子 Prefab 的根节点，纵向排布子节点。</summary>
        public static ProtoNode ItemColumn(string name) => new ProtoNode(name, ProtoRole.Item, ProtoLayout.Column);

        /// <summary>创建纵向容器。</summary>
        public static ProtoNode Column(string name) => new ProtoNode(name, ProtoRole.Box, ProtoLayout.Column);

        /// <summary>创建横向容器。</summary>
        public static ProtoNode Row(string name) => new ProtoNode(name, ProtoRole.Box, ProtoLayout.Row);

        /// <summary>
        /// 创建可纵向滚动的容器；子节点纵向排布在内容区，内容区高度由子节点撑开而不受容器高度限制。
        /// 长度不可控的正文应放进这里，而不是靠 Flex 占满剩余空间。
        /// </summary>
        public static ProtoNode ScrollBox(string name) => new ProtoNode(name, ProtoRole.ScrollBox, ProtoLayout.Column);

        /// <summary>创建固定列数的网格容器。</summary>
        public static ProtoNode Grid(string name, int columns, Vector2 cell)
        {
            ProtoNode node = new ProtoNode(name, ProtoRole.Box, ProtoLayout.Grid);
            node.GridColumnsValue = columns;
            node.GridCellValue = cell;
            return node;
        }

        /// <summary>创建文本节点。</summary>
        public static ProtoNode Label(string name, string text) => new ProtoNode(name, ProtoRole.Label, ProtoLayout.None).Text(text);

        /// <summary>创建按钮节点；按钮总是会被登记进 UIComponentBinder 以便生成点击回调。</summary>
        public static ProtoNode Button(string name, string text) => new ProtoNode(name, ProtoRole.Button, ProtoLayout.None).Text(text).Bind();

        /// <summary>创建图片占位节点。</summary>
        public static ProtoNode Icon(string name, Vector2 size) => new ProtoNode(name, ProtoRole.Icon, ProtoLayout.None).Size(size.x, size.y);

        /// <summary>创建带填充比例的进度条节点。</summary>
        public static ProtoNode Bar(string name) => new ProtoNode(name, ProtoRole.Bar, ProtoLayout.None);

        /// <summary>创建在主轴方向撑开剩余空间的占位节点。</summary>
        public static ProtoNode Spacer(string name = "Spacer") => new ProtoNode(name, ProtoRole.Spacer, ProtoLayout.None).Flex();

        /// <summary>创建由扩展程序集提供实例化逻辑的叶子节点，供 UIKit 之外的组件封装使用。</summary>
        public static ProtoNode Custom(string name, Action<GameObject> build)
        {
            ProtoNode node = new ProtoNode(name, ProtoRole.Custom, ProtoLayout.None);
            return node.With(build);
        }

        /// <summary>创建一条分隔线。</summary>
        public static ProtoNode Divider(string name = "Divider") => new ProtoNode(name, ProtoRole.Icon, ProtoLayout.None).Height(2f).Bg(UIProtoTheme.Divider, false);
    }
}
