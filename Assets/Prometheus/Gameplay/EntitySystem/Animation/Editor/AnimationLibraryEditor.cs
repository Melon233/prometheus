using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>为 AnimationLibrary 保留 Odin Inspector 绘制能力，并提供仅存在于 Editor 程序集的 MixDuration 矩阵入口。</summary>
    [CustomEditor(typeof(AnimationLibrary))]
    public sealed class AnimationLibraryEditor : OdinEditor
    {
        /// <summary>先绘制 AnimationLibrary 的完整 Odin Inspector，再追加 MixDuration 矩阵编辑按钮。</summary>
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space();
            if (GUILayout.Button("打开 MixDuration 矩阵配置")) AnimationMixDurationMatrixWindow.Open((AnimationLibrary)target);
        }
    }
}
