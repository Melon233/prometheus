using System;
using SuperScrollView;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Editor.Prototype;

namespace Xuan.Prometheus.UI.Prototype
{
    /// <summary>
    /// 把 SuperScrollView 的无限列表与无限网格封装成原型节点，使界面定义不需要接触 ScrollRect、Viewport 和 Content 这类实现细节。
    /// 这是原型框架推荐的第三方组件接入方式：组件的装配逻辑集中在这里，界面定义里只留一个语义节点。
    /// </summary>
    public static class ScrollProto
    {
        /// <summary>
        /// 创建一个纵向无限滚动列表节点，内部装配 ScrollRect、Viewport、Content 与 LoopListView2。
        /// </summary>
        /// <param name="name">节点名称，同时作为绑定名称。</param>
        /// <param name="itemPrefabPath">列表项 Prefab 路径，必须由 Order 更小的原型定义先行生成。</param>
        /// <param name="itemPadding">相邻列表项之间的额外间距。</param>
        /// <param name="initCreateCount">初始预创建的列表项数量。</param>
        /// <param name="examples">放入内容区的示例条目数量，仅用于在编辑器中查看效果，接入数据前需要手工删除。</param>
        /// <param name="decorateExample">对每个示例条目做差异化处理的回调，参数为条目实例与它的序号。</param>
        public static ProtoNode VList(string name, string itemPrefabPath, float itemPadding = 8f, int initCreateCount = 8, int examples = 0, Action<GameObject, int> decorateExample = null)
        {
            return CreateList(name, itemPrefabPath, itemPadding, initCreateCount, ListItemArrangeType.TopToBottom, examples, decorateExample);
        }

        /// <summary>
        /// 创建一个横向无限滚动列表节点，内部装配 ScrollRect、Viewport、Content 与 LoopListView2。
        /// 适用于数量会持续增长、不能一次性铺开的横向条目列表。
        /// </summary>
        /// <param name="name">节点名称，同时作为绑定名称。</param>
        /// <param name="itemPrefabPath">列表项 Prefab 路径，必须由 Order 更小的原型定义先行生成。</param>
        /// <param name="itemPadding">相邻列表项之间的额外间距。</param>
        /// <param name="initCreateCount">初始预创建的列表项数量。</param>
        /// <param name="examples">放入内容区的示例条目数量，仅用于在编辑器中查看效果，接入数据前需要手工删除。</param>
        /// <param name="decorateExample">对每个示例条目做差异化处理的回调，参数为条目实例与它的序号。</param>
        public static ProtoNode HList(string name, string itemPrefabPath, float itemPadding = 8f, int initCreateCount = 12, int examples = 0, Action<GameObject, int> decorateExample = null)
        {
            return CreateList(name, itemPrefabPath, itemPadding, initCreateCount, ListItemArrangeType.LeftToRight, examples, decorateExample);
        }

