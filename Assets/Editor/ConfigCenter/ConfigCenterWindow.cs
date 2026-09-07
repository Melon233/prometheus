using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Editor;

namespace Xuan.Prometheus.ConfigKit.Editor
{
    /// <summary>配置中心主窗口；提供分组导航、文本检索和通过 Unity 原生 InspectorWindow 定位资产的能力。</summary>
    public sealed class ConfigCenterWindow : EditorWindow
    {
        private ConfigCenterIndex index;
        private Vector2 listScroll;
        private string selectedGroup = "全部配置";
        private string searchText = string.Empty;
        private ConfigCenterEntry selectedEntry;
        private GUIStyle selectedRowStyle;
        private GUIStyle folderRowStyle;
        private Texture2D selectedRowBackground;
        private List<string> configuredRoots;

        /// <summary>当前索引对应的缓存目录树，仅在索引或扫描根目录变化时重建。</summary>
        private GroupNode groupTree;

        /// <summary>当前搜索和展开状态生成的扁平行列表，供滚动视口按索引直接访问。</summary>
        private readonly List<TreeRow> visibleRows = new List<TreeRow>();

        /// <summary>当前编辑器会话中的展开目录集合，避免每次绘制读取 EditorPrefs。</summary>
        private readonly HashSet<string> expandedGroups = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>当前搜索文本直接命中的配置集合，避免绘制阶段重复执行字符串匹配。</summary>
        private readonly HashSet<ConfigCenterEntry> matchingEntries = new HashSet<ConfigCenterEntry>();

        /// <summary>等待目录树重排完成后滚动定位的配置，避免使用搜索或展开前的旧行坐标。</summary>
        private ConfigCenterEntry pendingScrollEntry;
        private bool showRootConfiguration;
        private const string ExpandedGroupPreferencePrefix = "Prometheus.ConfigKit.ExpandedGroup.";
        private const string ShowRootConfigurationPreferenceKey = "Prometheus.ConfigKit.ShowRootConfiguration";

        /// <summary>目录树每级缩进使用的固定宽度。</summary>
        private const float TreeIndentWidth = 16f;

        /// <summary>目录折叠箭头和配置叶子占位使用的固定宽度。</summary>
        private const float FoldoutWidth = 14f;

        /// <summary>视口上下额外绘制的行数，避免滚动边界出现瞬时空白。</summary>
        private const int VisibleRowOverscan = 2;

        /// <summary>打开配置中心窗口。</summary>
        [MenuItem("Prometheus/Config Center", false, 10)]
        public static void Open() { GetWindow<ConfigCenterWindow>("Config Center"); }

        /// <summary>由索引器通知已打开窗口重新显示最新索引。</summary>
        internal static void NotifyIndexChanged(ConfigCenterIndex updatedIndex)
        {
            ConfigCenterWindow[] windows = Resources.FindObjectsOfTypeAll<ConfigCenterWindow>();
            foreach (ConfigCenterWindow window in windows) window.ApplyIndex(updatedIndex);
        }

        /// <summary>初始化窗口并读取派生索引；没有索引时自动执行一次完整扫描。</summary>
        private void OnEnable() { wantsMouseMove = true; ProjectNavigationHistory.DirectorySelectionRequested += RestoreDirectorySelection; configuredRoots = ConfigCenterIndexer.GetConfiguredRoots(); showRootConfiguration = EditorPrefs.GetBool(ShowRootConfigurationPreferenceKey, false); index = ConfigCenterIndexer.Load(); if (index.entries.Count == 0) index = ConfigCenterIndexer.Rebuild(); selectedRowStyle = BuildSelectedRowStyle(); folderRowStyle = BuildFolderRowStyle(); RebuildGroupTree(); }

        /// <summary>配置中心关闭时不销毁 Unity 原生 Inspector，Inspector 生命周期由 Unity 编辑器布局管理。</summary>
        private void OnDisable() { ProjectNavigationHistory.DirectorySelectionRequested -= RestoreDirectorySelection; if (selectedRowBackground != null) DestroyImmediate(selectedRowBackground); selectedRowBackground = null; selectedRowStyle = null; folderRowStyle = null; }

        /// <summary>绘制仅包含目录树的配置中心布局；配置详情由 Unity 独立 InspectorWindow 负责显示。</summary>
        private void OnGUI()
        {
            if (index == null) ApplyIndex(ConfigCenterIndexer.Load());
            DrawRootConfiguration();
            DrawToolbar();
            DrawDirectoryTree();
        }

