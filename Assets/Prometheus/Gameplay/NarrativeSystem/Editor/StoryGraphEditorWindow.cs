using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative.Editor
{
    /// <summary>
    /// 剧情图编辑器。
    /// <para>
    /// 采用<b>大纲树</b>而不是节点画布：剧情树是一棵<b>有序</b>树，顺序本身就是语义（Seq 的先后、选项的排列），
    /// 而节点画布对「顺序」没有自然表达，只能靠序号角标或手动按坐标排序；对话内容又是线性阅读的，
    /// 写手需要的是一份可以从上往下扫的剧本，而不是一张需要平移缩放的图。
    /// 大纲树在缩进、折叠、重排、搜索上都直接对应这套模型。
    /// </para>
    /// </summary>
    public sealed class StoryGraphEditorWindow : EditorWindow
    {
        /// <summary>保存当前展开的节点。</summary>
        private readonly HashSet<StoryNode> expanded = new HashSet<StoryNode>();

        /// <summary>保存最近一次校验发现的问题。</summary>
        private readonly List<string> problems = new List<string>();

        private StoryGraph graph;
        private SerializedObject serializedGraph;
        private StoryNode selected;
        private Vector2 outlineScroll;
        private Vector2 inspectorScroll;
        private Vector2 problemScroll;
        private float outlineWidth = 420f;
        private bool hasValidated;

        /// <summary>打开剧情图编辑器窗口。</summary>
        [MenuItem("Prometheus/剧情图编辑器")]
        public static void Open()
        {
            StoryGraphEditorWindow window = GetWindow<StoryGraphEditorWindow>();
            window.titleContent = new GUIContent("剧情图");
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        /// <summary>打开指定图资产。</summary>
        public static void Open(StoryGraph target)
        {
            StoryGraphEditorWindow window = GetWindow<StoryGraphEditorWindow>();
            window.titleContent = new GUIContent("剧情图");
            window.SetGraph(target);
            window.Show();
        }

        /// <summary>双击图资产时打开本窗口。</summary>
        [OnOpenAsset(1)]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            StoryGraph target = EditorUtility.InstanceIDToObject(instanceId) as StoryGraph;
            if (target == null) return false;
            Open(target);
            return true;
        }

        /// <summary>窗口获得焦点时同步当前选中的图资产。</summary>
        private void OnEnable()
        {
            if (graph == null) SetGraph(Selection.activeObject as StoryGraph);
        }

        /// <summary>绘制窗口。</summary>
        private void OnGUI()
        {
            DrawToolbar();
            if (graph == null)
            {
                EditorGUILayout.HelpBox("请选择或创建一份剧情图资产（Create → Prometheus → Narrative → Story Graph）。", MessageType.Info);
                return;
            }
            serializedGraph.Update();

            EditorGUILayout.BeginHorizontal();
            DrawOutlinePane();
            DrawSplitter();
            DrawInspectorPane();
            EditorGUILayout.EndHorizontal();

            DrawProblemPane();
            serializedGraph.ApplyModifiedProperties();
        }

        /// <summary>绘制顶部工具条。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            StoryGraph picked = (StoryGraph)EditorGUILayout.ObjectField(graph, typeof(StoryGraph), false, GUILayout.Width(240f));
            if (!ReferenceEquals(picked, graph)) SetGraph(picked);
            using (new EditorGUI.DisabledScope(graph == null))
            {
                if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(60f))) RunValidation();
                if (GUILayout.Button("全部展开", EditorStyles.toolbarButton, GUILayout.Width(72f))) SetAllExpanded(true);
                if (GUILayout.Button("全部折叠", EditorStyles.toolbarButton, GUILayout.Width(72f))) SetAllExpanded(false);
            }
            GUILayout.FlexibleSpace();
            if (graph != null) GUILayout.Label($"StoryId：{(string.IsNullOrEmpty(graph.StoryId) ? "（未填写）" : graph.StoryId)}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制左侧大纲树。</summary>
        private void DrawOutlinePane()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(outlineWidth));
            outlineScroll = EditorGUILayout.BeginScrollView(outlineScroll);
            if (graph.Root == null)
            {
                EditorGUILayout.HelpBox("这份剧情图还没有根节点。", MessageType.Warning);
                if (GUILayout.Button("创建根节点（顺序 Seq）"))
                {
                    Undo.RecordObject(graph, "创建根节点");
                    graph.Root = new SequenceNode();
                    MarkDirty();
                }
            }
            else
            {
                DrawNodeRow(graph.Root, 0);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>递归绘制一行节点及其子树。</summary>
        private void DrawNodeRow(StoryNode node, int depth)
        {
            if (node == null) return;
            IReadOnlyList<StoryNode> children = node.Children;
            bool isExpanded = expanded.Contains(node);

            Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            if (ReferenceEquals(node, selected)) EditorGUI.DrawRect(row, new Color(0.24f, 0.38f, 0.60f, 0.45f));

            Rect indented = new Rect(row.x + depth * 14f, row.y, row.width - depth * 14f, row.height);
            if (children.Count > 0)
            {
                Rect foldout = new Rect(indented.x, indented.y, 14f, indented.height);
                bool next = EditorGUI.Foldout(foldout, isExpanded, GUIContent.none);
                if (next != isExpanded)
                {
                    if (next) expanded.Add(node);
                    else expanded.Remove(node);
                }
            }
            Rect label = new Rect(indented.x + 16f, indented.y, indented.width - 16f, indented.height);
            if (GUI.Button(label, node.DisplayName, EditorStyles.label)) selected = node;

            if (children.Count == 0 || !expanded.Contains(node)) return;
            for (int index = 0; index < children.Count; index++) DrawNodeRow(children[index], depth + 1);
        }

        /// <summary>绘制可拖拽的分栏条。</summary>
        private void DrawSplitter()
        {
            Rect splitter = GUILayoutUtility.GetRect(4f, 4f, GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(splitter, new Color(0f, 0f, 0f, 0.25f));
            EditorGUIUtility.AddCursorRect(splitter, MouseCursor.ResizeHorizontal);
            // Xuan.Prometheus 命名空间下有同名的 Event 类型，这里必须显式限定到 UnityEngine。
            UnityEngine.Event current = UnityEngine.Event.current;
            if (current.type == EventType.MouseDrag && splitter.Contains(current.mousePosition))
            {
                outlineWidth = Mathf.Clamp(outlineWidth + current.delta.x, 240f, position.width - 320f);
                Repaint();
            }
        }

        /// <summary>绘制右侧属性面板。</summary>
        private void DrawInspectorPane()
        {
            EditorGUILayout.BeginVertical();
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);

            EditorGUILayout.LabelField("剧情图", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedGraph.FindProperty("storyId"));
            EditorGUILayout.PropertyField(serializedGraph.FindProperty("stage"), true);
            EditorGUILayout.Space(8f);

            if (selected == null)
            {
                EditorGUILayout.HelpBox("在左侧选择一个节点以编辑其属性。", MessageType.Info);
            }
            else
            {
                DrawSelectedNode();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>绘制选中节点的操作条与字段。</summary>
        private void DrawSelectedNode()
        {
            EditorGUILayout.LabelField($"节点：{selected.DisplayName}", EditorStyles.boldLabel);
            DrawNodeActions();

            SerializedProperty property = FindNodeProperty(selected);
            if (property == null)
            {
                EditorGUILayout.HelpBox("找不到该节点的序列化数据，请重新选择。", MessageType.Warning);
                return;
            }
            EditorGUILayout.Space(4f);
            DrawOwnFields(property);
            DrawBeatAttachmentFlags();
        }

        /// <summary>绘制添加、移动与删除操作。</summary>
        private void DrawNodeActions()
        {
            StoryNode parent = graph.FindParent(selected);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!selected.AcceptsChildren))
            {
                if (GUILayout.Button("添加子节点 ▾", GUILayout.Width(110f))) ShowCreateMenu(selected);
            }
            using (new EditorGUI.DisabledScope(parent == null))
            {
                if (GUILayout.Button("↑", GUILayout.Width(28f))) MoveSelected(parent, -1);
                if (GUILayout.Button("↓", GUILayout.Width(28f))) MoveSelected(parent, 1);
                if (GUILayout.Button("删除", GUILayout.Width(56f))) DeleteSelected(parent);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制节点自身的字段，跳过承载子节点的字段。</summary>
        private void DrawOwnFields(SerializedProperty node)
        {
            SerializedProperty iterator = node.Copy();
            SerializedProperty end = node.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                // 承载子节点的字段由左侧大纲负责编辑，这里跳过，避免把整棵子树重复画一遍。
                if (ContainsManagedReference(iterator)) continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        /// <summary>为节拍的挂载动作绘制「阻塞」开关，使并发与阻塞可以直接切换。</summary>
        private void DrawBeatAttachmentFlags()
        {
            if (!(selected is BeatNode beat) || beat.Attachments.Count == 0) return;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("挂载动作", EditorStyles.boldLabel);
            for (int index = 0; index < beat.Attachments.Count; index++)
            {
                BeatAttachment attachment = beat.Attachments[index];
                if (attachment == null || attachment.Node == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(attachment.Node.DisplayName);
                bool blocking = EditorGUILayout.ToggleLeft("阻塞推进", attachment.Blocking, GUILayout.Width(90f));
                if (blocking != attachment.Blocking)
                {
                    Undo.RecordObject(graph, "切换阻塞");
                    attachment.Blocking = blocking;
                    MarkDirty();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>绘制底部校验结果面板。</summary>
        private void DrawProblemPane()
        {
            if (!hasValidated) return;
            EditorGUILayout.Space(4f);
            if (problems.Count == 0)
            {
                EditorGUILayout.HelpBox("校验通过：结构、表达式、文案与舞台声明都没有发现问题。", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField($"校验发现 {problems.Count} 个问题", EditorStyles.boldLabel);
            problemScroll = EditorGUILayout.BeginScrollView(problemScroll, GUILayout.Height(120f));
            for (int index = 0; index < problems.Count; index++) EditorGUILayout.HelpBox(problems[index], MessageType.Error);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>弹出节点创建菜单。</summary>
        private void ShowCreateMenu(StoryNode parent)
        {
            GenericMenu menu = new GenericMenu();
            foreach (Type type in GetNodeTypes())
            {
                Type captured = type;
                StoryNode probe = (StoryNode)Activator.CreateInstance(captured);
                // 只做非破坏性查询：绝不能用「先加再删」试探，那会真的改动数据。
                bool allowed = parent.CanAddChild(probe);
                string label = $"{GetCategory(captured)}/{probe.DisplayName}";
                if (!allowed)
                {
                    menu.AddDisabledItem(new GUIContent(label));
                    continue;
                }
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    Undo.RecordObject(graph, "添加节点");
                    StoryNode created = (StoryNode)Activator.CreateInstance(captured);
                    parent.AddChild(created);
                    expanded.Add(parent);
                    selected = created;
                    MarkDirty();
                });
            }
            menu.ShowAsContext();
        }

        /// <summary>移动选中节点在父节点中的位置。</summary>
        private void MoveSelected(StoryNode parent, int delta)
        {
            if (parent == null) return;
            Undo.RecordObject(graph, "移动节点");
            if (parent.MoveChild(selected, delta)) MarkDirty();
        }

        /// <summary>从父节点中删除选中节点。</summary>
        private void DeleteSelected(StoryNode parent)
        {
            if (parent == null) return;
            if (!EditorUtility.DisplayDialog("删除节点", $"确定删除「{selected.DisplayName}」及其全部子节点吗？", "删除", "取消")) return;
            Undo.RecordObject(graph, "删除节点");
            if (!parent.RemoveChild(selected)) return;
            selected = parent;
            MarkDirty();
        }

        /// <summary>执行一次完整校验。</summary>
        private void RunValidation()
        {
            problems.Clear();
            StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems);
            hasValidated = true;
        }

        /// <summary>切换当前编辑的图资产。</summary>
        private void SetGraph(StoryGraph target)
        {
            graph = target;
            serializedGraph = graph == null ? null : new SerializedObject(graph);
            selected = null;
            expanded.Clear();
            problems.Clear();
            hasValidated = false;
            if (graph != null && graph.Root != null) SetAllExpanded(true);
        }

        /// <summary>展开或折叠全部节点。</summary>
        private void SetAllExpanded(bool value)
        {
            expanded.Clear();
            if (!value || graph == null) return;
            foreach (StoryNode node in graph.EnumerateNodes()) expanded.Add(node);
        }

        /// <summary>标记资产已修改并刷新序列化视图。</summary>
        private void MarkDirty()
        {
            EditorUtility.SetDirty(graph);
            serializedGraph = new SerializedObject(graph);
            hasValidated = false;
        }

        /// <summary>按托管引用实例查找其序列化属性。</summary>
        private SerializedProperty FindNodeProperty(StoryNode node)
        {
            SerializedProperty iterator = serializedGraph.GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference) continue;
                if (ReferenceEquals(iterator.managedReferenceValue, node)) return iterator.Copy();
            }
            return null;
        }

        /// <summary>判断一个属性的子树中是否包含托管引用，用于跳过承载子节点的字段。</summary>
        private static bool ContainsManagedReference(SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ManagedReference) return true;
            SerializedProperty iterator = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            while (iterator.NextVisible(true) && !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.propertyType == SerializedPropertyType.ManagedReference) return true;
            }
            return false;
        }

        /// <summary>枚举全部可创建的节点类型。</summary>
        private static IEnumerable<Type> GetNodeTypes()
        {
            return TypeCache.GetTypesDerivedFrom<StoryNode>()
                .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(GetCategory, StringComparer.Ordinal)
                .ThenBy(type => type.Name, StringComparer.Ordinal);
        }

        /// <summary>按节点用途归类，使创建菜单可读。</summary>
        private static string GetCategory(Type type)
        {
            if (type == typeof(SequenceNode) || type == typeof(ParallelNode) || type == typeof(IfNode) || type == typeof(BreakNode)) return "流程";
            if (type == typeof(BeatNode) || type == typeof(ChooseNode) || type == typeof(ChoiceOptionNode)) return "对话";
            if (type == typeof(SetVariableNode) || type == typeof(WaitNode) || type == typeof(WaitForNode)) return "变量与等待";
            if (type == typeof(ScreenFadeNode) || type == typeof(LetterboxNode) || type == typeof(HudNode)) return "屏幕";
            if (type == typeof(CinematicNode) || type == typeof(CameraBlendNode) || type == typeof(CameraShakeNode)) return "镜头与演出";
            return "表现";
        }
    }
}
