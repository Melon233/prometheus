using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.World.Editor
{
    /// <summary>
    /// PoiConfig 的自定义 PropertyDrawer：折叠头显示 Id + 类型名；
    /// 展开后按 PoiType 仅显示对应的专属 Config，避免同屏出现全部 8 个 Config。
    /// 自动作用于烘焙资产 WorldRegionsConfig 里嵌套的 List&lt;PoiConfig&gt; 与 PoiMono.Config。
    /// </summary>
    // [CustomPropertyDrawer(typeof(PoiConfig))]
    public class PoiConfigDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            SerializedProperty id = property.FindPropertyRelative("Id");
            SerializedProperty poiType = property.FindPropertyRelative("PoiType");
            float y = position.y;

            // 折叠头：Id + 类型名。
            property.isExpanded = EditorGUI.Foldout(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight),
                property.isExpanded, new GUIContent($"{id.stringValue}  [{poiType.enumDisplayNames[poiType.enumValueIndex]}]"), true);
            y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            if (property.isExpanded)
            {
                SerializedProperty positionProp = property.FindPropertyRelative("Position");
                // Position 由 GameObject transform 驱动（烘焙时写回），只读展示避免与 transform 冲突。
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight), positionProp);
                EditorGUI.EndDisabledGroup();
                y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                // 仅显示 PoiType 对应的专属 Config。
                SerializedProperty activeConfig = GetActiveConfig(property, (PoiType)poiType.enumValueIndex);
                if (activeConfig != null)
                {
                    float configHeight = EditorGUI.GetPropertyHeight(activeConfig, true);
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, configHeight), activeConfig, true);
                }
            }
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float total = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // 折叠头
            if (!property.isExpanded) return total;
            total += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing; // Position
            SerializedProperty poiType = property.FindPropertyRelative("PoiType");
            SerializedProperty activeConfig = GetActiveConfig(property, (PoiType)poiType.enumValueIndex);
            if (activeConfig != null) total += EditorGUI.GetPropertyHeight(activeConfig, true);
            return total;
        }

        /// <summary>按 PoiType 返回对应的专属 Config 属性。</summary>
        private static SerializedProperty GetActiveConfig(SerializedProperty property, PoiType type)
        {
            switch (type)
            {
                case PoiType.TeleAnchor: return property.FindPropertyRelative("TeleAnchor");
                case PoiType.Statue: return property.FindPropertyRelative("Statue");
                case PoiType.Chest: return property.FindPropertyRelative("Chest");
                case PoiType.SpiritCore: return property.FindPropertyRelative("SpiritCore");
                case PoiType.Gathering: return property.FindPropertyRelative("Gathering");
                case PoiType.Dungeon: return property.FindPropertyRelative("Dungeon");
                case PoiType.MapBoss: return property.FindPropertyRelative("MapBoss");
                case PoiType.MonsterCamp: return property.FindPropertyRelative("MonsterCamp");
                default: return null;
            }
        }
    }
}
