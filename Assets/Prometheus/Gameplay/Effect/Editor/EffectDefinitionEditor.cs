#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Effects.Editor
{
    /// <summary>
    /// 标识符编辑模式使用空字符串表示 Automatic，并让非空字符串自然表示 Custom，避免为现有资产增加迁移字段。
    /// </summary>
    internal enum EffectIdentifierMode
    {
        Automatic,
        Custom
    }

    /// <summary>
    /// EffectDefinitionEditor 为 Unity 2021 的 SerializeReference 列表提供明确的操作类型添加菜单，使效果能够直接在 Inspector 中组合。
    /// </summary>
    [CustomEditor(typeof(EffectDefinition))]
    public sealed class EffectDefinitionEditor : UnityEditor.Editor
    {
        private static readonly string[] OperationPropertyNames = { "onApplyOperations", "onStackOperations", "onRefreshOperations", "onTickOperations", "onRemoveOperations" };
        private const string PropertyModifierClipboardPrefix = "Prometheus.PropertyModifierOperation.v1:";

        /// <summary>
        /// 绘制基础配置、五个生命周期操作列表和效果授予的 Trigger 列表。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawEffectIdentifier();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"));
            SerializedProperty durationType = serializedObject.FindProperty("durationType");
            EditorGUILayout.PropertyField(durationType);
            EffectDurationType selectedDurationType = (EffectDurationType)durationType.intValue;
            bool isPersistent = selectedDurationType != EffectDurationType.Instant;
            if (isPersistent)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("buffIcon"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("showInBuffList"));
            }
            if (selectedDurationType == EffectDurationType.Duration) EditorGUILayout.PropertyField(serializedObject.FindProperty("duration"));
            SerializedProperty tickInterval = serializedObject.FindProperty("tickInterval");
            SerializedProperty stackPolicy = serializedObject.FindProperty("stackPolicy");
            SerializedProperty maxStacks = serializedObject.FindProperty("maxStacks");
            if (isPersistent)
            {
                EditorGUILayout.PropertyField(tickInterval);
                EditorGUILayout.PropertyField(stackPolicy);
                EffectStackPolicy selectedStackPolicy = (EffectStackPolicy)stackPolicy.intValue;
                if (selectedStackPolicy != EffectStackPolicy.Independent) EditorGUILayout.PropertyField(serializedObject.FindProperty("stackKeyPolicy"));
                if (SupportsMultipleStacks(selectedStackPolicy)) EditorGUILayout.PropertyField(maxStacks);
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("phase"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("priority"));
            DrawOperationList(OperationPropertyNames[0], "On Apply Operations");
            if (isPersistent)
            {
                EffectStackPolicy selectedStackPolicy = (EffectStackPolicy)stackPolicy.intValue;
                if (ExecutesOnStackOperations(selectedStackPolicy, maxStacks.intValue)) DrawOperationList(OperationPropertyNames[1], "On Stack Operations");
                if (ExecutesOnRefreshOperations(selectedDurationType, selectedStackPolicy)) DrawOperationList(OperationPropertyNames[2], "On Refresh Operations");
                if (tickInterval.floatValue > 0f) DrawOperationList(OperationPropertyNames[3], "On Tick Operations");
                DrawOperationList(OperationPropertyNames[4], "On Remove Operations");
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(serializedObject.FindProperty("grantedTriggers"), true);
            }
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 将空 Effect Id 显示为 Automatic，并在 Custom 模式下提供明确且可校验的字符串输入。
        /// </summary>
        private void DrawEffectIdentifier()
        {
            SerializedProperty effectId = serializedObject.FindProperty("effectId");
            EffectIdentifierMode mode = string.IsNullOrWhiteSpace(effectId.stringValue) ? EffectIdentifierMode.Automatic : EffectIdentifierMode.Custom;
            EffectIdentifierMode selectedMode = (EffectIdentifierMode)EditorGUILayout.EnumPopup("Effect Id Mode", mode);
            if (selectedMode != mode) effectId.stringValue = selectedMode == EffectIdentifierMode.Automatic ? string.Empty : target.name;
            if (selectedMode == EffectIdentifierMode.Automatic)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextField("Resolved Effect Id", target.name);
                return;
            }
            EditorGUILayout.PropertyField(effectId, new GUIContent("Custom Effect Id"));
            if (string.IsNullOrWhiteSpace(effectId.stringValue)) EditorGUILayout.HelpBox("Custom Effect Id is empty, so the asset name will be used automatically.", MessageType.Warning);
        }

        /// <summary>
        /// 判断堆叠策略是否会增加层数并实际读取 Max Stacks。
        /// </summary>
        private static bool SupportsMultipleStacks(EffectStackPolicy policy)
        {
            return policy == EffectStackPolicy.AddStack || policy == EffectStackPolicy.AddStackAndRefreshDuration;
        }

        /// <summary>
        /// 判断堆叠策略和最大层数是否允许层数实际增加，从而执行 On Stack Operations。
        /// </summary>
        private static bool ExecutesOnStackOperations(EffectStackPolicy policy, int maxStacks)
        {
            return SupportsMultipleStacks(policy) && maxStacks > 1;
        }

        /// <summary>
        /// 判断有限持续时间和堆叠策略是否允许实际刷新持续时间，从而执行 On Refresh Operations。
        /// </summary>
        private static bool ExecutesOnRefreshOperations(EffectDurationType durationType, EffectStackPolicy policy)
        {
            return durationType == EffectDurationType.Duration && (policy == EffectStackPolicy.RefreshDuration || policy == EffectStackPolicy.AddStackAndRefreshDuration);
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
                else if (element.managedReferenceValue is DamageOperation) DrawDamageConfiguration(element);
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
        /// 绘制伤害公式、打断能力、标签和伤害属性来源，并只在 Fixed 策略下展示固定属性字段。
        /// </summary>
        private static void DrawDamageConfiguration(SerializedProperty element)
        {
            SerializedProperty amount = element.FindPropertyRelative("amount");
            SerializedProperty interruptPower = element.FindPropertyRelative("interruptPower");
            SerializedProperty additionalTags = element.FindPropertyRelative("additionalTags");
            SerializedProperty damageAttributeSource = element.FindPropertyRelative("damageAttributeSource");
            SerializedProperty fixedDamageAttribute = element.FindPropertyRelative("fixedDamageAttribute");
            if (amount == null || interruptPower == null || additionalTags == null || damageAttributeSource == null || fixedDamageAttribute == null)
            {
                EditorGUILayout.PropertyField(element, GUIContent.none, true);
                return;
            }
            EditorGUILayout.PropertyField(amount, true);
            EditorGUILayout.PropertyField(interruptPower, true);
            EditorGUILayout.PropertyField(additionalTags);
            EditorGUILayout.PropertyField(damageAttributeSource);
            if ((DamageAttributeSource)damageAttributeSource.intValue == DamageAttributeSource.Fixed) EditorGUILayout.PropertyField(fixedDamageAttribute);
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
            menu.AddItem(new GUIContent("Damage Attribute Modifier"), false, () => AddOperation(propertyName, new DamageAttributeModifierOperation()));
            menu.AddItem(new GUIContent("Control State Modifier"), false, () => AddOperation(propertyName, new ControlStateModifierOperation()));
            menu.AddItem(new GUIContent("Apply Effect"), false, () => AddOperation(propertyName, new ApplyEffectOperation()));
            menu.AddItem(new GUIContent("Emit Signal"), false, () => AddOperation(propertyName, new EmitSignalOperation()));
            menu.AddItem(new GUIContent("Gain Core Energy"), false, () => AddOperation(propertyName, new CoreEnergyGainOperation()));
            menu.AddItem(new GUIContent("Gain Ultimate Energy"), false, () => AddOperation(propertyName, new UltEnergyGainOperation()));
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
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
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

    /// <summary>
    /// EffectValueFormulaDrawer 使用业务名称展示基础值来源，并保持公式的三个组成字段紧邻显示。
    /// </summary>
    [CustomPropertyDrawer(typeof(EffectValueFormula))]
    public sealed class EffectValueFormulaDrawer : PropertyDrawer
    {
        /// <summary>公式字段之间使用固定小间距，确保三列边界清晰且不浪费横向空间。</summary>
        private const float ColumnSpacing = 4f;

        /// <summary>
        /// 按折叠状态绘制公式；Property 来源额外展示可自由组合的 Entity 与 Property 字段。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (property.isExpanded)
            {
                line.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                Rect fieldsRect = EditorGUI.IndentedRect(line);
                int previousIndentLevel = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                DrawFormulaFields(fieldsRect, property);
                EditorGUI.indentLevel = previousIndentLevel;
            }
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 返回当前折叠状态所需的精确 Inspector 高度。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float lineHeight = EditorGUIUtility.singleLineHeight;
            return property.isExpanded ? lineHeight * 2f + EditorGUIUtility.standardVerticalSpacing : lineHeight;
        }

        /// <summary>
        /// 普通来源绘制来源、倍率和偏移三列，Property 来源绘制来源、实体、属性、倍率和偏移五列。
        /// </summary>
        private static void DrawFormulaFields(Rect position, SerializedProperty property)
        {
            SerializedProperty baseValueSource = property.FindPropertyRelative("baseValueSource");
            if ((EffectValueSource)baseValueSource.intValue == EffectValueSource.Property)
            {
                DrawPropertyFormulaFields(position, property, baseValueSource);
                return;
            }
            float availableWidth = position.width - ColumnSpacing * 2f;
            float sourceWidth = availableWidth * 0.5f;
            float multiplierWidth = availableWidth * 0.25f;
            Rect sourceRect = new Rect(position.x, position.y, sourceWidth, position.height);
            Rect multiplierRect = new Rect(sourceRect.xMax + ColumnSpacing, position.y, multiplierWidth, position.height);
            Rect offsetRect = new Rect(multiplierRect.xMax + ColumnSpacing, position.y, position.xMax - multiplierRect.xMax - ColumnSpacing, position.height);
            DrawCompactField(sourceRect, baseValueSource, new GUIContent("Base Value Source"), 105f);
            DrawCompactField(multiplierRect, property.FindPropertyRelative("multiplier"), new GUIContent("Multiplier"), 64f);
            DrawCompactField(offsetRect, property.FindPropertyRelative("offset"), new GUIContent("Offset"), 38f);
        }

        /// <summary>
        /// 在单行中绘制正交的 Property 公式字段，使 Caster、Target、Source 可以与任意运行时属性自由组合。
        /// </summary>
        private static void DrawPropertyFormulaFields(Rect position, SerializedProperty property, SerializedProperty baseValueSource)
        {
            float availableWidth = position.width - ColumnSpacing * 4f;
            float sourceWidth = availableWidth * 0.17f;
            float entityWidth = availableWidth * 0.16f;
            float propertyWidth = availableWidth * 0.27f;
            float multiplierWidth = availableWidth * 0.2f;
            Rect sourceRect = new Rect(position.x, position.y, sourceWidth, position.height);
            Rect entityRect = new Rect(sourceRect.xMax + ColumnSpacing, position.y, entityWidth, position.height);
            Rect propertyRect = new Rect(entityRect.xMax + ColumnSpacing, position.y, propertyWidth, position.height);
            Rect multiplierRect = new Rect(propertyRect.xMax + ColumnSpacing, position.y, multiplierWidth, position.height);
            Rect offsetRect = new Rect(multiplierRect.xMax + ColumnSpacing, position.y, position.xMax - multiplierRect.xMax - ColumnSpacing, position.height);
            DrawCompactField(sourceRect, baseValueSource, new GUIContent("Source"), 42f);
            DrawCompactField(entityRect, property.FindPropertyRelative("propertyEntity"), new GUIContent("Entity"), 38f);
            DrawCompactField(propertyRect, property.FindPropertyRelative("propertyValue"), new GUIContent("Property"), 48f);
            DrawCompactField(multiplierRect, property.FindPropertyRelative("multiplier"), new GUIContent("×"), 12f);
            DrawCompactField(offsetRect, property.FindPropertyRelative("offset"), new GUIContent("+"), 12f);
        }

        /// <summary>
        /// 使用字段自身列宽对应的紧凑标签宽度绘制属性，并在结束后恢复全局标签配置。
        /// </summary>
        private static void DrawCompactField(Rect position, SerializedProperty property, GUIContent label, float labelWidth)
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(labelWidth, position.width * 0.6f);
            EditorGUI.PropertyField(position, property, label);
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }
    }

    /// <summary>
    /// EffectConditionDefinitionDrawer 只展示当前条件类型会读取的 Tags 或 Threshold，避免无效配置干扰。
    /// </summary>
    [CustomPropertyDrawer(typeof(EffectConditionDefinition))]
    public sealed class EffectConditionDefinitionDrawer : PropertyDrawer
    {
        /// <summary>条件类型和分支参数之间使用固定小间距，使列表元素保持紧凑且易区分。</summary>
        private const float ColumnSpacing = 4f;

        /// <summary>
        /// 忽略数组元素标题，并将条件类型及其唯一有效的分支参数直接绘制在同一行。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty type = property.FindPropertyRelative("type");
            EffectConditionType conditionType = (EffectConditionType)type.intValue;
            EditorGUI.BeginProperty(position, GUIContent.none, property);
            Rect contentRect = EditorGUI.IndentedRect(new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight));
            int previousIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            SerializedProperty branchProperty = UsesTags(conditionType) ? property.FindPropertyRelative("tags") : UsesThreshold(conditionType) ? property.FindPropertyRelative("threshold") : UsesDamageAttribute(conditionType) ? property.FindPropertyRelative("damageAttribute") : null;
            if (branchProperty == null) DrawCompactField(contentRect, type, new GUIContent("Type"), 34f);
            else DrawConditionPair(contentRect, type, branchProperty, UsesTags(conditionType) ? new GUIContent("Tags") : UsesThreshold(conditionType) ? new GUIContent("Threshold") : new GUIContent("Attribute"), UsesThreshold(conditionType) ? 62f : UsesDamageAttribute(conditionType) ? 54f : 34f);
            EditorGUI.indentLevel = previousIndentLevel;
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 根据条件分支返回精确 Inspector 高度。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// 判断条件是否读取标签掩码。
        /// </summary>
        private static bool UsesTags(EffectConditionType conditionType)
        {
            return conditionType == EffectConditionType.HasAllTags || conditionType == EffectConditionType.HasAnyTags || conditionType == EffectConditionType.LacksAnyTags;
        }

        /// <summary>
        /// 判断条件是否读取数值阈值。
        /// </summary>
        private static bool UsesThreshold(EffectConditionType conditionType)
        {
            return conditionType == EffectConditionType.ValueGreaterThan || conditionType == EffectConditionType.ValueGreaterThanOrEqual;
        }

        /// <summary>
        /// 判断条件是否读取唯一伤害属性配置。
        /// </summary>
        private static bool UsesDamageAttribute(EffectConditionType conditionType)
        {
            return conditionType == EffectConditionType.DamageAttributeEquals;
        }

        /// <summary>
        /// 将条件类型和当前有效的分支参数各占半行绘制。
        /// </summary>
        private static void DrawConditionPair(Rect position, SerializedProperty type, SerializedProperty branchProperty, GUIContent branchLabel, float branchLabelWidth)
        {
            float columnWidth = (position.width - ColumnSpacing) * 0.5f;
            Rect typeRect = new Rect(position.x, position.y, columnWidth, position.height);
            Rect branchRect = new Rect(typeRect.xMax + ColumnSpacing, position.y, position.xMax - typeRect.xMax - ColumnSpacing, position.height);
            DrawCompactField(typeRect, type, new GUIContent("Type"), 34f);
            DrawCompactField(branchRect, branchProperty, branchLabel, branchLabelWidth);
        }

        /// <summary>
        /// 使用紧凑标签宽度绘制条件字段，并在结束后恢复全局标签配置。
        /// </summary>
        private static void DrawCompactField(Rect position, SerializedProperty property, GUIContent label, float labelWidth)
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(labelWidth, position.width * 0.6f);
            EditorGUI.PropertyField(position, property, label);
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }
    }

    /// <summary>
    /// EffectTriggerDefinitionDrawer 为嵌套 Trigger 提供 Automatic/Custom Id，并统一展示所有有效触发参数。
    /// </summary>
    [CustomPropertyDrawer(typeof(EffectTriggerDefinition))]
    public sealed class EffectTriggerDefinitionDrawer : PropertyDrawer
    {
        /// <summary>
        /// 绘制 Trigger 标识符、匹配规则、概率、冷却、条件和效果列表。
        /// </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty triggerId = property.FindPropertyRelative("triggerId");
            SerializedProperty signalType = property.FindPropertyRelative("signalType");
            string resolvedTriggerId = string.IsNullOrWhiteSpace(triggerId.stringValue) ? GetAutomaticTriggerId(signalType) : triggerId.stringValue;
            EditorGUI.BeginProperty(position, label, property);
            Rect cursor = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(cursor, property.isExpanded, new GUIContent($"{label.text}: {resolvedTriggerId}"), true);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawIdentifier(ref cursor, triggerId, signalType);
                DrawChild(ref cursor, signalType);
                DrawChild(ref cursor, property.FindPropertyRelative("listenScope"));
                DrawChild(ref cursor, property.FindPropertyRelative("targetSelector"));
                DrawPair(ref cursor, property.FindPropertyRelative("oncePerSignalChain"), new GUIContent("Once Per Signal Chain"), 125f, property.FindPropertyRelative("probability"), new GUIContent("Probability"), 70f);
                DrawPair(ref cursor, property.FindPropertyRelative("cooldown"), new GUIContent("Cooldown"), 66f, property.FindPropertyRelative("priority"), new GUIContent("Priority"), 52f);
                DrawChild(ref cursor, property.FindPropertyRelative("conditions"), true);
                DrawChild(ref cursor, property.FindPropertyRelative("effects"), true);
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 返回 Trigger 折叠内容及动态列表所需的精确 Inspector 高度。
        /// </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded) return height;
            for (int i = 0; i < 7; i++) height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;
            height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("conditions"), true);
            height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("effects"), true);
            return height;
        }

        /// <summary>
        /// 绘制 Trigger Id 模式及自动解析结果或自定义输入框。
        /// </summary>
        private static void DrawIdentifier(ref Rect cursor, SerializedProperty triggerId, SerializedProperty signalType)
        {
            EffectIdentifierMode mode = string.IsNullOrWhiteSpace(triggerId.stringValue) ? EffectIdentifierMode.Automatic : EffectIdentifierMode.Custom;
            Rect modeRect = GetNextRect(ref cursor, EditorGUIUtility.singleLineHeight);
            EffectIdentifierMode selectedMode = (EffectIdentifierMode)EditorGUI.EnumPopup(modeRect, "Trigger Id Mode", mode);
            if (selectedMode != mode) triggerId.stringValue = selectedMode == EffectIdentifierMode.Automatic ? string.Empty : GetAutomaticTriggerId(signalType);
            Rect idRect = GetNextRect(ref cursor, EditorGUIUtility.singleLineHeight);
            if (selectedMode == EffectIdentifierMode.Automatic)
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUI.TextField(idRect, "Resolved Trigger Id", GetAutomaticTriggerId(signalType));
                return;
            }
            EditorGUI.PropertyField(idRect, triggerId, new GUIContent("Custom Trigger Id"));
        }

        /// <summary>
        /// 使用信号枚举名称生成无需手写的稳定 Trigger Id。
        /// </summary>
        private static string GetAutomaticTriggerId(SerializedProperty signalType)
        {
            int index = Mathf.Clamp(signalType.enumValueIndex, 0, signalType.enumNames.Length - 1);
            return signalType.enumNames[index];
        }

        /// <summary>
        /// 在下一块矩形中绘制普通或递归子属性。
        /// </summary>
        private static void DrawChild(ref Rect cursor, SerializedProperty child, bool includeChildren = false)
        {
            float height = EditorGUI.GetPropertyHeight(child, includeChildren);
            Rect childRect = GetNextRect(ref cursor, height);
            EditorGUI.PropertyField(childRect, child, includeChildren);
        }

        /// <summary>
        /// 将两个 Trigger 参数放在同一行，并为每列使用独立的紧凑标签宽度。
        /// </summary>
        private static void DrawPair(ref Rect cursor, SerializedProperty leftProperty, GUIContent leftLabel, float leftLabelWidth, SerializedProperty rightProperty, GUIContent rightLabel, float rightLabelWidth)
        {
            const float columnSpacing = 6f;
            Rect rowRect = EditorGUI.IndentedRect(GetNextRect(ref cursor, EditorGUIUtility.singleLineHeight));
            int previousIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            float columnWidth = (rowRect.width - columnSpacing) * 0.5f;
            Rect leftRect = new Rect(rowRect.x, rowRect.y, columnWidth, rowRect.height);
            Rect rightRect = new Rect(leftRect.xMax + columnSpacing, rowRect.y, rowRect.xMax - leftRect.xMax - columnSpacing, rowRect.height);
            DrawCompactField(leftRect, leftProperty, leftLabel, leftLabelWidth);
            DrawCompactField(rightRect, rightProperty, rightLabel, rightLabelWidth);
            EditorGUI.indentLevel = previousIndentLevel;
        }

        /// <summary>
        /// 使用指定标签宽度绘制 Trigger 字段，并在结束后恢复全局标签配置。
        /// </summary>
        private static void DrawCompactField(Rect position, SerializedProperty property, GUIContent label, float labelWidth)
        {
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Min(labelWidth, position.width * 0.65f);
            EditorGUI.PropertyField(position, property, label);
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        /// <summary>
        /// 将绘制游标移动到下一个带标准间距的矩形。
        /// </summary>
        private static Rect GetNextRect(ref Rect cursor, float height)
        {
            cursor.y += cursor.height + EditorGUIUtility.standardVerticalSpacing;
            cursor.height = height;
            return cursor;
        }
    }
}
#endif