        /// <summary>
        /// 创建一个固定列数的无限滚动网格节点，内部装配 ScrollRect、Viewport、Content 与 LoopGridView。
        /// 适用于背包这类条目上千、不能用普通 Grid 一次性铺开的场景。
        /// </summary>
        /// <param name="name">节点名称，同时作为绑定名称。</param>
        /// <param name="itemPrefabPath">条目 Prefab 路径，必须由 Order 更小的原型定义先行生成。</param>
        /// <param name="columns">固定列数；不写入 Prefab 会让 LoopGridView 以 0 列计算行数而抛出除零异常。</param>
        /// <param name="itemSize">单个条目尺寸，必须与条目 Prefab 的实际尺寸一致。</param>
        /// <param name="itemPadding">条目之间的横向与纵向间距。</param>
        /// <param name="initCreateCount">初始预创建的条目数量。</param>
        /// <param name="examples">放入内容区的示例条目数量，仅用于在编辑器中查看效果，接入数据前需要手工删除。</param>
        /// <param name="decorateExample">对每个示例条目做差异化处理的回调，参数为条目实例与它的序号。</param>
        public static ProtoNode GridView(string name, string itemPrefabPath, int columns, Vector2 itemSize, Vector2 itemPadding, int initCreateCount = 16, int examples = 0, Action<GameObject, int> decorateExample = null)
        {
            if (string.IsNullOrWhiteSpace(itemPrefabPath)) throw new ArgumentException("A loop grid requires an item prefab path.", nameof(itemPrefabPath));
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns), "A loop grid requires at least one column.");
            return UIPrototypeDefinition
                .Custom(name, go => BuildGrid(go, name, itemPrefabPath, columns, itemSize, itemPadding, initCreateCount, examples, decorateExample))
                .Bind(typeof(LoopGridView));
        }

        /// <summary>创建一个可被列表使用的条目根节点，横向排布子节点并挂载 LoopListViewItem2。</summary>
        public static ProtoNode ListItemRow(string name)
        {
            return UIPrototypeDefinition.ItemRow(name).With(go => go.AddComponent<LoopListViewItem2>());
        }

        /// <summary>创建一个可被列表使用的条目根节点，纵向排布子节点并挂载 LoopListViewItem2。</summary>
        public static ProtoNode ListItemColumn(string name)
        {
            return UIPrototypeDefinition.ItemColumn(name).With(go => go.AddComponent<LoopListViewItem2>());
        }

        /// <summary>创建一个可被网格使用的条目根节点，纵向排布子节点并挂载 LoopGridViewItem。</summary>
        public static ProtoNode GridItemColumn(string name)
        {
            return UIPrototypeDefinition.ItemColumn(name).With(go => go.AddComponent<LoopGridViewItem>());
        }

        /// <summary>按排布方向创建无限滚动列表节点。</summary>
        private static ProtoNode CreateList(string name, string itemPrefabPath, float itemPadding, int initCreateCount, ListItemArrangeType arrangeType, int examples, Action<GameObject, int> decorateExample)
        {
            if (string.IsNullOrWhiteSpace(itemPrefabPath)) throw new ArgumentException("A loop list requires an item prefab path.", nameof(itemPrefabPath));
            return UIPrototypeDefinition
                .Custom(name, go => BuildList(go, name, itemPrefabPath, itemPadding, initCreateCount, arrangeType, examples, decorateExample))
                .Bind(typeof(LoopListView2));
        }

        /// <summary>在列表节点上装配滚动视图与 LoopListView2，并写入列表项 Prefab 配置。</summary>
        private static void BuildList(GameObject go, string name, string itemPrefabPath, float itemPadding, int initCreateCount, ListItemArrangeType arrangeType, int examples, Action<GameObject, int> decorateExample)
        {
            GameObject itemPrefab = LoadItemPrefab(name, itemPrefabPath);
            bool vertical = arrangeType == ListItemArrangeType.TopToBottom || arrangeType == ListItemArrangeType.BottomToTop;

            RectTransform viewport = CreateStretchedChild(go.transform, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = CreateStretchedChild(viewport, "Content");
            // 纵向列表由顶部向下延伸，横向列表由左侧向右延伸；实际长度由 LoopListView2 在运行时撑开。
            content.anchorMin = vertical ? new Vector2(0f, 1f) : new Vector2(0f, 0f);
            content.anchorMax = vertical ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            content.pivot = vertical ? new Vector2(0.5f, 1f) : new Vector2(0f, 0.5f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            ConfigureScrollRect(go, viewport, content, vertical);

            LoopListView2 list = go.AddComponent<LoopListView2>();
            list.ArrangeType = arrangeType;
            SerializedObject serializedList = new SerializedObject(list);
            SerializedProperty prefabDataList = serializedList.FindProperty("mItemPrefabDataList");
            prefabDataList.arraySize = 1;
            SerializedProperty entry = prefabDataList.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("mItemPrefab").objectReferenceValue = itemPrefab;
            entry.FindPropertyRelative("mPadding").floatValue = itemPadding;
            entry.FindPropertyRelative("mInitCreateCount").intValue = initCreateCount;
            entry.FindPropertyRelative("mStartPosOffset").floatValue = 0f;
            serializedList.FindProperty("mSupportScrollBar").boolValue = false;
            serializedList.ApplyModifiedPropertiesWithoutUndo();

            Vector2 itemSize = GetPrefabSize(itemPrefab);
            for (int index = 0; index < examples; index++)
            {
                RectTransform example = CreateExample(itemPrefab, content, index, decorateExample);
                if (vertical)
                {
                    example.anchorMin = new Vector2(0.5f, 1f);
                    example.anchorMax = new Vector2(0.5f, 1f);
                    example.pivot = new Vector2(0.5f, 1f);
                    example.anchoredPosition = new Vector2(0f, -index * (itemSize.y + itemPadding));
                }
                else
                {
                    example.anchorMin = new Vector2(0f, 0.5f);
                    example.anchorMax = new Vector2(0f, 0.5f);
                    example.pivot = new Vector2(0f, 0.5f);
                    example.anchoredPosition = new Vector2(index * (itemSize.x + itemPadding), 0f);
                }
            }
        }

        /// <summary>在网格节点上装配滚动视图与 LoopGridView，并写入条目 Prefab 配置与固定列数。</summary>
        private static void BuildGrid(GameObject go, string name, string itemPrefabPath, int columns, Vector2 itemSize, Vector2 itemPadding, int initCreateCount, int examples, Action<GameObject, int> decorateExample)
        {
            GameObject itemPrefab = LoadItemPrefab(name, itemPrefabPath);

            RectTransform viewport = CreateStretchedChild(go.transform, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = CreateStretchedChild(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            content.anchoredPosition = Vector2.zero;

            ConfigureScrollRect(go, viewport, content, true);

            LoopGridView grid = go.AddComponent<LoopGridView>();
            grid.ArrangeType = GridItemArrangeType.TopLeftToBottomRight;
            SerializedObject serializedGrid = new SerializedObject(grid);
            SerializedProperty prefabDataList = serializedGrid.FindProperty("mItemPrefabDataList");
            prefabDataList.arraySize = 1;
            SerializedProperty entry = prefabDataList.GetArrayElementAtIndex(0);
            entry.FindPropertyRelative("mItemPrefab").objectReferenceValue = itemPrefab;
            entry.FindPropertyRelative("mInitCreateCount").intValue = initCreateCount;
            // 固定列数必须写进 Prefab，否则 LoopGridView 会以 0 列计算行数而抛出除零异常。
            serializedGrid.FindProperty("mGridFixedType").enumValueIndex = (int)GridFixedType.ColumnCountFixed;
            serializedGrid.FindProperty("mFixedRowOrColumnCount").intValue = columns;
            serializedGrid.FindProperty("mItemSize").vector2Value = itemSize;
            serializedGrid.FindProperty("mItemPadding").vector2Value = itemPadding;
            serializedGrid.ApplyModifiedPropertiesWithoutUndo();

            for (int index = 0; index < examples; index++)
            {
                RectTransform example = CreateExample(itemPrefab, content, index, decorateExample);
                int row = index / columns;
                int column = index % columns;
                example.anchorMin = new Vector2(0f, 1f);
                example.anchorMax = new Vector2(0f, 1f);
                example.pivot = new Vector2(0f, 1f);
                example.anchoredPosition = new Vector2(column * (itemSize.x + itemPadding.x), -row * (itemSize.y + itemPadding.y));
            }
        }

        /// <summary>按滚动方向配置滚动视图。</summary>
        private static void ConfigureScrollRect(GameObject go, RectTransform viewport, RectTransform content, bool vertical)
        {
            ScrollRect scroll = go.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = !vertical;
            scroll.vertical = vertical;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 40f;
        }

        /// <summary>加载条目 Prefab，并在缺失时给出可操作的错误提示。</summary>
        private static GameObject LoadItemPrefab(string name, string itemPrefabPath)
        {
            GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(itemPrefabPath);
            if (itemPrefab == null) throw new InvalidOperationException("Loop view '" + name + "' could not load item prefab '" + itemPrefabPath + "'. Build the item prototype first by giving it a smaller Order.");
            return itemPrefab;
        }

        /// <summary>读取条目 Prefab 的尺寸，用于摆放示例实例。</summary>
        private static Vector2 GetPrefabSize(GameObject itemPrefab)
        {
            RectTransform rect = itemPrefab.GetComponent<RectTransform>();
            return rect == null ? Vector2.zero : rect.rect.size;
        }

        /// <summary>
        /// 在内容区放入一个示例条目实例。
        /// 名称统一以 Example 结尾，便于在接入真实数据时按名称检索并手工删除。
        /// </summary>
        private static RectTransform CreateExample(GameObject itemPrefab, RectTransform content, int index, Action<GameObject, int> decorateExample)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(itemPrefab, content);
            instance.name = itemPrefab.name + "Example" + index;
            if (decorateExample != null) decorateExample(instance, index);
            return instance.GetComponent<RectTransform>();
        }

        /// <summary>创建一个铺满父节点的子 RectTransform。</summary>
        private static RectTransform CreateStretchedChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            RectTransform rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }
    }
}
