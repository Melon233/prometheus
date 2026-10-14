#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// <see cref="SerializeReferencePickerAttribute"/> 的绘制器：首行画元素标签与类型下拉，下方展开绘制当前实例的全部字段。
    /// 切换类型会丢弃旧实例的字段值（不同子类之间没有可靠的字段映射），由 Undo 兜住误操作。
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializeReferencePickerAttribute))]
    internal sealed class SerializeReferencePickerDrawer : PropertyDrawer
    {
        /// <summary>按字段基类缓存可选子类，避免每次重绘都扫描全部类型。</summary>
        private static readonly Dictionary<Type, Type[]> CandidateCache = new Dictionary<Type, Type[]>();

        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // 空引用只占一行下拉；有实例时用 Unity 的默认高度（含展开的子字段）。
            if (property.managedReferenceValue == null) return EditorGUIUtility.singleLineHeight;
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                EditorGUI.LabelField(position, label.text, "SerializeReferencePicker 只能用于 [SerializeReference] 字段");
                return;
            }
            EditorGUI.BeginProperty(position, label, property);
            // 类型按钮放在首行右侧，宽度取标签之外的区域，不遮挡折叠箭头。
            Rect buttonRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y, position.width - EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
            object current = property.managedReferenceValue;
            string buttonText = current == null ? "（空）选择类型…" : ObjectNames.NicifyVariableName(current.GetType().Name);
            if (EditorGUI.DropdownButton(buttonRect, new GUIContent(buttonText), FocusType.Keyboard)) ShowTypeMenu(property);
            if (current == null) EditorGUI.LabelField(new Rect(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight), label);
            else EditorGUI.PropertyField(position, property, label, true);
            EditorGUI.EndProperty();
        }

        /// <summary>弹出候选子类菜单；选中后写入新实例并记录 Undo。</summary>
        /// <param name="property">当前绘制的 SerializeReference 属性。</param>
        private static void ShowTypeMenu(SerializedProperty property)
        {
            Type baseType = ResolveFieldBaseType(property);
            GenericMenu menu = new GenericMenu();
            SerializedObject owner = property.serializedObject;
            string path = property.propertyPath;
            menu.AddItem(new GUIContent("（空）"), property.managedReferenceValue == null, () => Assign(owner, path, null));
            foreach (Type candidate in GetCandidates(baseType))
            {
                Type captured = candidate;
                bool selected = property.managedReferenceValue != null && property.managedReferenceValue.GetType() == captured;
                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(captured.Name)), selected, () => Assign(owner, path, Activator.CreateInstance(captured)));
            }
            menu.ShowAsContext();
        }

        /// <summary>在菜单回调里重新定位属性并赋值；回调执行时原 SerializedProperty 可能已经失效。</summary>
        private static void Assign(SerializedObject owner, string path, object value)
        {
            owner.Update();
            SerializedProperty property = owner.FindProperty(path);
            property.managedReferenceValue = value;
            owner.ApplyModifiedProperties();
        }

        /// <summary>从 managedReferenceFieldTypename（"程序集 类型全名"）解析字段声明的基类型。</summary>
        private static Type ResolveFieldBaseType(SerializedProperty property)
        {
            string[] parts = property.managedReferenceFieldTypename.Split(' ');
            Type type = Type.GetType($"{parts[1]}, {parts[0]}");
            if (type == null) throw new InvalidOperationException($"无法解析 SerializeReference 字段类型 '{property.managedReferenceFieldTypename}'。");
            return type;
        }

        /// <summary>列出基类型的全部可实例化子类，按名称排序。</summary>
        private static Type[] GetCandidates(Type baseType)
        {
            if (CandidateCache.TryGetValue(baseType, out Type[] cached)) return cached;
            Type[] result = TypeCache.GetTypesDerivedFrom(baseType)
                .Where(t => !t.IsAbstract && !t.IsGenericType && !typeof(UnityEngine.Object).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null && t.IsSerializable)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToArray();
            CandidateCache[baseType] = result;
            return result;
        }
    }
}
#endif
