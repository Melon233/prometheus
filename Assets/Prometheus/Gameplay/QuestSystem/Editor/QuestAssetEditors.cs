#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Xuan.Prometheus.Editor;

namespace Xuan.Prometheus.Quest.Editor
{
    /// <summary>
    /// 任务资产的 Inspector：原生绘制（绕开 Odin 兜底编辑器）、结构校验按钮，
    /// 并在选中期间于场景视图里画出各节点的区域与交互点——位置按当前打开场景中的 <see cref="WorldAnchor"/> 计算，
    /// 区域与交互点都是球体，可以直接拖动手柄修改偏移与半径（改动写回任务资产，支持撤销）。
    /// 节点按颜色区分，标签形如「N1 区域触发」。
    /// </summary>
    [CustomEditor(typeof(QuestDefinition))]
    internal sealed class QuestDefinitionEditor : ValidatedAssetEditor
    {
        /// <summary>复用的球体手柄：画出球体线框，并提供六个方向的半径拖点。</summary>
        private readonly SphereBoundsHandle sphereHandle = new SphereBoundsHandle();

        /// <summary>选中资产时开始在场景视图中绘制。</summary>
        private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;

        /// <summary>取消选中时停止绘制。</summary>
        private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("选中本资产时，场景视图会按当前场景中的 WorldAnchor 画出各节点的区域与交互点，可直接拖动调整。", MessageType.None);
            base.OnInspectorGUI();
        }

        /// <inheritdoc />
        protected override List<string> CollectErrors()
        {
            List<string> errors = new List<string>();
            ((QuestDefinition)target).Validate(errors);
            return errors;
        }

        /// <summary>遍历全部节点的触发器与目标，按类型画出区域或交互点。</summary>
        private void OnSceneGUI(SceneView view)
        {
            Dictionary<string, Transform> anchors = SceneAnchors();
            serializedObject.Update();
            SerializedProperty nodes = serializedObject.FindProperty("nodes");
            for (int n = 0; n < nodes.arraySize; n++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(n);
                Color color = Color.HSVToRGB(n * 0.17f % 1f, 0.75f, 1f);
                DrawEntries(node.FindPropertyRelative("triggers"), n, color, anchors);
                DrawEntries(node.FindPropertyRelative("objectives"), n, color, anchors);
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>画一个 SerializeReference 列表里与空间有关的条目。</summary>
        private void DrawEntries(SerializedProperty list, int nodeIndex, Color color, Dictionary<string, Transform> anchors)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                switch (entry.managedReferenceValue)
                {
                    case AreaTrigger _:
                        DrawSphere(entry.FindPropertyRelative("area"), false, $"N{nodeIndex} 区域触发", color, anchors);
                        break;
                    case ReachObjective _:
                        DrawSphere(entry.FindPropertyRelative("area"), false, $"N{nodeIndex} 到达目标", color, anchors);
                        break;
                    case InteractTrigger _:
                        DrawSphere(entry, true, $"N{nodeIndex} 交互", color, anchors);
                        break;
                }
            }
        }

        /// <summary>
        /// 球体（区域或交互点）：位置手柄移动球心、球体手柄调整半径，写回 offset 与 radius。
        /// 区域允许不挂锚点（offset 即世界坐标）；交互点必须挂锚点，未配置时不绘制。
        /// </summary>
        /// <param name="property">含 anchorId、offset、radius 三个字段的属性（QuestArea 或 InteractTrigger）。</param>
        /// <param name="anchorRequired">是否必须挂锚点。</param>
        /// <param name="label">场景视图标签。</param>
        /// <param name="color">节点颜色。</param>
        /// <param name="anchors">当前场景中的锚点。</param>
        private void DrawSphere(SerializedProperty property, bool anchorRequired, string label, Color color, Dictionary<string, Transform> anchors)
        {
            string anchorId = property.FindPropertyRelative("anchorId").stringValue;
            if (anchorRequired && string.IsNullOrEmpty(anchorId)) return;
            if (!TryBase(anchorId, anchors, label, out Vector3 basePosition)) return;
            SerializedProperty offset = property.FindPropertyRelative("offset"), radius = property.FindPropertyRelative("radius");
            Vector3 center = basePosition + offset.vector3Value;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(center, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                offset.vector3Value = moved - basePosition;
                center = moved;
            }
            sphereHandle.center = center;
            sphereHandle.radius = radius.floatValue;
            sphereHandle.wireframeColor = color;
            sphereHandle.handleColor = color;
            EditorGUI.BeginChangeCheck();
            sphereHandle.DrawHandle();
            if (EditorGUI.EndChangeCheck())
            {
                offset.vector3Value = sphereHandle.center - basePosition;
                radius.floatValue = sphereHandle.radius;
            }
            Handles.Label(center + Vector3.up * (radius.floatValue + 0.3f), label, LabelStyle(color));
        }

        /// <summary>
        /// 求条目的基准位置：无锚点为世界原点；锚点在当前场景中找不到时在原点标出红色提示并跳过绘制
        /// （任务资产可能服务于另一个场景，这不是错误，跨资产的锚点核对见菜单「校验场景锚点」）。
        /// </summary>
        private static bool TryBase(string anchorId, Dictionary<string, Transform> anchors, string label, out Vector3 basePosition)
        {
            basePosition = Vector3.zero;
            if (string.IsNullOrEmpty(anchorId)) return true;
            if (anchors.TryGetValue(anchorId, out Transform anchor))
            {
                basePosition = anchor.position;
                return true;
            }
            Handles.Label(Vector3.zero, $"{label}：当前场景没有锚点 '{anchorId}'", LabelStyle(Color.red));
            return false;
        }

        /// <summary>当前打开场景中的锚点（忽略空 id 与重复 id，那些由运行时与校验菜单报告）。</summary>
        private static Dictionary<string, Transform> SceneAnchors()
        {
            Dictionary<string, Transform> anchors = new Dictionary<string, Transform>();
            foreach (WorldAnchor anchor in Object.FindObjectsByType<WorldAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!string.IsNullOrEmpty(anchor.AnchorId)) anchors[anchor.AnchorId] = anchor.transform;
            }
            return anchors;
        }

        /// <summary>带颜色的粗体标签样式。</summary>
        private static GUIStyle LabelStyle(Color color)
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = color;
            return style;
        }
    }

    /// <summary>任务目录的 Inspector：校验目录中的每个任务。</summary>
    [CustomEditor(typeof(QuestCatalog))]
    internal sealed class QuestCatalogEditor : ValidatedAssetEditor
    {
        /// <inheritdoc />
        protected override List<string> CollectErrors()
        {
            List<string> errors = new List<string>();
            foreach (QuestDefinition quest in ((QuestCatalog)target).AutoAcceptQuests)
            {
                if (quest == null)
                {
                    errors.Add("目录中存在空的任务引用。");
                    continue;
                }
                List<string> questErrors = new List<string>();
                quest.Validate(questErrors);
                foreach (string error in questErrors) errors.Add($"{quest.name}：{error}");
            }
            return errors;
        }
    }
}
#endif
