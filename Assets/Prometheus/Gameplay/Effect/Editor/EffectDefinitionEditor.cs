#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Effects.Editor
{
    /// <summary>
    /// EffectDefinitionEditor 为 Unity 2021 的 SerializeReference 列表提供明确的操作类型添加菜单，使效果能够直接在 Inspector 中组合。
    /// </summary>
    [CustomEditor(typeof(EffectDefinition))]
    public sealed class EffectDefinitionEditor : UnityEditor.Editor
    {
        private static readonly string[] OperationPropertyNames = { "onApplyOperations", "onStackOperations", "onTickOperations", "onRemoveOperations" };
        private const string PropertyModifierClipboardPrefix = "Prometheus.PropertyModifierOperation.v1:";

        /// <summary>
        /// 绘制基础配置、四个生命周期操作列表和效果授予的 Trigger 列表。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", OperationPropertyNames[0], OperationPropertyNames[1], OperationPropertyNames[2], OperationPropertyNames[3]);
            DrawOperationList(OperationPropertyNames[0], "On Apply Operations");
            DrawOperationList(OperationPropertyNames[1], "On Stack Operations");
            DrawOperationList(OperationPropertyNames[2], "On Tick Operations");
            DrawOperationList(OperationPropertyNames[3], "On Remove Operations");
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 绘制一个托管引用操作列表，并允许单独删除或通过类型菜单添加元素。
        /// </summary>
        private void DrawOperationList(string propertyName, string label)
        {
            SerializedProperty list = serializedObject.FindProperty(propertyName);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            int removeIndex = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                Rect operationRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                string typeName = element.managedReferenceValue == null ? "Unassigned Operation" : element.managedReferenceValue.GetType().Name;
                EditorGUILayout.LabelField($"{i}: {typeName}", EditorStyles.boldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(72f))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
                if (element.managedReferenceValue is PropertyModifierOperation) DrawPropertyModifierConfiguration(element);
                else if (element.managedReferenceValue != null) EditorGUILayout.PropertyField(element, GUIContent.none, true);
                EditorGUILayout.EndVertical();
                HandlePropertyModifierContextClick(operationRect, propertyName, i, element.managedReferenceValue);
            }
            if (removeIndex >= 0) list.DeleteArrayElementAtIndex(removeIndex);
            if (GUILayout.Button("Add Operation")) ShowAddOperationMenu(propertyName);
        }

        /// <summary>
        /// 按属性修改语义绘制配置：自动策略只展示生成结果，只有 Custom 策略才允许输入自定义键。
        /// </summary>
        private static void DrawPropertyModifierConfiguration(SerializedProperty element)
        {
            SerializedProperty propertyType = element.FindPropertyRelative("propertyType");
            SerializedProperty modifierMode = element.FindPropertyRelative("modifierMode");
            SerializedProperty keyPolicy = element.FindPropertyRelative("keyPolicy");
            SerializedProperty customModifierKey = element.FindPropertyRelative("customModifierKey");
            SerializedProperty valuePerStack = element.FindPropertyRelative("valuePerStack");
            if (propertyType == null || modifierMode == null || keyPolicy == null || customModifierKey == null || valuePerStack == null)
            {
                EditorGUILayout.PropertyField(element, GUIContent.none, true);
                return;
            }

            EditorGUILayout.PropertyField(propertyType);
            EditorGUILayout.PropertyField(modifierMode);
            EditorGUILayout.PropertyField(keyPolicy);
            PropertyModifierKeyPolicy policy = (PropertyModifierKeyPolicy)keyPolicy.intValue;
            if (policy == PropertyModifierKeyPolicy.Custom)
            {
                EditorGUILayout.PropertyField(customModifierKey);
                if (string.IsNullOrWhiteSpace(customModifierKey.stringValue)) EditorGUILayout.HelpBox("Custom Modifier Key is empty, so the operation will fall back to the automatic key.", MessageType.Warning);
            }
            else
            {
                string resolvedKey = PropertyModifierOperation.BuildAutomaticKey((PropertyType)propertyType.intValue, (PropertyModifierMode)modifierMode.intValue);
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Resolved Key", resolvedKey);
            }

            EditorGUILayout.PropertyField(valuePerStack, true);
        }

        /// <summary>
        /// 显示当前系统提供的全部原子操作类型。
        /// </summary>
        private void ShowAddOperationMenu(string propertyName)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Damage"), false, () => AddOperation(propertyName, new DamageOperation()));
            menu.AddItem(new GUIContent("Heal"), false, () => AddOperation(propertyName, new HealOperation()));
            menu.AddItem(new GUIContent("Property Modifier"), false, () => AddOperation(propertyName, new PropertyModifierOperation()));
            menu.AddItem(new GUIContent("Control State Modifier"), false, () => AddOperation(propertyName, new ControlStateModifierOperation()));
            menu.AddItem(new GUIContent("Apply Effect"), false, () => AddOperation(propertyName, new ApplyEffectOperation()));
            menu.AddItem(new GUIContent("Emit Signal"), false, () => AddOperation(propertyName, new EmitSignalOperation()));
            menu.ShowAsContext();
        }

        /// <summary>
        /// 将新操作写入指定 SerializeReference 列表并立即保存序列化修改。
        /// </summary>
        private void AddOperation(string propertyName, EffectOperation operation)
        {
            serializedObject.Update();
            SerializedProperty list = serializedObject.FindProperty(propertyName);
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            element.managedReferenceValue = operation;
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            list = serializedObject.FindProperty(propertyName);
            element = list.GetArrayElementAtIndex(index);
            if (operation is PropertyModifierOperation) ExpandPropertyModifier(element);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        /// <summary>
        /// 在 PropertyModifierOperation 区域响应鼠标右键，并提供跨列表和跨资产复制粘贴配置的上下文菜单。
        /// </summary>
        private void HandlePropertyModifierContextClick(Rect operationRect, string propertyName, int index, object operation)
        {
            Event currentEvent = Event.current;
            if (!(operation is PropertyModifierOperation) || currentEvent.type != UnityEngine.EventType.ContextClick || !operationRect.Contains(currentEvent.mousePosition)) return;
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy Configuration"), false, () => CopyPropertyModifierConfiguration(propertyName, index));
            if (TryReadPropertyModifierClipboard(out _)) menu.AddItem(new GUIContent("Paste Configuration"), false, () => PastePropertyModifierConfiguration(propertyName, index));
            else menu.AddDisabledItem(new GUIContent("Paste Configuration"));
            menu.ShowAsContext();
            currentEvent.Use();
        }

        /// <summary>
        /// 将指定 PropertyModifierOperation 的全部序列化字段写入系统剪贴板，并附加格式标识防止粘贴无关文本。
        /// </summary>
        private void CopyPropertyModifierConfiguration(string propertyName, int index)
        {
            serializedObject.Update();
            SerializedProperty list = serializedObject.FindProperty(propertyName);
            if (list == null || index < 0 || index >= list.arraySize) return;
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            if (!(element.managedReferenceValue is PropertyModifierOperation operation)) return;
            EditorGUIUtility.systemCopyBuffer = PropertyModifierClipboardPrefix + EditorJsonUtility.ToJson(operation);
        }

        /// <summary>
        /// 用剪贴板中的完整配置替换指定 PropertyModifierOperation，并接入 Unity Undo 与资产脏标记。
        /// </summary>
        private void PastePropertyModifierConfiguration(string propertyName, int index)
        {
            if (!TryReadPropertyModifierClipboard(out string json)) return;
            PropertyModifierOperation pastedOperation = new PropertyModifierOperation();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(json, pastedOperation);
            }
            catch (System.ArgumentException exception)
            {
                Debug.LogError($"Cannot paste PropertyModifierOperation configuration: {exception.Message}", target);
                return;
            }

            Undo.RecordObject(target, "Paste Property Modifier Configuration");
            serializedObject.Update();
            SerializedProperty list = serializedObject.FindProperty(propertyName);
            if (list == null || index < 0 || index >= list.arraySize) return;
            SerializedProperty element = list.GetArrayElementAtIndex(index);
            if (!(element.managedReferenceValue is PropertyModifierOperation)) return;
            element.managedReferenceValue = pastedOperation;
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            list = serializedObject.FindProperty(propertyName);
            element = list.GetArrayElementAtIndex(index);
            ExpandPropertyModifier(element);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            Repaint();
        }

        /// <summary>
        /// 校验剪贴板格式并返回 PropertyModifierOperation 的 JSON 配置正文。
        /// </summary>
        private static bool TryReadPropertyModifierClipboard(out string json)
        {
            string clipboard = EditorGUIUtility.systemCopyBuffer ?? string.Empty;
            if (!clipboard.StartsWith(PropertyModifierClipboardPrefix, System.StringComparison.Ordinal))
            {
                json = null;
                return false;
            }

            json = clipboard.Substring(PropertyModifierClipboardPrefix.Length);
            return !string.IsNullOrWhiteSpace(json);
        }

        /// <summary>
        /// 展开新建或粘贴后的操作及其 valuePerStack，使数值来源、Multiplier 和 Offset 初始即可见。
        /// </summary>
        private static void ExpandPropertyModifier(SerializedProperty element)
        {
            if (element == null) return;
            element.isExpanded = true;
            SerializedProperty valuePerStack = element.FindPropertyRelative("valuePerStack");
            if (valuePerStack != null) valuePerStack.isExpanded = true;
        }
    }
}
#endif
