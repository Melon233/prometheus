using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Xuan.Prometheus.Editor.Prototype
{
    /// <summary>
    /// 把原型定义树实例化成 UI Prefab 的构建器。
    /// 构建过程是幂等的：目标 Prefab 每次都被整体覆盖，因此 Prefab 上的任何手工修改都会丢失。
    /// </summary>
    public static class UIPrototypeBuilder
    {
        private const int MaxDepth = 8;

        /// <summary>
        /// 构建器自己会创建的实现节点名称。
        /// 作者节点不得使用这些名称：它们可能与实现节点成为同级兄弟，
        /// 而 Transform.Find 在同级重名时只返回第一个，会让绑定静默地指向错误对象。
        /// </summary>
        private static readonly HashSet<string> ReservedNodeNames = new HashSet<string>(StringComparer.Ordinal) { "Bg", "Text", "Fill", "Viewport", "Content" };

        /// <summary>重建工程内全部原型定义，按 Order 从小到大执行，并返回汇总报告。</summary>
        public static string RebuildAll()
        {
            List<Type> types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<UIPrototypeDefinition>())
            {
                if (type.IsAbstract) continue;
                if (type.GetCustomAttributes(typeof(UIPrototypeAttribute), false).Length == 0) continue;
                types.Add(type);
            }

            types.Sort((left, right) => GetAttribute(left).Order.CompareTo(GetAttribute(right).Order));
            StringBuilder report = new StringBuilder();
            foreach (Type type in types)
                report.AppendLine(Rebuild(type)).AppendLine();

            AssetDatabase.Refresh();
            return report.ToString();
        }

        /// <summary>重建单个原型定义并返回它的结构回读报告。</summary>
        public static string Rebuild(Type definitionType)
        {
            if (definitionType == null) throw new ArgumentNullException(nameof(definitionType));
            UIPrototypeAttribute attribute = GetAttribute(definitionType);
            UIPrototypeDefinition definition = (UIPrototypeDefinition)Activator.CreateInstance(definitionType);
            ProtoNode root = definition.Build();
            if (root == null) throw new InvalidOperationException(definitionType.Name + ".Build() returned null.");
            if (root.Role != ProtoRole.Panel && root.Role != ProtoRole.Item)
                throw new InvalidOperationException(definitionType.Name + " root must be created by Panel(), ItemRow() or ItemColumn().");

            ValidateTree(root, definitionType.Name);
            return Realize(root, attribute, definitionType.Name);
        }

        /// <summary>读取原型定义类型上的特性，缺失时给出明确错误。</summary>
        private static UIPrototypeAttribute GetAttribute(Type definitionType)
        {
            object[] attributes = definitionType.GetCustomAttributes(typeof(UIPrototypeAttribute), false);
            if (attributes.Length == 0) throw new InvalidOperationException(definitionType.Name + " requires [UIPrototype].");
            return (UIPrototypeAttribute)attributes[0];
        }

        /// <summary>在实例化之前检查结构树本身的静态约束。</summary>
        private static void ValidateTree(ProtoNode root, string definitionName)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            Walk(root, 0, (node, depth) =>
            {
                if (depth > MaxDepth)
                    throw new InvalidOperationException(definitionName + ": hierarchy is deeper than " + MaxDepth + " levels. Flatten the layout before continuing.");

                if (ReservedNodeNames.Contains(node.Name))
                    throw new InvalidOperationException(definitionName + ": node name '" + node.Name + "' is reserved for nodes the builder creates itself. Choose another name.");

                if (!names.Add(node.Name))
                    throw new InvalidOperationException(definitionName + ": duplicate node name '" + node.Name + "'. Node names must be unique because they become binding names.");

                if (node.Layout == ProtoLayout.Grid && node.GridColumnsValue < 1)
                    throw new InvalidOperationException(definitionName + ": grid '" + node.Name + "' requires at least one column.");

                if (node.Layout != ProtoLayout.None && node.Children.Count == 0)
                    throw new InvalidOperationException(definitionName + ": container '" + node.Name + "' has no children. Remove it or give it content.");

                if (node.Role == ProtoRole.Custom && node.CustomizeAction == null)
                    throw new InvalidOperationException(definitionName + ": custom node '" + node.Name + "' has no build action.");
            });
        }

        /// <summary>先序遍历结构树。</summary>
        private static void Walk(ProtoNode node, int depth, Action<ProtoNode, int> visit)
        {
            visit(node, depth);
            foreach (ProtoNode child in node.Children)
                Walk(child, depth + 1, visit);
        }

        /// <summary>在临时预览场景中实例化结构树，完成布局计算、校验、绑定登记与 Prefab 落盘。</summary>
        private static string Realize(ProtoNode root, UIPrototypeAttribute attribute, string definitionName)
        {
            Scene previewScene = EditorSceneManager.NewPreviewScene();
            GameObject canvasRoot = null;
            GameObject panelRoot = null;
            try
            {
                canvasRoot = new GameObject("[UIProtoBuildCanvas]", typeof(RectTransform), typeof(Canvas));
                EditorSceneManager.MoveGameObjectToScene(canvasRoot, previewScene);
                Canvas canvas = canvasRoot.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                RectTransform canvasRect = canvasRoot.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(attribute.DesignWidth, attribute.DesignHeight);

                List<BindingRecord> bindings = new List<BindingRecord>();
                Dictionary<ProtoNode, GameObject> realized = new Dictionary<ProtoNode, GameObject>();
                panelRoot = CreateNode(root, canvasRect, ProtoLayout.None, bindings, realized);
                RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
                if (root.Role == ProtoRole.Panel) StretchToParent(panelRect);
                LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
                RebuildLayoutRoots(panelRoot);
                ValidateRealized(root, panelRoot, realized, definitionName);

                string directory = Path.GetDirectoryName(attribute.PrefabPath).Replace('\\', '/');
                if (!AssetDatabase.IsValidFolder(directory))
                {
                    Directory.CreateDirectory(directory);
                    AssetDatabase.Refresh();
                }

                Dictionary<string, string> bindingPaths = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int index = 0; index < bindings.Count; index++)
                    bindingPaths[bindings[index].Name] = GetPath(bindings[index].Target.transform, panelRoot.transform);

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(panelRoot, attribute.PrefabPath);
                if (saved == null) throw new InvalidOperationException(definitionName + ": failed to save prefab '" + attribute.PrefabPath + "'.");

                string report = BuildReport(definitionName, attribute, panelRoot, bindings);
                WriteBindings(saved, bindings, bindingPaths);
                if (attribute.GeneratePanelCode)
                {
                    UIComponentBinder binder = saved.GetComponent<UIComponentBinder>();
                    if (binder == null) throw new InvalidOperationException(definitionName + ": GeneratePanelCode requires a Panel() root.");
                    UIPanelCodeGenerator.Generate(binder);
                }

                return report;
            }
            finally
            {
                if (panelRoot != null) UnityEngine.Object.DestroyImmediate(panelRoot);
                if (canvasRoot != null) UnityEngine.Object.DestroyImmediate(canvasRoot);
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        /// <summary>
        /// 逐个重建布局根，补齐主布局重建触及不到的部分。
        /// uGUI 的 ForceRebuildLayoutImmediate 只在节点自身带 ILayoutController 时才向下递归，
        /// 而滚动视图的 Viewport 只挂 RectMask2D，递归就断在那里，
        /// 导致 Content 的 ContentSizeFitter 不生效、放在 Content 里的示例条目尺寸为零。
        /// 这里不去猜链路会在哪里断，而是自外向内把每一个带布局组或 ContentSizeFitter 的节点都重建一遍，
        /// 因此新增任何插入实现层的封装节点都不需要再改动本方法。
        /// 重建是幂等的，重复覆盖已在主链上算过的节点只有构建期的开销，没有正确性代价。
        /// </summary>
        private static void RebuildLayoutRoots(GameObject panelRoot)
        {
            List<RectTransform> roots = new List<RectTransform>();
            CollectLayoutRoots(panelRoot.transform, roots);
            for (int index = 0; index < roots.Count; index++)
                LayoutRebuilder.ForceRebuildLayoutImmediate(roots[index]);
        }

        /// <summary>按先序收集所有自身控制子节点或自身尺寸由内容决定的节点，保证外层排在内层之前。</summary>
        private static void CollectLayoutRoots(Transform transform, List<RectTransform> roots)
        {
            RectTransform rect = transform as RectTransform;
            if (rect != null && (transform.GetComponent<LayoutGroup>() != null || transform.GetComponent<ContentSizeFitter>() != null))
                roots.Add(rect);

            for (int index = 0; index < transform.childCount; index++)
                CollectLayoutRoots(transform.GetChild(index), roots);
        }

        /// <summary>记录一条待写入 UIComponentBinder 的绑定，路径用于在保存后的 Prefab 上重新定位组件。</summary>
        private readonly struct BindingRecord
        {
            /// <summary>创建一条绑定记录。</summary>
            public BindingRecord(string name, GameObject target, Type componentType)
            {
                Name = name;
                Target = target;
                ComponentType = componentType;
            }

            /// <summary>获取绑定名称。</summary>
            public string Name { get; }

            /// <summary>获取实例化后的目标对象，用于在整棵树建好之后推导真实层级路径。</summary>
            public GameObject Target { get; }

            /// <summary>获取绑定的组件类型。</summary>
            public Type ComponentType { get; }
        }

        /// <summary>递归实例化一个结构节点及其全部子节点。</summary>
        private static GameObject CreateNode(ProtoNode node, RectTransform parent, ProtoLayout parentLayout, List<BindingRecord> bindings, Dictionary<ProtoNode, GameObject> realized)
        {
            GameObject go = new GameObject(node.Name, typeof(RectTransform));
            int uiLayer = LayerMask.NameToLayer("UI");
            go.layer = uiLayer >= 0 ? uiLayer : 0;
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            realized.Add(node, go);
            RectTransform childParent = ApplyRole(node, go);
            ApplyLayoutGroup(node, go);
            if (parentLayout == ProtoLayout.Row || parentLayout == ProtoLayout.Column)
                ApplyLayoutElement(node, go, parentLayout);
            else if (parentLayout == ProtoLayout.None)
                rect.sizeDelta = new Vector2(node.WidthValue ?? rect.sizeDelta.x, node.HeightValue ?? rect.sizeDelta.y);

            if (node.BoundValue)
                bindings.Add(new BindingRecord(node.Name, go, ResolveBindType(node)));

            if (node.BindTextValue)
            {
                Transform buttonText = go.transform.Find("Text");
                if (buttonText == null) throw new InvalidOperationException("Button '" + node.Name + "' has no inner text to bind.");
                bindings.Add(new BindingRecord(node.Name + "Text", buttonText.gameObject, typeof(TextMeshProUGUI)));
            }

            if (node.BindBackgroundValue)
            {
                Transform background = go.transform.Find("Bg");
                if (background == null) throw new InvalidOperationException("Node '" + node.Name + "' has no background to bind.");
                bindings.Add(new BindingRecord(node.Name + "Bg", background.gameObject, typeof(Image)));
            }

            foreach (ProtoNode child in node.Children)
                CreateNode(child, childParent, node.Layout, bindings, realized);

            if (node.CustomizeAction != null) node.CustomizeAction(go);
            return go;
        }

        /// <summary>按节点角色挂载对应的表现组件，并返回子节点应当挂载到的父节点。</summary>
        private static RectTransform ApplyRole(ProtoNode node, GameObject go)
        {
            switch (node.Role)
            {
                case ProtoRole.Panel:
                    go.AddComponent<UIComponentBinder>();
                    go.AddComponent<RaycastBlocker>().raycastTarget = true;
                    if (node.BackgroundValue.HasValue) CreateBackground(node, go);
                    if (node.ClickableValue) MakeClickable(go);
                    break;
                case ProtoRole.Item:
                case ProtoRole.Box:
                case ProtoRole.Custom:
                    if (node.BackgroundValue.HasValue) CreateBackground(node, go);
                    if (node.ClickableValue) MakeClickable(go);
                    break;
                case ProtoRole.Label:
                    CreateText(go, node.TextValue, node.FontSizeValue, node.TextColorValue ?? UIProtoTheme.TextPrimary, node.TextAlignValue, node.TextVAlignValue, node.TruncateValue);
                    break;
                case ProtoRole.Icon:
                    Image icon = go.AddComponent<Image>();
                    icon.sprite = node.BackgroundSliced ? UIProtoTheme.SlicedSprite : null;
                    icon.type = node.BackgroundSliced ? Image.Type.Sliced : Image.Type.Simple;
                    icon.color = node.BackgroundValue ?? UIProtoTheme.Accent;
                    icon.raycastTarget = false;
                    break;
                case ProtoRole.Button:
                    CreateButton(node, go);
                    break;
                case ProtoRole.Bar:
                    CreateBar(node, go);
                    break;
                case ProtoRole.ScrollBox:
                    return CreateScrollBox(node, go);
            }

            return go.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 创建纵向滚动容器：容器自身承载 ScrollRect，内容区由 VerticalLayoutGroup 排布并用 ContentSizeFitter 按内容撑高。
        /// 返回内容区，使子节点挂到内容区而不是容器本身。
        /// </summary>
        private static RectTransform CreateScrollBox(ProtoNode node, GameObject go)
        {
            if (node.BackgroundValue.HasValue) CreateBackground(node, go);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.layer = go.layer;
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.SetParent(go.transform, false);
            StretchAsImplementationChild(viewport);

            GameObject contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.layer = go.layer;
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup group = contentObject.AddComponent<VerticalLayoutGroup>();
            group.padding = node.PaddingValue;
            group.spacing = node.SpacingValue;
            group.childAlignment = ResolveChildAlignment(node);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = go.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 40f;
            return content;
        }

        /// <summary>
        /// 让容器整体可点击：挂一个无图、全透明但接收射线的 Image 作为 Button 的目标图形。
        /// 不设置 sprite 是关键——Image 也实现 ILayoutElement，带图时会向父容器上报一个来自贴图尺寸的期望大小，
        /// 干扰按内容计算的布局；sprite 为空时它上报的期望尺寸为零。
        /// </summary>
        private static void MakeClickable(GameObject go)
        {
            Image hit = go.AddComponent<Image>();
            hit.sprite = null;
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            go.AddComponent<Button>().targetGraphic = hit;
        }

        /// <summary>为容器节点创建铺满自身的底图子节点，避免底图 Image 干扰容器自身的布局尺寸计算。</summary>
        private static void CreateBackground(ProtoNode node, GameObject go)
        {
            GameObject background = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            background.layer = go.layer;
            RectTransform rect = background.GetComponent<RectTransform>();
            rect.SetParent(go.transform, false);
            StretchAsImplementationChild(rect);
            Image image = background.GetComponent<Image>();
            image.sprite = node.BackgroundSliced ? UIProtoTheme.SlicedSprite : null;
            image.type = node.BackgroundSliced ? Image.Type.Sliced : Image.Type.Simple;
            image.color = node.BackgroundValue.Value;
            image.raycastTarget = false;
        }

        /// <summary>在指定节点上创建文本组件。</summary>
        private static void CreateText(GameObject go, string text, float fontSize, Color color, ProtoAlign align, ProtoVAlign vAlign, bool truncate)
        {
            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UIProtoTheme.ResolveFont();
            if (font != null) label.font = font;
            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = truncate ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            label.alignment = ResolveAlignment(align, vAlign);
        }

        /// <summary>把水平与垂直对齐组合翻译成 TMP 的对齐枚举。</summary>
        private static TextAlignmentOptions ResolveAlignment(ProtoAlign align, ProtoVAlign vAlign)
        {
            if (vAlign == ProtoVAlign.Top)
                return align == ProtoAlign.Center ? TextAlignmentOptions.Top : align == ProtoAlign.Right ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;

            if (vAlign == ProtoVAlign.Bottom)
                return align == ProtoAlign.Center ? TextAlignmentOptions.Bottom : align == ProtoAlign.Right ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft;

            return align == ProtoAlign.Center ? TextAlignmentOptions.Center : align == ProtoAlign.Right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
        }

        /// <summary>创建带底图、Button 与内部文本的按钮节点。</summary>
        private static void CreateButton(ProtoNode node, GameObject go)
        {
            Image image = go.AddComponent<Image>();
            image.sprite = UIProtoTheme.SlicedSprite;
            image.type = Image.Type.Sliced;
            image.color = node.BackgroundValue ?? UIProtoTheme.ButtonSecondary;
            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.layer = go.layer;
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(go.transform, false);
            StretchAsImplementationChild(rect);
            CreateText(textObject, node.TextValue, node.FontSizeValue, node.TextColorValue ?? UIProtoTheme.TextPrimary, ProtoAlign.Center, ProtoVAlign.Middle, node.TruncateValue);
        }

        /// <summary>创建由底图与填充图组成的进度条节点。</summary>
        private static void CreateBar(ProtoNode node, GameObject go)
        {
            Image background = go.AddComponent<Image>();
            background.sprite = UIProtoTheme.SlicedSprite;
            background.type = Image.Type.Sliced;
            background.color = node.BackgroundValue ?? UIProtoTheme.Divider;
            background.raycastTarget = false;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.layer = go.layer;
            RectTransform rect = fillObject.GetComponent<RectTransform>();
            rect.SetParent(go.transform, false);
            StretchAsImplementationChild(rect);
            Image fill = fillObject.GetComponent<Image>();
            fill.sprite = UIProtoTheme.SlicedSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = node.FillValue;
            fill.color = UIProtoTheme.Accent;
            fill.raycastTarget = false;
        }

        /// <summary>按节点声明的排布方式挂载布局组件，统一使用受控但不强制拉伸的规则。</summary>
        private static void ApplyLayoutGroup(ProtoNode node, GameObject go)
        {
            if (node.Layout == ProtoLayout.Grid)
            {
                GridLayoutGroup grid = go.AddComponent<GridLayoutGroup>();
                grid.padding = node.PaddingValue;
                grid.spacing = new Vector2(node.SpacingValue, node.SpacingValue);
                grid.cellSize = node.GridCellValue;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = node.GridColumnsValue;
                grid.childAlignment = ResolveChildAlignment(node);
                return;
            }

            if (node.Layout == ProtoLayout.None || node.Role == ProtoRole.ScrollBox) return;

            HorizontalOrVerticalLayoutGroup group = node.Layout == ProtoLayout.Row
                ? (HorizontalOrVerticalLayoutGroup)go.AddComponent<HorizontalLayoutGroup>()
                : go.AddComponent<VerticalLayoutGroup>();
            group.padding = node.PaddingValue;
            group.spacing = node.SpacingValue;
            group.childAlignment = ResolveChildAlignment(node);
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childScaleWidth = false;
            group.childScaleHeight = false;
        }

        /// <summary>
        /// 解析容器的子节点排布对齐。只有显式调用过 Align/VAlign 的方向才会偏离默认的左上角，
        /// 使既有界面不因为这项能力的加入而改变布局。
        /// </summary>
        private static TextAnchor ResolveChildAlignment(ProtoNode node)
        {
            ProtoAlign horizontal = node.AlignExplicit ? node.TextAlignValue : ProtoAlign.Left;
            ProtoVAlign vertical = node.VAlignExplicit ? node.TextVAlignValue : ProtoVAlign.Top;
            if (vertical == ProtoVAlign.Top)
                return horizontal == ProtoAlign.Center ? TextAnchor.UpperCenter : horizontal == ProtoAlign.Right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

            if (vertical == ProtoVAlign.Bottom)
                return horizontal == ProtoAlign.Center ? TextAnchor.LowerCenter : horizontal == ProtoAlign.Right ? TextAnchor.LowerRight : TextAnchor.LowerLeft;

            return horizontal == ProtoAlign.Center ? TextAnchor.MiddleCenter : horizontal == ProtoAlign.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
        }

        /// <summary>
        /// 把节点尺寸声明翻译成 LayoutElement。
        /// 主轴由父容器方向决定：显式尺寸写入 preferred，Flex 写入 flexible；交叉轴在未显式指定尺寸时自动填满父容器。
        /// </summary>
        private static void ApplyLayoutElement(ProtoNode node, GameObject go, ProtoLayout parentLayout)
        {
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minWidth = node.WidthValue ?? -1f;
            element.preferredWidth = node.WidthValue ?? -1f;
            element.minHeight = node.HeightValue ?? -1f;
            element.preferredHeight = node.HeightValue ?? -1f;
            if (parentLayout == ProtoLayout.Row)
            {
                element.flexibleWidth = node.FlexValue;
                element.flexibleHeight = node.HeightValue.HasValue ? 0f : 1f;
            }
            else
            {
                element.flexibleHeight = node.FlexValue;
                element.flexibleWidth = node.WidthValue.HasValue ? 0f : 1f;
            }
        }

        /// <summary>把 RectTransform 铺满父节点，仅用于底图和内部文本等非作者声明的实现节点。</summary>
        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>
        /// 把底图、内部文本这类实现节点铺满父节点并排除在父级布局之外。
        /// 缺少 ignoreLayout 时它们会被父节点的 LayoutGroup 当成普通子项排布，既占用主轴空间又拿不到正确尺寸。
        /// </summary>
        private static void StretchAsImplementationChild(RectTransform rect)
        {
            StretchToParent(rect);
            rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        /// <summary>推断绑定使用的组件类型。</summary>
        private static Type ResolveBindType(ProtoNode node)
        {
            if (node.BindTypeValue != null) return node.BindTypeValue;
            if (node.ClickableValue) return typeof(Button);
            switch (node.Role)
            {
                case ProtoRole.Button: return typeof(Button);
                case ProtoRole.Label: return typeof(TextMeshProUGUI);
                case ProtoRole.Icon:
                case ProtoRole.Bar: return typeof(Image);
                default: return typeof(RectTransform);
            }
        }

        /// <summary>
        /// 在布局计算完成后检查实例化结果，拦截零尺寸节点和残留的 Inspector 点击监听。
        /// 节点一律从实例化时记录的对象表取得，不按名称在层级里反查，
        /// 避免实现节点与作者节点重名时匹配到错误对象。
        /// </summary>
        private static void ValidateRealized(ProtoNode root, GameObject panelRoot, Dictionary<ProtoNode, GameObject> realized, string definitionName)
        {
            Walk(root, 0, (node, depth) =>
            {
                if (node.Role == ProtoRole.Spacer) return;
                GameObject instance;
                if (!realized.TryGetValue(node, out instance) || instance == null)
                    throw new InvalidOperationException(definitionName + ": node '" + node.Name + "' was not instantiated.");

                Transform found = instance.transform;
                RectTransform rect = found as RectTransform;
                if (rect != null && (rect.rect.width <= 0.5f || rect.rect.height <= 0.5f))
                    throw new InvalidOperationException(definitionName + ": node '" + node.Name + "' resolved to a zero size (" + rect.rect.width.ToString("0.#") + "x" + rect.rect.height.ToString("0.#") + "). Give it Size/Height/Flex, or check its parent layout.");

                Button button = found.GetComponent<Button>();
                if (button != null && button.onClick.GetPersistentEventCount() > 0)
                    throw new InvalidOperationException(definitionName + ": button '" + node.Name + "' has persistent onClick entries; UIKit requires that list to stay empty.");
            });

            // 边界检查只对面板有意义：条目 Prefab 的根就是卡片本身，底图铺满整张卡片是正确的。
            ValidateRealizedSizes(panelRoot.transform, panelRoot.transform, definitionName, root.Role == ProtoRole.Panel);
        }

        /// <summary>
        /// 检查实例化结果中每一个 RectTransform 的尺寸，覆盖底图和内部文本这类不出现在结构树里的实现节点。
        /// 滚动视图的 Content 由列表组件在运行时撑开，因此不参与检查。
        /// </summary>
        private static void ValidateRealizedSizes(Transform transform, Transform panelRoot, string definitionName, bool checkEdges)
        {
            RectTransform rect = transform as RectTransform;
            if (rect != null && !IsRuntimeSized(rect) && (rect.rect.width <= 0.5f || rect.rect.height <= 0.5f))
                throw new InvalidOperationException(definitionName + ": '" + GetPath(rect.transform, panelRoot) + "' resolved to a zero size (" + rect.rect.width.ToString("0.#") + "x" + rect.rect.height.ToString("0.#") + ").");

            ValidateTextFits(transform, panelRoot, definitionName);
            if (checkEdges) ValidateEdgeBackground(transform, panelRoot, definitionName);

            for (int index = 0; index < transform.childCount; index++)
                ValidateRealizedSizes(transform.GetChild(index), panelRoot, definitionName, checkEdges);
        }

        /// <summary>
        /// 检查文本在分配到的高度内能否完整显示。
        /// 声明过 Truncate 的文本按省略号处理并跳过检查，其余文本放不下一律构建失败，避免文案被静默切掉。
        /// </summary>
        private static void ValidateTextFits(Transform transform, Transform panelRoot, string definitionName)
        {
            TextMeshProUGUI label = transform.GetComponent<TextMeshProUGUI>();
            if (label == null || label.overflowMode == TextOverflowModes.Ellipsis) return;
            RectTransform rect = label.rectTransform;
            if (rect.rect.width <= 0.5f) return;

            float required = label.GetPreferredValues(label.text, rect.rect.width, 0f).y;
            if (required <= rect.rect.height + 1f) return;

            throw new InvalidOperationException(definitionName + ": text '" + GetPath(transform, panelRoot) + "' needs " + required.ToString("0") + "px but only has " + rect.rect.height.ToString("0") + "px. Give it more height, put it inside a ScrollBox, or mark it Truncate() if clipping is intended.");
        }

        /// <summary>
        /// 拦截贴到面板边界却使用九宫格底图的节点。
        /// 占位用的内置 UISprite 带圆角和一圈透明边，作为卡片底图没问题，
        /// 但铺到屏幕边界时圆角与透明边会在最外圈露出缝隙，且不会报错。
        /// </summary>
        private static void ValidateEdgeBackground(Transform transform, Transform panelRoot, string definitionName)
        {
            Image image = transform.GetComponent<Image>();
            if (image == null || image.type != Image.Type.Sliced) return;
            RectTransform rect = transform as RectTransform;
            RectTransform panelRect = panelRoot as RectTransform;
            if (rect == null || panelRect == null) return;
            // 遮罩内的内容会随滚动超出面板范围，它们并不真的贴在屏幕边界上。
            if (IsClipped(transform)) return;
            if (!SitsFlushWithPanelEdge(rect, panelRect)) return;

            throw new InvalidOperationException(definitionName + ": '" + GetPath(transform, panelRoot) + "' uses a sliced background but reaches the panel edge. The placeholder sprite has rounded corners and a transparent border, which leaves a visible gap at the screen edge. Use Bg(color, sliced: false) for surfaces that run to the edge.");
        }

        /// <summary>判断节点是否处在某个遮罩之内。</summary>
        private static bool IsClipped(Transform transform)
        {
            Transform current = transform.parent;
            while (current != null)
            {
                if (current.GetComponent<RectMask2D>() != null) return true;
                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// 判断节点的矩形是否与面板根的某条边界齐平。
        /// 只匹配“恰好对齐”，不包括超出面板范围的情况，后者是被裁剪的内容而不是铺到边界的背板。
        /// </summary>
        private static bool SitsFlushWithPanelEdge(RectTransform rect, RectTransform panelRect)
        {
            const float Tolerance = 0.5f;
            Vector3[] corners = new Vector3[4];
            Vector3[] panelCorners = new Vector3[4];
            rect.GetWorldCorners(corners);
            panelRect.GetWorldCorners(panelCorners);
            return Mathf.Abs(corners[0].x - panelCorners[0].x) <= Tolerance
                || Mathf.Abs(corners[0].y - panelCorners[0].y) <= Tolerance
                || Mathf.Abs(corners[2].x - panelCorners[2].x) <= Tolerance
                || Mathf.Abs(corners[2].y - panelCorners[2].y) <= Tolerance;
        }

        /// <summary>判断一个节点的尺寸是否由运行时组件负责计算。</summary>
        private static bool IsRuntimeSized(RectTransform rect)
        {
            Transform parent = rect.parent;
            while (parent != null)
            {
                ScrollRect scroll = parent.GetComponent<ScrollRect>();
                if (scroll != null && scroll.content == rect) return true;
                parent = parent.parent;
            }

            return false;
        }

        /// <summary>返回节点相对面板根的层级路径，用于校验报错定位。</summary>
        private static string GetPath(Transform transform, Transform panelRoot)
        {
            if (transform == panelRoot) return string.Empty;
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null && current != panelRoot)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>把收集到的绑定按声明顺序写进保存后 Prefab 根节点的 UIComponentBinder。</summary>
        private static void WriteBindings(GameObject savedPrefab, List<BindingRecord> bindings, Dictionary<string, string> bindingPaths)
        {
            UIComponentBinder binder = savedPrefab.GetComponent<UIComponentBinder>();
            if (binder == null) return;
            SerializedObject serializedBinder = new SerializedObject(binder);
            SerializedProperty bindingsProperty = serializedBinder.FindProperty("bindings");
            bindingsProperty.arraySize = bindings.Count;
            for (int index = 0; index < bindings.Count; index++)
            {
                BindingRecord record = bindings[index];
                string path = bindingPaths[record.Name];
                Transform target = string.IsNullOrEmpty(path) ? savedPrefab.transform : savedPrefab.transform.Find(path);
                if (target == null) throw new InvalidOperationException("Binding '" + record.Name + "' could not be resolved at path '" + path + "'.");
                UnityEngine.Component component = target.GetComponent(record.ComponentType);
                if (component == null) throw new InvalidOperationException("Binding '" + record.Name + "' has no component of type '" + record.ComponentType.Name + "'.");
                SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(index);
                bindingProperty.FindPropertyRelative("name").stringValue = record.Name;
                bindingProperty.FindPropertyRelative("component").objectReferenceValue = component;
            }

            serializedBinder.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(savedPrefab);
        }

        /// <summary>生成带实际尺寸的结构回读报告，供人和 AI 在不截图的情况下检查布局结果。</summary>
        private static string BuildReport(string definitionName, UIPrototypeAttribute attribute, GameObject panelRoot, List<BindingRecord> bindings)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("[" + definitionName + "] -> " + attribute.PrefabPath + "  (design " + attribute.DesignWidth.ToString("0") + "x" + attribute.DesignHeight.ToString("0") + ")");
            AppendNode(builder, panelRoot.transform, 0);
            builder.AppendLine("bindings (" + bindings.Count + "):");
            for (int index = 0; index < bindings.Count; index++)
                builder.AppendLine("  [" + index + "] " + bindings[index].Name + " : " + bindings[index].ComponentType.Name);

            return builder.ToString();
        }

        /// <summary>递归输出一个实例化节点的尺寸和关键组件。</summary>
        private static void AppendNode(StringBuilder builder, Transform transform, int depth)
        {
            RectTransform rect = transform as RectTransform;
            string indent = new string(' ', depth * 2);
            string size = rect != null ? rect.rect.width.ToString("0") + "x" + rect.rect.height.ToString("0") : "-";
            List<string> parts = new List<string>();
            if (transform.GetComponent<VerticalLayoutGroup>() != null) parts.Add("Column");
            if (transform.GetComponent<HorizontalLayoutGroup>() != null) parts.Add("Row");
            if (transform.GetComponent<GridLayoutGroup>() != null) parts.Add("Grid");
            if (transform.GetComponent<ScrollRect>() != null) parts.Add("ScrollRect");
            if (transform.GetComponent<Button>() != null) parts.Add("Button");
            if (transform.GetComponent<TextMeshProUGUI>() != null) parts.Add("Text");
            if (transform.GetComponent<Image>() != null) parts.Add("Image");
            builder.AppendLine(indent + transform.name.PadRight(Math.Max(1, 26 - indent.Length)) + " [" + size.PadLeft(10) + "]  " + string.Join(" ", parts.ToArray()));
            for (int index = 0; index < transform.childCount; index++)
                AppendNode(builder, transform.GetChild(index), depth + 1);
        }
    }
}
