#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// 叙事资产（演出、任务）Inspector 的公共基类：用 Unity 原生 IMGUI 绘制全部字段，并在底部提供资产校验。
    ///
    /// 必须显式声明 CustomEditor 的原因：项目中 Odin 会作为兜底编辑器接管所有没有专属编辑器的类型，
    /// 而当前 Odin 版本在 Unity 6.6 下初始化即抛出（EntityId.op_Implicit 未实现），选中资产就会报错。
    /// 更具体的 CustomEditor 优先于 Odin 的兜底编辑器，同时也保证 <see cref="SerializeReferencePickerAttribute"/> 的绘制器生效。
    /// </summary>
    internal abstract class ValidatedAssetEditor : UnityEditor.Editor
    {
        /// <summary>上一次点击校验得到的错误；为空表示尚未校验。</summary>
        private List<string> lastErrors;

        /// <summary>收集当前资产的结构错误。</summary>
        /// <returns>错误列表；空列表表示通过。</returns>
        protected abstract List<string> CollectErrors();

        /// <inheritdoc />
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            if (UnityEngine.GUILayout.Button("校验")) lastErrors = CollectErrors();
            if (lastErrors == null) return;
            if (lastErrors.Count == 0) EditorGUILayout.HelpBox("校验通过。场景标记是否存在要到运行时检查。", MessageType.Info);
            else EditorGUILayout.HelpBox(string.Join("\n", lastErrors), MessageType.Error);
        }
    }
}
#endif