        /// <summary>绘制搜索框和索引刷新按钮。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("搜索", GUILayout.Width(35));
            string updatedSearchText = GUILayout.TextField(searchText, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(180));
            if (!string.Equals(updatedSearchText, searchText, StringComparison.Ordinal)) { searchText = updatedSearchText; RebuildVisibleRows(); }
            bool updatedShowRootConfiguration = GUILayout.Toggle(showRootConfiguration, "配置", EditorStyles.toolbarButton, GUILayout.Width(55));
            if (updatedShowRootConfiguration != showRootConfiguration) { showRootConfiguration = updatedShowRootConfiguration; EditorPrefs.SetBool(ShowRootConfigurationPreferenceKey, showRootConfiguration); }
            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(55))) { RefreshIndex(); }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制搜索栏上方的一级目录配置列表；每次字段、添加或删除操作都会立即刷新索引。</summary>
        private void DrawRootConfiguration()
        {
            if (!showRootConfiguration) return;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("一级目录", EditorStyles.boldLabel);
            for (int index = 0; index < configuredRoots.Count; index++)
            {
                EditorGUILayout.BeginHorizontal();
                string updatedRoot = EditorGUILayout.TextField(configuredRoots[index]);
                if (!string.Equals(updatedRoot, configuredRoots[index], StringComparison.Ordinal)) { configuredRoots[index] = updatedRoot; SaveRootConfiguration(); }
                if (GUILayout.Button("移除", EditorStyles.miniButton, GUILayout.Width(45))) { configuredRoots.RemoveAt(index); SaveRootConfiguration(); break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("添加目录", EditorStyles.miniButton)) { configuredRoots.Add("Assets/"); SaveRootConfiguration(); }
            EditorGUILayout.EndVertical();
        }

        /// <summary>保存一级目录设置并立即重建配置索引，确保目录树与当前配置同步。</summary>
        private void SaveRootConfiguration() { ConfigCenterIndexer.SaveConfiguredRoots(configuredRoots); configuredRoots = ConfigCenterIndexer.GetConfiguredRoots(); RefreshIndex(); }

        /// <summary>重建索引并清理当前选择，避免选择项来自已移除的根目录。</summary>
        private void RefreshIndex() { ApplyIndex(ConfigCenterIndexer.Rebuild()); selectedEntry = null; }

        /// <summary>应用最新索引并一次性重建目录树缓存，供主动刷新和资产导入通知共用。</summary>
        private void ApplyIndex(ConfigCenterIndex updatedIndex) { index = updatedIndex; RebuildGroupTree(); Repaint(); }

        /// <summary>绘制完整配置目录树；文件夹和配置资产统一在同一棵树中展示。</summary>
        private void DrawDirectoryTree()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            float rowHeight = EditorGUIUtility.singleLineHeight;
            float contentHeight = visibleRows.Count * rowHeight;
            Rect contentRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.Height(contentHeight));
            ScrollToPendingEntry(contentRect, rowHeight);
            int firstVisibleRow = Mathf.Clamp(Mathf.FloorToInt(listScroll.y / rowHeight) - VisibleRowOverscan, 0, visibleRows.Count);
            int lastVisibleRow = Mathf.Clamp(Mathf.CeilToInt((listScroll.y + position.height) / rowHeight) + VisibleRowOverscan, firstVisibleRow, visibleRows.Count);
            for (int rowIndex = firstVisibleRow; rowIndex < lastVisibleRow; rowIndex++) { DrawTreeRow(visibleRows[rowIndex], new Rect(contentRect.x, contentRect.y + rowIndex * rowHeight, contentRect.width, rowHeight)); if (UnityEngine.Event.current.type == UnityEngine.EventType.Used) break; }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>在扁平列表完成重建后按配置的新行号滚动，定位成功或条目消失后清理一次性请求。</summary>
        private void ScrollToPendingEntry(Rect contentRect, float rowHeight)
        {
            if (pendingScrollEntry == null) return;
            int rowIndex = visibleRows.FindIndex(row => row.entry == pendingScrollEntry);
            if (rowIndex >= 0) GUI.ScrollTo(new Rect(contentRect.x, contentRect.y + rowIndex * rowHeight, contentRect.width, rowHeight));
            pendingScrollEntry = null;
        }

        /// <summary>根据当前索引重建稳定目录树，并预计算排序、配置数量、展开状态和搜索结果。</summary>
        private void RebuildGroupTree()
        {
            GroupNode root = new GroupNode("全部配置", string.Empty);
            foreach (string scanRoot in configuredRoots) { GroupNode rootNode = new GroupNode(scanRoot, scanRoot); root.children.Add(scanRoot, rootNode); root.orderedChildren.Add(rootNode); }
            foreach (ConfigCenterEntry entry in index.entries)
            {
                string scanRoot = ConfigCenterIndexer.GetRootPath(entry.assetPath, configuredRoots);
                if (string.IsNullOrEmpty(scanRoot) || !root.children.TryGetValue(scanRoot, out GroupNode rootNode)) continue;
                GroupNode current = rootNode;
                string[] segments = entry.groupPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    if (!current.children.TryGetValue(segment, out GroupNode child)) { string path = string.IsNullOrEmpty(current.path) ? segment : current.path + "/" + segment; child = new GroupNode(segment, path); current.children.Add(segment, child); }
                    current = child;
                }
                current.entries.Add(entry);
            }
            root.FinalizeNode(true);
            groupTree = root;
            LoadExpandedGroups(root);
            RebuildVisibleRows();
        }

        /// <summary>读取当前目录树所有节点的持久化展开状态，仅在树缓存重建时访问 EditorPrefs。</summary>
        private void LoadExpandedGroups(GroupNode node)
        {
            if (!string.IsNullOrEmpty(node.path) && EditorPrefs.GetBool(ExpandedGroupPreferencePrefix + node.path, false)) expandedGroups.Add(node.path);
            foreach (GroupNode child in node.orderedChildren) LoadExpandedGroups(child);
        }

        /// <summary>根据缓存树、搜索结果和折叠状态生成当前需要展示的扁平行列表。</summary>
        private void RebuildVisibleRows()
        {
            visibleRows.Clear();
            matchingEntries.Clear();
            if (groupTree == null) return;
            groupTree.UpdateSearch(searchText, matchingEntries);
            AppendVisibleRows(groupTree, 0);
        }

        /// <summary>递归追加搜索命中的展开节点；目录树变化前不会重复分配、排序或统计节点。</summary>
        private void AppendVisibleRows(GroupNode node, int depth)
        {
            if (!node.matchesSearch) return;
            visibleRows.Add(new TreeRow(node, depth));
            if (!IsGroupExpanded(node.path)) return;
            foreach (GroupNode child in node.orderedChildren) AppendVisibleRows(child, depth + 1);
            foreach (ConfigCenterEntry entry in node.entries) if (string.IsNullOrWhiteSpace(searchText) || matchingEntries.Contains(entry)) visibleRows.Add(new TreeRow(entry, depth + 1));
        }

        /// <summary>从内存缓存读取目录展开状态；根节点始终展开。</summary>
        private bool IsGroupExpanded(string path) { return string.IsNullOrEmpty(path) || expandedGroups.Contains(path); }

        /// <summary>仅在展开状态实际变化时更新内存和 EditorPrefs，避免绘制事件产生持久化访问。</summary>
        private void SetGroupExpanded(string path, bool expanded)
        {
            if (string.IsNullOrEmpty(path)) return;
            bool changed = expanded ? expandedGroups.Add(path) : expandedGroups.Remove(path);
            if (changed) EditorPrefs.SetBool(ExpandedGroupPreferencePrefix + path, expanded);
        }

        /// <summary>判断配置条目是否匹配搜索文本；搜索覆盖显示名、类型名和资产路径。</summary>
        private static bool MatchesSearch(ConfigCenterEntry entry, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return entry.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || entry.typeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || entry.assetPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>绘制视口内的一行目录或配置，视口外条目只保留总高度而不参与 GUI 绘制和命中检测。</summary>
        private void DrawTreeRow(TreeRow row, Rect rowRect)
        {
            if (row.group != null) { DrawGroupRow(row, rowRect); return; }
            Rect labelRect = new Rect(rowRect.x + row.depth * TreeIndentWidth + FoldoutWidth, rowRect.y, Mathf.Max(0f, rowRect.width - row.depth * TreeIndentWidth - FoldoutWidth), rowRect.height);
            GUIStyle style = selectedEntry == row.entry ? selectedRowStyle : EditorStyles.label;
            DrawHoverBackground(labelRect, selectedEntry == row.entry);
            GUI.Label(labelRect, row.label, style);
            if (UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown && labelRect.Contains(UnityEngine.Event.current.mousePosition)) { SelectEntry(row.entry); UnityEngine.Event.current.Use(); }
        }

        /// <summary>绘制视口内的目录行，并在箭头或文字被按下时更新缓存和持久化展开状态。</summary>
        private void DrawGroupRow(TreeRow row, Rect rowRect)
        {
            GroupNode node = row.group;
            bool isRoot = string.IsNullOrEmpty(node.path);
            bool expanded = IsGroupExpanded(node.path);
            Rect arrowRect = new Rect(rowRect.x + row.depth * TreeIndentWidth, rowRect.y, FoldoutWidth, rowRect.height);
            Rect labelRect = new Rect(arrowRect.xMax, rowRect.y, Mathf.Max(0f, rowRect.xMax - arrowRect.xMax), rowRect.height);
            bool arrowPressed = UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown && arrowRect.Contains(UnityEngine.Event.current.mousePosition);
            bool labelPressed = UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown && labelRect.Contains(UnityEngine.Event.current.mousePosition);
            if (arrowPressed || labelPressed)
            {
                string selectedPath = isRoot ? "全部配置" : node.path;
                if (labelPressed) { selectedGroup = selectedPath; selectedEntry = null; ProjectNavigationHistory.RecordDirectorySelection(selectedGroup); }
                SetGroupExpanded(node.path, !expanded);
                RebuildVisibleRows();
                expanded = IsGroupExpanded(node.path);
                UnityEngine.Event.current.Use();
                Repaint();
            }
            Rect arrowVisualRect = new Rect(arrowRect.x, arrowRect.y + 2.5f, arrowRect.width, arrowRect.height - 2f);
            EditorGUI.Foldout(arrowVisualRect, expanded, GUIContent.none, false);
            string groupSelection = isRoot ? "全部配置" : node.path;
            GUIStyle style = selectedGroup == groupSelection ? selectedRowStyle : folderRowStyle;
            DrawHoverBackground(labelRect, selectedGroup == groupSelection);
            GUI.Label(labelRect, row.label, style);
        }

        /// <summary>切换当前资产并把选择同步给 Unity 独立 InspectorWindow，不在 Config Center 内嵌绘制 Inspector。</summary>
        private void SelectEntry(ConfigCenterEntry entry)
        {
            selectedGroup = null;
            selectedEntry = entry;
            searchText = string.Empty;
            EnsureExpandedPath(entry);
            pendingScrollEntry = entry;
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.assetPath);
            FocusNativeInspector();
        }

        private void RestoreDirectorySelection(string path)
        {
            selectedGroup = string.IsNullOrEmpty(path) ? "全部配置" : path;
            selectedEntry = null;
            searchText = string.Empty;
            RebuildVisibleRows();
            Repaint();
        }

        /// <summary>展开配置所属分组的全部父级，并在清空搜索后让目录树能够立即显示该配置。</summary>
        private void EnsureExpandedPath(ConfigCenterEntry entry)
        {
            string scanRoot = ConfigCenterIndexer.GetRootPath(entry.assetPath, configuredRoots);
            if (string.IsNullOrEmpty(scanRoot)) return;
            string[] segments = entry.groupPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string currentPath = scanRoot;
            SetGroupExpanded(currentPath, true);
            foreach (string segment in segments) { currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + "/" + segment; SetGroupExpanded(currentPath, true); }
            RebuildVisibleRows();
        }

        /// <summary>创建绿色选中行样式；使用与普通 Label 相同的内边距，避免选中后文字横向偏移。</summary>
        private GUIStyle BuildSelectedRowStyle()
        {
            selectedRowBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            selectedRowBackground.SetPixel(0, 0, new Color(0.18f, 0.58f, 0.25f, 1f));
            selectedRowBackground.Apply();
            GUIStyle style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(EditorStyles.label.padding.left, EditorStyles.label.padding.right, EditorStyles.label.padding.top, EditorStyles.label.padding.bottom) };
            style.normal.background = selectedRowBackground;
            style.normal.textColor = Color.white;
            return style;
        }

        /// <summary>创建文件夹文字淡蓝色样式；背景保持透明，选中状态由绿色选中样式覆盖。</summary>
        private GUIStyle BuildFolderRowStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, padding = new RectOffset(EditorStyles.label.padding.left, EditorStyles.label.padding.right, EditorStyles.label.padding.top, EditorStyles.label.padding.bottom) };
            style.normal.background = null;
            style.normal.textColor = new Color(0.32f, 0.58f, 0.86f, 1f);
            return style;
        }

        /// <summary>在鼠标悬浮且条目未选中时绘制浅色背景，选中项继续由绿色选中样式负责绘制。</summary>
        private static void DrawHoverBackground(Rect rowRect, bool selected)
        {
            if (selected || UnityEngine.Event.current.type != UnityEngine.EventType.Repaint || !rowRect.Contains(UnityEngine.Event.current.mousePosition)) return;
            EditorGUI.DrawRect(rowRect, new Color(0.22f, 0.30f, 0.24f, 1f));
        }

        /// <summary>表示配置目录树中的一个节点，保存当前目录直接拥有的资产数和子目录。</summary>
        private sealed class GroupNode
        {
            /// <summary>创建目录节点。</summary>
            public GroupNode(string name, string path) { this.name = name; this.path = path; }

            public readonly string name;
            public readonly string path;
            public readonly List<ConfigCenterEntry> entries = new List<ConfigCenterEntry>();
            public readonly Dictionary<string, GroupNode> children = new Dictionary<string, GroupNode>(StringComparer.Ordinal);

            /// <summary>按照窗口显示规则预先排序的直接子目录。</summary>
            public readonly List<GroupNode> orderedChildren = new List<GroupNode>();

            /// <summary>当前目录及全部后代拥有的配置总数。</summary>
            public int totalCount;

            /// <summary>当前目录子树是否包含当前搜索文本的匹配项。</summary>
            public bool matchesSearch;

            /// <summary>一次性排序子节点和配置，并缓存包含全部后代的配置数量；根节点保留扫描目录配置顺序。</summary>
            public int FinalizeNode(bool preserveChildOrder)
            {
                if (!preserveChildOrder) { orderedChildren.Clear(); orderedChildren.AddRange(children.Values); orderedChildren.Sort((left, right) => StringComparer.Ordinal.Compare(left.name, right.name)); }
                entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.displayName, right.displayName));
                totalCount = entries.Count;
                foreach (GroupNode child in orderedChildren) totalCount += child.FinalizeNode(false);
                return totalCount;
            }

            /// <summary>在搜索条件变化时遍历当前子树一次，缓存目录命中状态并收集直接命中的配置条目。</summary>
            public bool UpdateSearch(string query, HashSet<ConfigCenterEntry> matchingEntries)
            {
                bool hasMatch = string.IsNullOrWhiteSpace(query);
                if (!hasMatch) foreach (ConfigCenterEntry entry in entries) if (MatchesSearch(entry, query)) { matchingEntries.Add(entry); hasMatch = true; }
                foreach (GroupNode child in orderedChildren) if (child.UpdateSearch(query, matchingEntries)) hasMatch = true;
                matchesSearch = hasMatch;
                return hasMatch;
            }
        }

        /// <summary>表示缓存后的单行目录树数据；目录行和配置行共享固定高度与缩进绘制路径。</summary>
        private sealed class TreeRow
        {
            /// <summary>创建目录行并缓存包含配置数量的显示文本。</summary>
            public TreeRow(GroupNode group, int depth) { this.group = group; this.depth = depth; label = $"{group.name} ({group.totalCount})"; }

            /// <summary>创建配置资产行并缓存显示名和类型名文本。</summary>
            public TreeRow(ConfigCenterEntry entry, int depth) { this.entry = entry; this.depth = depth; label = $"{entry.displayName}  [{entry.typeName}]"; }

            /// <summary>目录行对应的节点；配置行中为空。</summary>
            public readonly GroupNode group;

            /// <summary>配置行对应的索引条目；目录行中为空。</summary>
            public readonly ConfigCenterEntry entry;

            /// <summary>当前行相对于根节点的缩进层级。</summary>
            public readonly int depth;

            /// <summary>目录计数或配置类型已经拼接完成的显示文本。</summary>
            public readonly string label;
        }

        /// <summary>打开或激活 Unity 原生 InspectorWindow；其停靠位置由当前 Editor 布局和用户拖拽决定。</summary>
        private static void FocusNativeInspector()
        {
            Type inspectorWindowType = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");
            if (inspectorWindowType == null) return;
            EditorWindow inspectorWindow = EditorWindow.GetWindow(inspectorWindowType);
            inspectorWindow.Show();
        }
    }
}
