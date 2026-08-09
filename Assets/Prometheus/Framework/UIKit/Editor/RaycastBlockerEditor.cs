using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// 为 RaycastBlocker 提供无配置 Inspector，隐藏 Graphic 继承的材质、颜色和射线等无需人工修改的序列化字段。
    /// </summary>
    [CustomEditor(typeof(RaycastBlocker))]
    [CanEditMultipleObjects]
    public sealed class RaycastBlockerEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 仅显示组件用途说明；RaycastBlocker 的行为完全固定，因此不暴露任何可编辑字段。
        /// </summary>
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("This component provides an invisible full-rect UI raycast blocker and requires no configuration.", MessageType.Info);
        }
    }
}
