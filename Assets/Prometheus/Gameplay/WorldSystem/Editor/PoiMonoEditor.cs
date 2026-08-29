using UnityEditor;

namespace Xuan.Prometheus.World.Editor
{
    /// <summary>PoiMono 的自定义 Inspector：交由 PoiConfigDrawer 按 PoiType 分面显示对应 Config。</summary>
    [CustomEditor(typeof(PoiMono))]
    public class PoiMonoEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Config"), true);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
