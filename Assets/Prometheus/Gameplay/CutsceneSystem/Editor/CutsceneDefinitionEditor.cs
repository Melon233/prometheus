#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using Xuan.Prometheus.Editor;

namespace Xuan.Prometheus.Cutscene.Editor
{
    /// <summary>演出资产的 Inspector：原生绘制（绕开 Odin 兜底编辑器）并提供结构校验按钮。</summary>
    [CustomEditor(typeof(CutsceneDefinition))]
    internal sealed class CutsceneDefinitionEditor : ValidatedAssetEditor
    {
        /// <inheritdoc />
        protected override List<string> CollectErrors()
        {
            List<string> errors = new List<string>();
            ((CutsceneDefinition)target).Validate(errors);
            return errors;
        }
    }
}
#endif
