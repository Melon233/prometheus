using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Narrative.Editor
{
    /// <summary>
    /// 剧情图资产的检视面板。
    /// 默认检视面板会把整棵 <c>[SerializeReference]</c> 节点树平铺出来，深度一大就完全不可读，
    /// 因此这里只展示标识、舞台声明与校验结果，结构编辑交给剧情图编辑器窗口。
    /// </summary>
    [CustomEditor(typeof(StoryGraph))]
    public sealed class StoryGraphInspector : UnityEditor.Editor
    {
        /// <summary>保存最近一次校验发现的问题。</summary>
        private readonly List<string> problems = new List<string>();

        private bool hasValidated;

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("storyId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("stage"), true);
            serializedObject.ApplyModifiedProperties();

            StoryGraph graph = (StoryGraph)target;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("结构", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("节点数", CountNodes(graph).ToString());

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开剧情图编辑器")) StoryGraphEditorWindow.Open(graph);
            if (GUILayout.Button("校验"))
            {
                problems.Clear();
                StoryGraphValidator.Validate(graph, new StoryVariables(), null, problems);
                hasValidated = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!hasValidated) return;
            EditorGUILayout.Space(4f);
            if (problems.Count == 0)
            {
                EditorGUILayout.HelpBox("校验通过。", MessageType.Info);
                return;
            }
            for (int index = 0; index < problems.Count; index++) EditorGUILayout.HelpBox(problems[index], MessageType.Error);
        }

        /// <summary>统计图中的节点数量。</summary>
        private static int CountNodes(StoryGraph graph)
        {
            int count = 0;
            foreach (StoryNode node in graph.EnumerateNodes()) count++;
            return count;
        }
    }
}
