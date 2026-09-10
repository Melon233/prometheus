using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Editor.Prototype
{
    /// <summary>描述一个原型节点为其子节点提供的排布方式。</summary>
    public enum ProtoLayout
    {
        /// <summary>叶子节点，不排布子节点。</summary>
        None,
        /// <summary>纵向依次排布子节点。</summary>
        Column,
        /// <summary>横向依次排布子节点。</summary>
        Row,
        /// <summary>按固定列数的网格排布子节点。</summary>
        Grid
    }

    /// <summary>描述一个原型节点的语义角色，决定它实例化成哪些 Unity 组件。</summary>
    public enum ProtoRole
    {
        /// <summary>面板根节点，实例化时挂载 UIComponentBinder 与 RaycastBlocker。</summary>
        Panel,
        /// <summary>列表项等可复用子 Prefab 的根节点，不挂载面板根组件。</summary>
        Item,
        /// <summary>纯容器节点。</summary>
        Box,
        /// <summary>文本节点。</summary>
        Label,
        /// <summary>按钮节点，自带底图、Button 与内部文本。</summary>
        Button,
        /// <summary>图片节点。</summary>
        Icon,
        /// <summary>带填充比例的进度条节点。</summary>
        Bar,
        /// <summary>占位节点，在主轴方向撑开剩余空间。</summary>
        Spacer,
        /// <summary>可纵向滚动的容器节点，子节点排布在其内容区并由内容高度撑开。</summary>
        ScrollBox,
        /// <summary>由外部程序集提供实例化逻辑的扩展节点。</summary>
        Custom
    }

    /// <summary>描述原型文本的水平对齐方式。</summary>
    public enum ProtoAlign
    {
        /// <summary>左对齐。</summary>
        Left,
        /// <summary>居中对齐。</summary>
        Center,
        /// <summary>右对齐。</summary>
        Right
    }

    /// <summary>描述原型文本的垂直对齐方式。</summary>
    public enum ProtoVAlign
    {
        /// <summary>顶对齐，适合多行正文。</summary>
        Top,
        /// <summary>垂直居中，适合固定高度的单行文本。</summary>
        Middle,
        /// <summary>底对齐。</summary>
        Bottom
    }

    /// <summary>
    /// 原型界面的中间表示节点：一棵纯数据树，不持有任何 Unity 场景对象。
    /// 该类型刻意不提供 anchor、pivot、anchoredPosition 等坐标接口，所有定位一律交给布局组件完成。
    /// </summary>
    public sealed class ProtoNode
    {
        private readonly List<ProtoNode> children = new List<ProtoNode>();

        /// <summary>创建一个指定名称与角色的原型节点，仅供工厂方法调用。</summary>
        internal ProtoNode(string name, ProtoRole role, ProtoLayout layout)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("ProtoNode requires a non-empty name.", nameof(name));
            Name = name.Trim();
            Role = role;
            Layout = layout;
        }

        /// <summary>获取节点名称，同时作为 GameObject 名称和绑定名称。</summary>
        public string Name { get; private set; }

        /// <summary>获取节点语义角色。</summary>
        public ProtoRole Role { get; private set; }

        /// <summary>获取节点为子节点提供的排布方式。</summary>
        public ProtoLayout Layout { get; private set; }

        /// <summary>获取只读子节点列表。</summary>
        public IReadOnlyList<ProtoNode> Children => children;

        internal RectOffset PaddingValue = new RectOffset();
        internal float SpacingValue;
        internal float? WidthValue;
        internal float? HeightValue;
        internal float FlexValue;
        internal Color? BackgroundValue;
        internal bool BackgroundSliced = true;
        internal string TextValue = string.Empty;
        internal float FontSizeValue = 24f;
        internal Color? TextColorValue;
        internal ProtoAlign TextAlignValue = ProtoAlign.Left;
        internal ProtoVAlign TextVAlignValue = ProtoVAlign.Middle;
        internal bool AlignExplicit;
        internal bool VAlignExplicit;
        internal bool ClickableValue;
        internal bool BindTextValue;
        internal bool BindBackgroundValue;
        internal bool TruncateValue;
        internal int GridColumnsValue = 1;
        internal Vector2 GridCellValue = new Vector2(100f, 100f);
        internal float FillValue = 1f;
        internal bool BoundValue;
        internal Type BindTypeValue;
        internal Action<GameObject> CustomizeAction;

        /// <summary>设置四周内边距。</summary>
        public ProtoNode Padding(int all) => Padding(all, all, all, all);

        /// <summary>分别设置水平和垂直内边距。</summary>
        public ProtoNode Padding(int horizontal, int vertical) => Padding(horizontal, horizontal, vertical, vertical);

        /// <summary>分别设置左右上下内边距。</summary>
        public ProtoNode Padding(int left, int right, int top, int bottom)
        {
            PaddingValue = new RectOffset(left, right, top, bottom);
            return this;
        }

        /// <summary>设置子节点之间的间距。</summary>
        public ProtoNode Spacing(float spacing)
        {
            SpacingValue = spacing;
            return this;
        }

        /// <summary>设置节点在主轴与交叉轴上的固定尺寸。</summary>
        public ProtoNode Size(float width, float height)
        {
            WidthValue = width;
            HeightValue = height;
            return this;
        }

        /// <summary>设置节点固定宽度；未设置时节点在父列容器中自动填满宽度。</summary>
        public ProtoNode Width(float width)
        {
            WidthValue = width;
            return this;
        }

        /// <summary>设置节点固定高度；未设置时节点在父行容器中自动填满高度。</summary>
        public ProtoNode Height(float height)
        {
            HeightValue = height;
            return this;
        }

        /// <summary>设置节点在父容器主轴上瓜分剩余空间的权重。</summary>
        public ProtoNode Flex(float weight = 1f)
        {
            FlexValue = weight;
            return this;
        }

        /// <summary>设置节点底图颜色；sliced 为 false 时使用整图而非九宫格圆角图。</summary>
        public ProtoNode Bg(Color color, bool sliced = true)
        {
            BackgroundValue = color;
            BackgroundSliced = sliced;
            return this;
        }

        /// <summary>设置文本内容。</summary>
        public ProtoNode Text(string text)
        {
            TextValue = text ?? string.Empty;
            return this;
        }

        /// <summary>设置字号。</summary>
        public ProtoNode FontSize(float size)
        {
            FontSizeValue = size;
            return this;
        }

        /// <summary>设置文本颜色。</summary>
        public ProtoNode TextColor(Color color)
        {
            TextColorValue = color;
            return this;
        }

        /// <summary>
        /// 设置内容水平对齐方式。文本节点上控制文字对齐，容器节点上控制子节点的排布对齐。
        /// 未调用时容器保持左对齐。
        /// </summary>
        public ProtoNode Align(ProtoAlign align)
        {
            TextAlignValue = align;
            AlignExplicit = true;
            return this;
        }

        /// <summary>
        /// 将按钮内部的文本以 <节点名>Text 登记进 UIComponentBinder，用于标题会在运行时变化的按钮。
        /// 按钮文本固定时不应调用，避免往绑定表里塞入不会被读写的引用。
        /// </summary>
        public ProtoNode BindText()
        {
            if (Role != ProtoRole.Button)
                throw new InvalidOperationException("ProtoNode '" + Name + "' is a " + Role + "; BindText() only applies to Button nodes.");

            BindTextValue = true;
            return this;
        }

        /// <summary>
        /// 将容器底图以 <节点名>Bg 登记进 UIComponentBinder，用于底色会在运行时变化的区域，例如按品质改色的详情头部。
        /// 节点未设置 Bg 时由构建期报错，因此本方法与链式调用顺序无关。
        /// </summary>
        public ProtoNode BindBackground()
        {
            BindBackgroundValue = true;
            return this;
        }

        /// <summary>
        /// 把容器变成可点击区域：挂载不参与布局的透明 Graphic 与 Button，并自动登记进 UIComponentBinder。
        /// 用于整块可点击的卡片、列表行、菜单格子等无法用叶子 Button 表达的结构。
        /// </summary>
        public ProtoNode Clickable()
        {
            if (Role != ProtoRole.Panel && Role != ProtoRole.Item && Role != ProtoRole.Box)
                throw new InvalidOperationException("ProtoNode '" + Name + "' is a " + Role + " and cannot be made clickable; only containers support Clickable().");

            ClickableValue = true;
            return Bind();
        }

        /// <summary>
        /// 允许该文本在超出容器时以省略号截断。
        /// 默认情况下文本放不下会导致构建失败，只有显式声明可截断的文本才会被跳过检查，
        /// 避免长文案在界面上被悄悄切掉而没有任何提示。
        /// </summary>
        public ProtoNode Truncate()
        {
            TruncateValue = true;
            return this;
        }

        /// <summary>
        /// 设置内容垂直对齐方式。文本节点上控制文字对齐，容器节点上控制子节点的排布对齐。
        /// 多行正文应显式设为 Top，否则会在剩余空间里居中；未调用时容器保持顶对齐。
        /// </summary>
        public ProtoNode VAlign(ProtoVAlign align)
        {
            TextVAlignValue = align;
            VAlignExplicit = true;
            return this;
        }

        /// <summary>设置进度条填充比例。</summary>
        public ProtoNode Fill(float ratio)
        {
            FillValue = Mathf.Clamp01(ratio);
            return this;
        }

        /// <summary>把节点登记进 UIComponentBinder，可指定绑定的组件类型；不指定时按角色取默认组件。</summary>
        public ProtoNode Bind(Type componentType = null)
        {
            BoundValue = true;
            BindTypeValue = componentType;
            return this;
        }

        /// <summary>
        /// 追加一段直接操作 GameObject 的实例化逻辑，在该节点及其子节点全部创建完成后执行。
        /// 这是提供给扩展程序集封装第三方组件的出口，界面定义本身不应直接调用。
        /// </summary>
        public ProtoNode With(Action<GameObject> build)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            CustomizeAction = CustomizeAction == null ? build : CustomizeAction + build;
            return this;
        }

        /// <summary>追加子节点；叶子角色不允许添加子节点。</summary>
        public ProtoNode Add(params ProtoNode[] nodes)
        {
            if (nodes == null) return this;
            if (Layout == ProtoLayout.None) throw new InvalidOperationException($"ProtoNode '{Name}' is a leaf ({Role}) and cannot contain children.");
            foreach (ProtoNode node in nodes)
            {
                if (node == null) throw new ArgumentNullException(nameof(nodes), $"ProtoNode '{Name}' received a null child.");
                children.Add(node);
            }

            return this;
        }
    }
}
