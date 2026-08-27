#if UNITY_EDITOR
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>把运行时倍率转换为百分比数值供 Inspector 编辑，并在写回时恢复为倍率。</summary>
    [CustomPropertyDrawer(typeof(PercentageAttribute))]
    public sealed class PercentageAttributeDrawer : PropertyDrawer
    {
        private const float SuffixWidth = 12f;
        private const float SuffixSpacing = 1f;

        /// <summary>把运行时倍率转换为 Inspector 显示的百分比数值。</summary>
        internal static float ToDisplayPercentage(float multiplier)
        {
            return multiplier * 100f;
        }

        /// <summary>把 Inspector 输入的百分比转换为运行时倍率，并应用字段声明的最小值约束。</summary>
        internal static float ToStoredMultiplier(float percentage, float minimumMultiplier)
        {
            return Mathf.Max(minimumMultiplier, percentage / 100f);
        }

        /// <summary>根据当前百分比文本宽度计算输入框内部紧随数字的百分号起点，并保证后缀不会越过输入框右边界。</summary>
        internal static float CalculateSuffixX(Rect valueRect, float displayedPercentage, GUIStyle numberFieldStyle)
        {
            string displayedText = displayedPercentage.ToString("0.#######", CultureInfo.CurrentCulture);
            float textWidth = Mathf.Max(0f, numberFieldStyle.CalcSize(new GUIContent(displayedText)).x - numberFieldStyle.padding.horizontal);
            float requestedX = valueRect.x + numberFieldStyle.padding.left + textWidth + SuffixSpacing;
            return Mathf.Min(requestedX, valueRect.xMax - SuffixWidth - numberFieldStyle.padding.right);
        }

        /// <summary>绘制带百分号后缀的浮点输入框，同时保持底层序列化值使用倍率单位。</summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Float)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }
            EditorGUI.BeginProperty(position, label, property);
            int previousIndentLevel = EditorGUI.indentLevel;
            Rect valueRect = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            EditorGUI.indentLevel = 0;
            bool previousMixedValue = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            float displayedPercentage = EditorGUI.FloatField(valueRect, ToDisplayPercentage(property.floatValue));
            if (EditorGUI.EndChangeCheck())
            {
                PercentageAttribute percentageAttribute = (PercentageAttribute)attribute;
                property.floatValue = ToStoredMultiplier(displayedPercentage, percentageAttribute.MinimumMultiplier);
            }
            EditorGUI.showMixedValue = previousMixedValue;
            if (!property.hasMultipleDifferentValues)
            {
                float suffixX = CalculateSuffixX(valueRect, displayedPercentage, EditorStyles.numberField);
                Rect suffixRect = new Rect(suffixX, valueRect.y, SuffixWidth, valueRect.height);
                GUI.Label(suffixRect, "%", EditorStyles.label);
            }
            EditorGUI.indentLevel = previousIndentLevel;
            EditorGUI.EndProperty();
        }
    }
}
#endif
