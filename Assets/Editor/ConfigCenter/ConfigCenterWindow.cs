using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool showRootConfiguration;
        private const string ExpandedGroupPreferencePrefix = "Prometheus.ConfigKit.ExpandedGroup.";
        private const string ShowRootConfigurationPreferenceKey = "Prometheus.ConfigKit.ShowRootConfiguration";

        /// <summary>打开配置中心窗口。</summary>
        [MenuItem("Prometheus/Config Center", false, 10)]
        public static void Open() { GetWindow<ConfigCenterWindow>("Config Center"); }

        /// <summary>由索引器通知已打开窗口重新显示最新索引。</summary>
        internal static void NotifyIndexChanged(ConfigCenterIndex updatedIndex)
        {
            ConfigCenterWindow[] windows = Resources.FindObjectsOfTypeAll<ConfigCenterWindow>();
            foreach (ConfigCenterWindow window in windows) { window.index = updatedIndex; window.Repaint(); }
        }

        /// <summary>初始化窗口并读取派生索引；没有索引时自动执行一次完整扫描。</summary>
        private void OnEnable() { wantsMouseMove = true; ProjectNavigationHistory.DirectorySelectionRequested += RestoreDirectorySelection; configuredRoots = ConfigCenterIndexer.GetConfiguredRoots(); showRootConfiguration = EditorPrefs.GetBool(ShowRootConfigurationPreferenceKey, false); index = ConfigCenterIndexer.Load(); if (index.entries.Count == 0) index = ConfigCenterIndexer.Rebuild(); selectedRowStyle = BuildSelectedRowStyle(); folderRowStyle = BuildFolderRowStyle(); }

        /// <summary>配置中心关闭时不销毁 Unity 原生 Inspector，Inspector 生命周期由 Unity 编辑器布局管理。</summary>
        private void OnDisable() { ProjectNavigationHistory.DirectorySelectionRequested -= RestoreDirectorySelection; if (selectedRowBackground != null) DestroyImmediate(selectedRowBackground); selectedRowBackground = null; selectedRowStyle = null; folderRowStyle = null; }

        /// <summary>绘制仅包含目录树的配置中心布局；配置详情由 Unity 独立 InspectorWindow 负责显示。</summary>
        private void OnGUI()
        {
            if (index == null) index = ConfigCenterIndexer.Load();
            DrawRootConfiguration();
            DrawToolbar();
            DrawDirectoryTree();
        }

        /// <summary>绘制搜索框和索引刷新按钮。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("搜索", GUILayout.Width(35));
            searchText = GUILayout.TextField(searchText, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(180));
            showRootConfiguration = GUILayout.Toggle(showRootConfiguration, "配置", EditorStyles.toolbarButton, GUILayout.Width(55));
            EditorPrefs.SetBool(ShowRootConfigurationPreferenceKey, showRootConfiguration);
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
        private void SaveRootConfiguration() { ConfigCenterIndexer.SaveConfiguredRoots(configuredRoots); RefreshIndex(); }

        /// <summary>重建索引并清理当前选择，避免选择项来自已移除的根目录。</summary>
        private void RefreshIndex() { index = ConfigCenterIndexer.Rebuild(); selectedEntry = null; Repaint(); }

        /// <summary>绘制完整配置目录树；文件夹和配置资产统一在同一棵树中展示。</summary>
        private void DrawDirectoryTree()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            DrawGroupNode(BuildGroupTree(), 0);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>根据索引中的分组路径构建目录树；每个路径段只创建一个节点，保证目录层级稳定。</summary>
        private GroupNode BuildGroupTree()
        {
            GroupNode root = new GroupNode("全部配置", string.Empty);
            foreach (string scanRoot in ConfigCenterIndexer.ScanRoots) root.children.Add(scanRoot, new GroupNode(scanRoot, scanRoot));
            foreach (ConfigCenterEntry entry in index.entries)
            {
                string scanRoot = ConfigCenterIndexer.GetRootPath(entry.assetPath);
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
            return root;
        }

        /// <summary>递归绘制目录节点；节点数量包含自身及全部子目录资产，展开状态按完整路径保存。</summary>
        private void DrawGroupNode(GroupNode node, int depth)
        {
            if (!string.IsNullOrWhiteSpace(searchText) && !node.ContainsSearchMatch(searchText)) return;
            int totalCount = node.GetTotalCount();
            bool isRoot = string.IsNullOrEmpty(node.path);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 16f);
            bool expanded = GetGroupExpanded(node.path);
            Rect arrowRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            bool arrowPressed = UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown && arrowRect.Contains(UnityEngine.Event.current.mousePosition);
            if (arrowPressed) { SetGroupExpanded(node.path, !expanded); UnityEngine.Event.current.Use(); Repaint(); }
            Rect arrowVisualRect = new Rect(arrowRect.x, arrowRect.y + 2.5f, arrowRect.width, arrowRect.height - 2f);
            EditorGUI.Foldout(arrowVisualRect, arrowPressed ? !expanded : expanded, GUIContent.none, false);
            GUIStyle style = selectedGroup == (isRoot ? "全部配置" : node.path) ? selectedRowStyle : folderRowStyle;
            Rect rowRect = GUILayoutUtility.GetRect(new GUIContent($"{node.name} ({totalCount})"), style, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            DrawHoverBackground(rowRect, selectedGroup == (isRoot ? "全部配置" : node.path));
            GUI.Label(rowRect, $"{node.name} ({totalCount})", style);
            if (!arrowPressed && UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown && rowRect.Contains(UnityEngine.Event.current.mousePosition)) { selectedGroup = isRoot ? "全部配置" : node.path; selectedEntry = null; ProjectNavigationHistory.RecordDirectorySelection(selectedGroup); SetGroupExpanded(node.path, !expanded); UnityEngine.Event.current.Use(); Repaint(); }
            EditorGUILayout.EndHorizontal();
            if (!GetGroupExpanded(node.path)) return;
            if (isRoot) { foreach (string scanRoot in ConfigCenterIndexer.ScanRoots) DrawGroupNode(node.children[scanRoot], depth + 1); }
            else { foreach (GroupNode child in node.children.Values.OrderBy(value => value.name, StringComparer.Ordinal)) DrawGroupNode(child, depth + 1); }
            foreach (ConfigCenterEntry entry in node.entries.OrderBy(value => value.displayName, StringComparer.Ordinal)) DrawEntryNode(entry, depth + 1);
        }

        /// <summary>读取目录节点展开状态；全部配置根节点保持展开，其余目录首次出现时默认折叠。</summary>
        private bool GetGroupExpanded(string path)
        {
            return string.IsNullOrEmpty(path) || EditorPrefs.GetBool(ExpandedGroupPreferencePrefix + path, false);
        }

        /// <summary>持久化目录节点的展开状态，使 Unity 重启或窗口重建后仍保留用户的目录折叠习惯。</summary>
        private static void SetGroupExpanded(string path, bool expanded)
        {
            EditorPrefs.SetBool(ExpandedGroupPreferencePrefix + path, expanded);
        }

        /// <summary>判断配置条目是否匹配搜索文本；搜索覆盖显示名、类型名和资产路径。</summary>
        private static bool MatchesSearch(ConfigCenterEntry entry, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            return entry.displayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || entry.typeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || entry.assetPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>绘制目录树中的配置资产叶子节点；按下鼠标左键立即选择并打开独立 Inspector。</summary>
        private void DrawEntryNode(ConfigCenterEntry entry, int depth)
        {
            if (!MatchesSearch(entry, searchText)) return;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 16f + 14f);
            GUIStyle style = selectedEntry == entry ? selectedRowStyle : EditorStyles.label;
            string label = $"{entry.displayName}  [{entry.typeName}]";
            Rect rowRect = GUILayoutUtility.GetRect(new GUIContent(label), style, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            DrawHoverBackground(rowRect, selectedEntry == entry);
            GUI.Label(rowRect, label, style);
            if (UnityEngine.Event.current.type == UnityEngine.EventType.MouseDown && rowRect.Contains(UnityEngine.Event.current.mousePosition)) { SelectEntry(entry); UnityEngine.Event.current.Use(); GUI.ScrollTo(rowRect); }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>切换当前资产并把选择同步给 Unity 独立 InspectorWindow，不在 Config Center 内嵌绘制 Inspector。</summary>
        private void SelectEntry(ConfigCenterEntry entry)
        {
            selectedGroup = null;
            selectedEntry = entry;
            searchText = string.Empty;
            EnsureExpandedPath(entry);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.assetPath);
            FocusNativeInspector();
        }

        private void RestoreDirectorySelection(string path)
        {
            selectedGroup = string.IsNullOrEmpty(path) ? "全部配置" : path;
            selectedEntry = null;
            searchText = string.Empty;
            Repaint();
        }

        /// <summary>展开配置所属分组的全部父级，并在清空搜索后让目录树能够立即显示该配置。</summary>
        private static void EnsureExpandedPath(ConfigCenterEntry entry)
        {
            string scanRoot = ConfigCenterIndexer.GetRootPath(entry.assetPath);
            if (string.IsNullOrEmpty(scanRoot)) return;
            string[] segments = entry.groupPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string currentPath = scanRoot;
            SetGroupExpanded(string.Empty, true);
            SetGroupExpanded(currentPath, true);
            foreach (string segment in segments) { currentPath = string.IsNullOrEmpty(currentPath) ? segment : currentPath + "/" + segment; SetGroupExpanded(currentPath, true); }
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

            /// <summary>计算当前节点及所有后代节点包含的配置数量。</summary>
            public int GetTotalCount() { return entries.Count + children.Values.Sum(child => child.GetTotalCount()); }

            /// <summary>递归判断当前目录或后代目录是否包含匹配搜索条件的配置文件。</summary>
            public bool ContainsSearchMatch(string query) { return entries.Any(entry => MatchesSearch(entry, query)) || children.Values.Any(child => child.ContainsSearchMatch(query)); }
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
