using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>
    /// 为 UIComponentBinder 提供组件表校验和一键生成 PanelBase/Panel 脚本入口。
    /// </summary>
    [CustomEditor(typeof(UIComponentBinder))]
    public sealed class UIComponentBinderEditor : UnityEditor.Editor
    {
        private const float IndexColumnWidth = 42f;
        private const float NameColumnWidth = 160f;
        private const float RemoveButtonWidth = 24f;
        private const float DragHandleWidth = 24f;
        private const float ColumnSpacing = 4f;
        private const float BindingRowSpacing = 2f;
        private const float DragAnimationResponsiveness = 18f;
        private const int DragPreviewTextureSize = 16;
        private const int DragPreviewCornerRadius = 5;
        private const int DragControlHint = 1847321;
        private const string BindingNameControlPrefix = "Prometheus.UIKit.BindingName.";
        private static readonly Color RegisterButtonColor = new Color32(145, 252, 177, 255);
        private static readonly Color DrewBindColor = new Color(0.35f, 0.65f, 1f, 0.14f) * 2f;
        private static readonly Color DraggedRowDarkBackgroundColor = new Color(0.24f, 0.26f, 0.3f, 0.98f);
        private static readonly Color DraggedRowLightBackgroundColor = new Color(0.82f, 0.86f, 0.92f, 0.98f);
        private static readonly Color DraggedRowShadowColor = new Color(0f, 0f, 0f, 0.28f);

        private SerializedProperty bindingsProperty;
        private int draggedBindingIndex = -1;
        private int dragInsertionIndex = -1;
        private Vector2 dragPointerPosition;
        private Vector2 dragPointerOffset;
        private double lastDragAnimationTime;
        private int editingBindingNameIndex = -1;
        private string editingBindingName = string.Empty;
        private readonly Dictionary<int, float> animatedBindingRowYPositions = new Dictionary<int, float>();
        private GUIStyle bindingInsertionGapStyle;
        private GUIStyle draggedRowBackgroundStyle;
        private GUIStyle draggedRowShadowStyle;
        private Texture2D bindingInsertionGapTexture;
        private Texture2D draggedRowBackgroundTexture;
        private Texture2D draggedRowShadowTexture;
        private bool dragPreviewStylesUseProSkin;

        /// <summary>
        /// Inspector 首次绑定目标时缓存组件表属性，后续所有行都由自定义表格直接绘制，因此不再依赖数组和元素的折叠状态。
        /// </summary>
        private void OnEnable()
        {
            serializedObject.Update();
            bindingsProperty = serializedObject.FindProperty("bindings");
        }

        /// <summary>
        /// Inspector 销毁或切换目标时取消尚未提交的拖动预览，避免 IMGUI 热控制权残留到其他 Inspector。
        /// </summary>
        private void OnDisable()
        {
            CancelBindingDrag();
            DestroyDragPreviewResources();
        }

        /// <summary>
        /// 绘制单行组件绑定表、批量注册入口、即时校验信息和生成按钮。
        /// </summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (bindingsProperty == null)
                bindingsProperty = serializedObject.FindProperty("bindings");

            DrawBindingTable();
            serializedObject.ApplyModifiedProperties();
            UIComponentBinder binder = (UIComponentBinder)target;
            GUILayout.Space(10f);
            DrawRegisterAllButton(binder);
            DrawValidationMessages(binder);
            GUILayout.Space(8f);

            if (GUILayout.Button("Generate Panel Code", GUILayout.Height(28f)))
            {
                CommitEditingBindingName(true);
                serializedObject.ApplyModifiedProperties();
                UIPanelCodeGenerator.Generate(binder);
            }
        }

        /// <summary>
        /// 将绑定表绘制为支持浮动预览、目标空槽和让位动画的固定行列表，鼠标松开前不会修改真实序列化顺序。
        /// </summary>
        private void DrawBindingTable()
        {
            if (bindingsProperty == null)
            {
                EditorGUILayout.HelpBox("Cannot find the serialized bindings list.", MessageType.Error);
                return;
            }

            DrawBindingTableHeader();
            int bindingCount = bindingsProperty.arraySize;
            if (editingBindingNameIndex >= bindingCount)
                ClearBindingNameEditingState();

            if (bindingCount == 0)
            {
                CancelBindingDrag();
                ClearBindingNameEditingState();
                EditorGUILayout.HelpBox("No component bindings have been added.", MessageType.Info);
                return;
            }

            float rowHeight = EditorGUIUtility.singleLineHeight;
            float rowPitch = rowHeight + BindingRowSpacing;
            float listHeight = bindingCount * rowPitch - BindingRowSpacing;
            Rect listRect = GUILayoutUtility.GetRect(0f, listHeight, GUILayout.ExpandWidth(true));
            int dragControlId = GUIUtility.GetControlID(DragControlHint, FocusType.Passive);
            ValidateBindingDragState(bindingCount, dragControlId);
            UpdateBindingDragTarget(listRect, rowPitch, bindingCount, dragControlId);
            float animationDeltaTime = GetDragAnimationDeltaTime();
            int removeIndex = -1;
            for (int index = 0; index < bindingCount; index++)
            {
                if (IsBindingDragActive(dragControlId) && index == draggedBindingIndex)
                    continue;

                int targetSlot = IsBindingDragActive(dragControlId) ? GetBindingPreviewSlot(index) : index;
                float defaultRowY = listRect.y + index * rowPitch;
                float targetRowY = listRect.y + targetSlot * rowPitch;
                float animatedRowY = IsBindingDragActive(dragControlId) ? GetAnimatedBindingRowY(index, defaultRowY, targetRowY, animationDeltaTime) : targetRowY;
                Rect rowRect = new Rect(listRect.x, animatedRowY, listRect.width, rowHeight);
                if (DrawInteractiveBindingRow(rowRect, listRect, rowPitch, index, targetSlot, dragControlId))
                    removeIndex = index;
            }

            if (IsBindingDragActive(dragControlId))
            {
                DrawBindingInsertionGap(listRect, rowPitch, rowHeight);
                DrawFloatingBindingRow(listRect, rowHeight);
                Repaint();
            }

            CompleteBindingDrag(dragControlId);

            if (removeIndex >= 0)
            {
                HandleBindingRemovedFromNameEditing(removeIndex);
                bindingsProperty.DeleteArrayElementAtIndex(removeIndex);
            }
        }

        /// <summary>
        /// 绘制绑定表的列标题；右侧为移除按钮和拖动柄预留固定宽度，从而保证标题和数据列严格对齐。
        /// </summary>
        private static void DrawBindingTableHeader()
        {
            Rect headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            GetBindingColumnRects(headerRect, out Rect indexRect, out Rect nameRect, out Rect componentRect, out _, out _);
            GUI.Label(indexRect, "Index", EditorStyles.miniBoldLabel);
            GUI.Label(nameRect, "Name", EditorStyles.miniBoldLabel);
            GUI.Label(componentRect, "Component", EditorStyles.miniBoldLabel);
        }

        /// <summary>
        /// 根据 Inspector 当前宽度计算 Index、Name、Component、移除按钮和拖动柄矩形，窄窗口下优先压缩 Name 并保留操作区宽度。
        /// </summary>
        private static void GetBindingColumnRects(Rect rowRect, out Rect indexRect, out Rect nameRect, out Rect componentRect, out Rect removeRect, out Rect dragHandleRect)
        {
            dragHandleRect = new Rect(rowRect.xMax - DragHandleWidth, rowRect.y, DragHandleWidth, rowRect.height);
            removeRect = new Rect(dragHandleRect.x - ColumnSpacing - RemoveButtonWidth, rowRect.y, RemoveButtonWidth, rowRect.height);
            indexRect = new Rect(rowRect.x, rowRect.y, IndexColumnWidth, rowRect.height);
            float fieldsStartX = indexRect.xMax + ColumnSpacing;
            float fieldsEndX = removeRect.x - ColumnSpacing;
            float availableFieldsWidth = Mathf.Max(0f, fieldsEndX - fieldsStartX);
            float resolvedNameWidth = Mathf.Min(NameColumnWidth, Mathf.Max(70f, availableFieldsWidth - 84f));
            nameRect = new Rect(fieldsStartX, rowRect.y, resolvedNameWidth, rowRect.height);
            float componentStartX = nameRect.xMax + ColumnSpacing;
            componentRect = new Rect(componentStartX, rowRect.y, Mathf.Max(0f, fieldsEndX - componentStartX), rowRect.height);
        }

        /// <summary>
        /// 为每个 Name 输入框生成当前 Inspector 内唯一且顺序稳定的 IMGUI 控件名称，用于准确识别当前焦点所属绑定。
        /// </summary>
        /// <param name="bindingIndex">绑定在真实序列化数组中的索引。</param>
        /// <returns>包含 Binder 实例编号和绑定索引的唯一控件名称。</returns>
        private string GetBindingNameControlName(int bindingIndex)
        {
            return BindingNameControlPrefix + target.GetInstanceID() + "." + bindingIndex;
        }

        /// <summary>
        /// 将当前 Name 草稿写入对应绑定并同步组件 GameObject 名称；Generate Code 会在生成前通过该入口强制保存焦点内容。
        /// </summary>
        /// <param name="clearKeyboardFocus">提交后是否同时清除 IMGUI 键盘焦点。</param>
        private void CommitEditingBindingName(bool clearKeyboardFocus)
        {
            if (editingBindingNameIndex >= 0 && bindingsProperty != null && editingBindingNameIndex < bindingsProperty.arraySize)
            {
                SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(editingBindingNameIndex);
                SerializedProperty nameProperty = bindingProperty.FindPropertyRelative("name");
                SerializedProperty componentProperty = bindingProperty.FindPropertyRelative("component");
                string committedBindingName = editingBindingName ?? string.Empty;
                if (!string.Equals(nameProperty.stringValue, committedBindingName, StringComparison.Ordinal))
                {
                    nameProperty.stringValue = committedBindingName;
                    SynchronizeBoundObjectName(componentProperty.objectReferenceValue as UnityEngine.Component, committedBindingName);
                }
            }

            ClearBindingNameEditingState();
            if (clearKeyboardFocus)
                GUI.FocusControl(null);
        }

        /// <summary>
        /// 清除当前 Name 输入草稿和绑定索引，不修改已经保存到 SerializedProperty 的名称。
        /// </summary>
        private void ClearBindingNameEditingState()
        {
            editingBindingNameIndex = -1;
            editingBindingName = string.Empty;
        }

        /// <summary>
        /// 删除绑定后丢弃被删除行的名称草稿，或修正后续编辑行因数组收缩产生的真实索引偏移。
        /// </summary>
        /// <param name="removedIndex">即将从序列化绑定数组中删除的索引。</param>
        private void HandleBindingRemovedFromNameEditing(int removedIndex)
        {
            if (editingBindingNameIndex == removedIndex)
                ClearBindingNameEditingState();
            else if (editingBindingNameIndex > removedIndex)
                editingBindingNameIndex--;
        }

        /// <summary>
        /// 绘制一条可交互绑定行，包括实时预览索引、可强制提交的名称草稿、组件引用、移除按钮和位于最右侧的拖动柄。
        /// </summary>
        /// <param name="rowRect">当前绑定行经过拖动动画计算后的绘制区域。</param>
        /// <param name="listRect">整个绑定列表的绘制区域。</param>
        /// <param name="rowPitch">一行高度与行间距之和。</param>
        /// <param name="rowIndex">绑定在真实序列化数组中的索引。</param>
        /// <param name="displayIndex">绑定按照当前拖动预览顺序即将落下的索引。</param>
        /// <param name="dragControlId">当前绑定列表持有的 IMGUI 拖动控制编号。</param>
        /// <returns>用户点击当前绑定行的移除按钮时返回 true。</returns>
        private bool DrawInteractiveBindingRow(Rect rowRect, Rect listRect, float rowPitch, int rowIndex, int displayIndex, int dragControlId)
        {
            SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(rowIndex);
            SerializedProperty nameProperty = bindingProperty.FindPropertyRelative("name");
            SerializedProperty componentProperty = bindingProperty.FindPropertyRelative("component");
            GetBindingColumnRects(rowRect, out Rect indexRect, out Rect nameRect, out Rect componentRect, out Rect removeRect, out Rect dragHandleRect);
            GUI.Label(indexRect, displayIndex.ToString(), EditorStyles.miniLabel);
            string nameControlName = GetBindingNameControlName(rowIndex);
            bool wasEditingCurrentName = editingBindingNameIndex == rowIndex;
            string displayedBindingName = wasEditingCurrentName ? editingBindingName : nameProperty.stringValue;
            GUI.SetNextControlName(nameControlName);
            string editedBindingName = EditorGUI.TextField(nameRect, GUIContent.none, displayedBindingName);
            bool currentNameHasFocus = string.Equals(GUI.GetNameOfFocusedControl(), nameControlName, StringComparison.Ordinal);
            if (currentNameHasFocus)
            {
                if (!wasEditingCurrentName)
                {
                    CommitEditingBindingName(false);
                    editingBindingNameIndex = rowIndex;
                }

                editingBindingName = editedBindingName;
                if (UnityEngine.Event.current.rawType == UnityEngine.EventType.KeyDown && (UnityEngine.Event.current.keyCode == KeyCode.Return || UnityEngine.Event.current.keyCode == KeyCode.KeypadEnter))
                    CommitEditingBindingName(true);
            }
            else if (wasEditingCurrentName)
                CommitEditingBindingName(false);

            EditorGUI.PropertyField(componentRect, componentProperty, GUIContent.none);
            bool shouldRemove = GUI.Button(removeRect, new GUIContent("×", $"Remove binding currently displayed at index {displayIndex}."), EditorStyles.miniButton);
            EditorGUIUtility.AddCursorRect(dragHandleRect, MouseCursor.Pan);
            GUI.Box(dragHandleRect, new GUIContent("≡", "Drag this binding to reorder the list."), EditorStyles.miniButton);
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            if (currentEvent.type == UnityEngine.EventType.MouseDown && currentEvent.button == 0 && dragHandleRect.Contains(currentEvent.mousePosition))
                BeginBindingDrag(rowRect, listRect, rowPitch, rowIndex, dragControlId);

            return shouldRemove;
        }

        /// <summary>
        /// 从指定行开始拖动，记录鼠标抓取偏移、初始插入位置和每行动画起点，但暂不改变绑定数组。
        /// </summary>
        private void BeginBindingDrag(Rect rowRect, Rect listRect, float rowPitch, int rowIndex, int dragControlId)
        {
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            CommitEditingBindingName(true);
            draggedBindingIndex = rowIndex;
            dragInsertionIndex = rowIndex;
            dragPointerPosition = currentEvent.mousePosition;
            dragPointerOffset = currentEvent.mousePosition - rowRect.position;
            lastDragAnimationTime = EditorApplication.timeSinceStartup;
            animatedBindingRowYPositions.Clear();
            for (int index = 0; index < bindingsProperty.arraySize; index++)
                animatedBindingRowYPositions[index] = listRect.y + index * rowPitch;

            GUIUtility.hotControl = dragControlId;
            currentEvent.Use();
            Repaint();
        }

        /// <summary>
        /// 在拖动事件中更新浮动行位置，并根据鼠标跨过的行边界选择新的目标空槽。
        /// </summary>
        private void UpdateBindingDragTarget(Rect listRect, float rowPitch, int bindingCount, int dragControlId)
        {
            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            if (currentEvent.type != UnityEngine.EventType.MouseDrag || !IsBindingDragActive(dragControlId))
                return;

            dragPointerPosition = currentEvent.mousePosition;
            int resolvedInsertionIndex = Mathf.Clamp(Mathf.FloorToInt((dragPointerPosition.y - listRect.y) / rowPitch), 0, bindingCount - 1);
            if (resolvedInsertionIndex != dragInsertionIndex)
                dragInsertionIndex = resolvedInsertionIndex;

            currentEvent.Use();
            Repaint();
        }

        /// <summary>
        /// 返回原始绑定在当前拖动预览中的显示槽位，使源空槽收拢并在目标位置打开一个完整行高的新空槽。
        /// </summary>
        private int GetBindingPreviewSlot(int bindingIndex)
        {
            if (draggedBindingIndex < dragInsertionIndex && bindingIndex > draggedBindingIndex && bindingIndex <= dragInsertionIndex)
                return bindingIndex - 1;

            if (draggedBindingIndex > dragInsertionIndex && bindingIndex >= dragInsertionIndex && bindingIndex < draggedBindingIndex)
                return bindingIndex + 1;

            return bindingIndex;
        }

        /// <summary>
        /// 使用指数插值把非拖动行平滑移动到预览槽位，动画结果按原始索引缓存并在拖动期间持续重绘。
        /// </summary>
        private float GetAnimatedBindingRowY(int bindingIndex, float defaultRowY, float targetRowY, float animationDeltaTime)
        {
            if (!animatedBindingRowYPositions.TryGetValue(bindingIndex, out float currentRowY))
                currentRowY = defaultRowY;

            float interpolation = animationDeltaTime <= 0f ? 0f : 1f - Mathf.Exp(-DragAnimationResponsiveness * animationDeltaTime);
            float animatedRowY = Mathf.Abs(currentRowY - targetRowY) <= 0.05f ? targetRowY : Mathf.Lerp(currentRowY, targetRowY, interpolation);
            animatedBindingRowYPositions[bindingIndex] = animatedRowY;
            return animatedRowY;
        }

        /// <summary>
        /// 只在 Repaint 事件推进让位动画，并限制单帧步长以避免 Editor 暂停后出现位置跳变。
        /// </summary>
        private float GetDragAnimationDeltaTime()
        {
            if (UnityEngine.Event.current.type != UnityEngine.EventType.Repaint || draggedBindingIndex < 0)
                return 0f;

            double currentTime = EditorApplication.timeSinceStartup;
            float deltaTime = Mathf.Clamp((float)(currentTime - lastDragAnimationTime), 0f, 0.05f);
            lastDragAnimationTime = currentTime;
            return deltaTime;
        }

        /// <summary>
        /// 在预期落点绘制带抗锯齿圆角且不包含控件的空槽，让用户能够直接看到松开鼠标后绑定将占据的位置。
        /// </summary>
        private void DrawBindingInsertionGap(Rect listRect, float rowPitch, float rowHeight)
        {
            EnsureDragPreviewStyles();
            Rect gapRect = new Rect(listRect.x, listRect.y + dragInsertionIndex * rowPitch, listRect.width, rowHeight);
            GUI.Box(gapRect, GUIContent.none, bindingInsertionGapStyle);
        }

        /// <summary>
        /// 在鼠标抓取位置绘制脱离列表的绑定副本和阴影；副本仅用于预览，不响应名称、组件或移除操作。
        /// </summary>
        private void DrawFloatingBindingRow(Rect listRect, float rowHeight)
        {
            if (draggedBindingIndex < 0 || draggedBindingIndex >= bindingsProperty.arraySize)
                return;

            EnsureDragPreviewStyles();
            Rect floatingRect = new Rect(dragPointerPosition.x - dragPointerOffset.x, dragPointerPosition.y - dragPointerOffset.y, listRect.width, rowHeight);
            Rect shadowRect = new Rect(floatingRect.x + 2f, floatingRect.y + 2f, floatingRect.width, floatingRect.height);
            GUI.Box(shadowRect, GUIContent.none, draggedRowShadowStyle);
            GUI.Box(floatingRect, GUIContent.none, draggedRowBackgroundStyle);
            SerializedProperty bindingProperty = bindingsProperty.GetArrayElementAtIndex(draggedBindingIndex);
            SerializedProperty nameProperty = bindingProperty.FindPropertyRelative("name");
            SerializedProperty componentProperty = bindingProperty.FindPropertyRelative("component");
            UnityEngine.Component component = componentProperty.objectReferenceValue as UnityEngine.Component;
            GetBindingColumnRects(floatingRect, out Rect indexRect, out Rect nameRect, out Rect componentRect, out Rect removeRect, out Rect dragHandleRect);
            GUI.Label(indexRect, dragInsertionIndex.ToString(), EditorStyles.miniLabel);
            GUI.Label(nameRect, nameProperty.stringValue, EditorStyles.textField);
            GUIContent componentContent = EditorGUIUtility.ObjectContent(component, component != null ? component.GetType() : typeof(UnityEngine.Component));
            GUI.Label(componentRect, componentContent, EditorStyles.objectField);
            GUI.Box(removeRect, new GUIContent("×"), EditorStyles.miniButton);
            GUI.Box(dragHandleRect, new GUIContent("≡"), EditorStyles.miniButton);
        }

        /// <summary>
        /// 按当前编辑器皮肤延迟创建拖动预览样式；九宫格边框能够在任意 Inspector 宽度下保持固定圆角半径。
        /// </summary>
        private void EnsureDragPreviewStyles()
        {
            bool useProSkin = EditorGUIUtility.isProSkin;
            if (bindingInsertionGapStyle != null && draggedRowBackgroundStyle != null && draggedRowShadowStyle != null && dragPreviewStylesUseProSkin == useProSkin)
                return;

            DestroyDragPreviewResources();
            dragPreviewStylesUseProSkin = useProSkin;
            bindingInsertionGapTexture = CreateRoundedPreviewTexture("UIKit Binding Insertion Gap", DrewBindColor);
            draggedRowBackgroundTexture = CreateRoundedPreviewTexture("UIKit Dragged Binding Background", useProSkin ? DraggedRowDarkBackgroundColor : DraggedRowLightBackgroundColor);
            draggedRowShadowTexture = CreateRoundedPreviewTexture("UIKit Dragged Binding Shadow", DraggedRowShadowColor);
            bindingInsertionGapStyle = CreateRoundedPreviewStyle(bindingInsertionGapTexture);
            draggedRowBackgroundStyle = CreateRoundedPreviewStyle(draggedRowBackgroundTexture);
            draggedRowShadowStyle = CreateRoundedPreviewStyle(draggedRowShadowTexture);
        }

        /// <summary>
        /// 创建带固定九宫格边界的无内容样式，使圆角纹理中间区域可以伸展而四角不会发生形变。
        /// </summary>
        /// <param name="texture">作为样式普通状态背景的圆角纹理。</param>
        /// <returns>可直接用于绘制任意宽度圆角块的 GUIStyle。</returns>
        private static GUIStyle CreateRoundedPreviewStyle(Texture2D texture)
        {
            GUIStyle style = new GUIStyle(GUIStyle.none);
            style.normal.background = texture;
            style.border = new RectOffset(DragPreviewCornerRadius + 1, DragPreviewCornerRadius + 1, DragPreviewCornerRadius + 1, DragPreviewCornerRadius + 1);
            return style;
        }

        /// <summary>
        /// 使用圆角矩形有符号距离场生成带一像素抗锯齿的临时纹理，纹理仅用于当前 Binder Inspector 的拖动预览。
        /// </summary>
        /// <param name="textureName">方便在 Unity 内存分析器中识别用途的纹理名称。</param>
        /// <param name="color">圆角块内部颜色与最终透明度。</param>
        /// <returns>已应用像素并设为不可读的临时圆角纹理。</returns>
        private static Texture2D CreateRoundedPreviewTexture(string textureName, Color color)
        {
            Texture2D texture = new Texture2D(DragPreviewTextureSize, DragPreviewTextureSize, TextureFormat.RGBA32, false);
            texture.name = textureName;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[DragPreviewTextureSize * DragPreviewTextureSize];
            Vector2 center = Vector2.one * (DragPreviewTextureSize * 0.5f);
            Vector2 roundedRectangleCore = Vector2.one * (DragPreviewTextureSize * 0.5f - DragPreviewCornerRadius);
            for (int y = 0; y < DragPreviewTextureSize; y++)
            {
                for (int x = 0; x < DragPreviewTextureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f) - center;
                    Vector2 cornerDistance = new Vector2(Mathf.Abs(point.x), Mathf.Abs(point.y)) - roundedRectangleCore;
                    Vector2 outsideDistance = new Vector2(Mathf.Max(cornerDistance.x, 0f), Mathf.Max(cornerDistance.y, 0f));
                    float signedDistance = outsideDistance.magnitude + Mathf.Min(Mathf.Max(cornerDistance.x, cornerDistance.y), 0f) - DragPreviewCornerRadius;
                    float coverage = Mathf.Clamp01(0.5f - signedDistance);
                    pixels[y * DragPreviewTextureSize + x] = new Color(color.r, color.g, color.b, color.a * coverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>
        /// 销毁当前 Inspector 创建的全部拖动预览纹理并清空样式引用，避免切换皮肤或关闭 Inspector 后遗留隐藏资源。
        /// </summary>
        private void DestroyDragPreviewResources()
        {
            if (bindingInsertionGapTexture != null)
                UnityEngine.Object.DestroyImmediate(bindingInsertionGapTexture);

            if (draggedRowBackgroundTexture != null)
                UnityEngine.Object.DestroyImmediate(draggedRowBackgroundTexture);

            if (draggedRowShadowTexture != null)
                UnityEngine.Object.DestroyImmediate(draggedRowShadowTexture);

            bindingInsertionGapTexture = null;
            draggedRowBackgroundTexture = null;
            draggedRowShadowTexture = null;
            bindingInsertionGapStyle = null;
            draggedRowBackgroundStyle = null;
            draggedRowShadowStyle = null;
        }

        /// <summary>
        /// 鼠标松开时一次性提交数组移动，按下 Escape 时取消预览；两种路径都会清理热控制权和动画缓存。
        /// </summary>
        private void CompleteBindingDrag(int dragControlId)
        {
            if (!IsBindingDragActive(dragControlId))
                return;

            UnityEngine.Event currentEvent = UnityEngine.Event.current;
            if (currentEvent.type == UnityEngine.EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                CancelBindingDrag();
                currentEvent.Use();
                Repaint();
                return;
            }

            if (currentEvent.rawType != UnityEngine.EventType.MouseUp)
                return;

            int sourceIndex = draggedBindingIndex;
            int targetIndex = dragInsertionIndex;
            CancelBindingDrag();
            if (sourceIndex != targetIndex)
            {
                bindingsProperty.MoveArrayElement(sourceIndex, targetIndex);
                GUI.changed = true;
            }

            currentEvent.Use();
            Repaint();
        }

        /// <summary>
        /// 验证拖动源、目标和 IMGUI 热控制权仍然有效；外部修改数组或控制权意外丢失时立即取消拖动预览。
        /// </summary>
        private void ValidateBindingDragState(int bindingCount, int dragControlId)
        {
            if (draggedBindingIndex < 0)
                return;

            if (draggedBindingIndex >= bindingCount || dragInsertionIndex < 0 || dragInsertionIndex >= bindingCount || GUIUtility.hotControl != dragControlId)
                CancelBindingDrag();
        }

        /// <summary>
        /// 判断当前 Inspector 是否持有本列表的拖动控制权，避免其他 IMGUI 控件的热状态误触发排序预览。
        /// </summary>
        private bool IsBindingDragActive(int dragControlId)
        {
            return draggedBindingIndex >= 0 && GUIUtility.hotControl == dragControlId;
        }

        /// <summary>
        /// 取消当前拖动并清除浮动行、目标空槽、动画位置和 IMGUI 热控制权，不修改绑定数组顺序。
        /// </summary>
        private void CancelBindingDrag()
        {
            if (draggedBindingIndex >= 0)
                GUIUtility.hotControl = 0;

            draggedBindingIndex = -1;
            dragInsertionIndex = -1;
            dragPointerPosition = Vector2.zero;
            dragPointerOffset = Vector2.zero;
            lastDragAnimationTime = 0d;
            animatedBindingRowYPositions.Clear();
        }

        /// <summary>
        /// 使用柔和绿色绘制批量注册按钮，并在点击后注册 Binder 层级内所有受支持的 Button 与 Text 组件。
        /// </summary>
        /// <param name="binder">当前 Inspector 正在编辑的 Binder。</param>
        private void DrawRegisterAllButton(UIComponentBinder binder)
        {
            Color previousBackgroundColor = GUI.backgroundColor;
            try
            {
                GUI.backgroundColor = RegisterButtonColor;
                if (!GUILayout.Button(new GUIContent("Register All Buttons And Texts", "Register every Button, TextMeshProUGUI and supported custom XButton under this Binder without creating duplicates."), GUILayout.Height(30f)))
                    return;
            }
            finally
            {
                GUI.backgroundColor = previousBackgroundColor;
            }

            int addedCount = RegisterAllButtonsAndTexts(binder);
            serializedObject.Update();
            bindingsProperty = serializedObject.FindProperty("bindings");
            Repaint();
            Debug.Log(addedCount > 0 ? $"[UIKit Binder] Registered {addedCount} Button/Text component(s) in '{binder.name}'." : $"[UIKit Binder] Every supported Button/Text component in '{binder.name}' is already registered.", binder);
        }

        /// <summary>
        /// 按层级遍历顺序注册全部 Button、TextMeshProUGUI 及未来的自定义 XButton，已有组件引用会被跳过且现有绑定不会被重排。
        /// </summary>
        /// <param name="binder">需要批量扫描的 Binder 根节点。</param>
        /// <returns>本次实际新增的绑定数量。</returns>
        private static int RegisterAllButtonsAndTexts(UIComponentBinder binder)
        {
            SerializedObject serializedBinder = new SerializedObject(binder);
            SerializedProperty serializedBindings = serializedBinder.FindProperty("bindings");
            HashSet<UnityEngine.Component> registeredComponents = new HashSet<UnityEngine.Component>();
            HashSet<string> registeredNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < serializedBindings.arraySize; index++)
            {
                SerializedProperty bindingProperty = serializedBindings.GetArrayElementAtIndex(index);
                UnityEngine.Component registeredComponent = bindingProperty.FindPropertyRelative("component").objectReferenceValue as UnityEngine.Component;
                string registeredName = bindingProperty.FindPropertyRelative("name").stringValue;
                if (registeredComponent != null)
                    registeredComponents.Add(registeredComponent);

                if (!string.IsNullOrEmpty(registeredName))
                    registeredNames.Add(registeredName);
            }

            UnityEngine.Component[] hierarchyComponents = binder.GetComponentsInChildren<UnityEngine.Component>(true);
            int addedCount = 0;
            Undo.RecordObject(binder, "Register All UI Buttons And Texts");
            for (int index = 0; index < hierarchyComponents.Length; index++)
            {
                UnityEngine.Component component = hierarchyComponents[index];
                if (!IsAutoRegisterComponent(component) || !registeredComponents.Add(component))
                    continue;

                string bindingName = CreateUniqueBindingName(component, registeredNames);
                int newIndex = serializedBindings.arraySize;
                serializedBindings.arraySize = newIndex + 1;
                SerializedProperty newBindingProperty = serializedBindings.GetArrayElementAtIndex(newIndex);
                newBindingProperty.FindPropertyRelative("name").stringValue = bindingName;
                newBindingProperty.FindPropertyRelative("component").objectReferenceValue = component;
                registeredNames.Add(bindingName);
                addedCount++;
            }

            if (addedCount <= 0)
                return 0;

            serializedBinder.ApplyModifiedProperties();
            EditorUtility.SetDirty(binder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder);
            return addedCount;
        }

        /// <summary>
        /// 判断组件是否属于自动注册范围；标准 Button 和 TextMeshProUGUI 直接按类型识别，自定义 XButton 则按约定类型名称兼容其非 Button 继承实现。
        /// </summary>
        /// <param name="component">层级扫描得到的候选组件。</param>
        /// <returns>组件应被自动加入 Binder 时返回 true。</returns>
        private static bool IsAutoRegisterComponent(UnityEngine.Component component)
        {
            if (component == null)
                return false;

            Type componentType = component.GetType();
            return component is UnityEngine.UI.Button || IsTypeOrSubclassOf(componentType, "TMPro.TextMeshProUGUI") || string.Equals(componentType.Name, "XButton", StringComparison.Ordinal);
        }

        /// <summary>
        /// 沿组件继承链匹配完整类型名称，使 Editor 程序集无需直接引用 TextMeshPro 也能识别 TextMeshProUGUI 及其 XText 子类。
        /// </summary>
        /// <param name="componentType">需要检查的实际组件类型。</param>
        /// <param name="expectedFullName">目标基类的完整类型名称。</param>
        /// <returns>当前类型或任意基类匹配目标完整名称时返回 true。</returns>
        private static bool IsTypeOrSubclassOf(Type componentType, string expectedFullName)
        {
            Type currentType = componentType;
            while (currentType != null)
            {
                if (string.Equals(currentType.FullName, expectedFullName, StringComparison.Ordinal))
                    return true;

                currentType = currentType.BaseType;
            }

            return false;
        }

        /// <summary>
        /// 直接使用 GameObject 原名构造批量注册名称，仅在名称已经存在时追加递增序号以维持 Binder 名称唯一性。
        /// </summary>
        /// <param name="component">需要生成绑定名称的组件。</param>
        /// <param name="registeredNames">包含已有名称以及本轮已经分配名称的集合。</param>
        /// <returns>不会与当前绑定表冲突的新绑定名称。</returns>
        private static string CreateUniqueBindingName(UnityEngine.Component component, HashSet<string> registeredNames)
        {
            string objectName = string.IsNullOrWhiteSpace(component.gameObject.name) ? "Component" : component.gameObject.name.Trim();
            string baseName = objectName;
            string candidate = baseName;
            int suffix = 2;
            while (registeredNames.Contains(candidate))
            {
                candidate = baseName + suffix;
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// 将绑定名称修改同步到对应组件所在 GameObject，并记录 Undo、Prefab 覆盖和脏标记以保证修改可以保存与撤销。
        /// </summary>
        /// <param name="component">绑定项当前引用的组件；空引用不会触发重命名。</param>
        /// <param name="bindingName">用户通过延迟文本框提交的新绑定名称。</param>
        private static void SynchronizeBoundObjectName(UnityEngine.Component component, string bindingName)
        {
            if (component == null || string.Equals(component.gameObject.name, bindingName, StringComparison.Ordinal))
                return;

            Undo.RecordObject(component.gameObject, "Rename Bound UI Object");
            component.gameObject.name = bindingName;
            EditorUtility.SetDirty(component.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component.gameObject);
        }

        /// <summary>
        /// 在 Inspector 中显示空引用、空名称和重复名称等会破坏生成代码的问题。
        /// </summary>
        private static void DrawValidationMessages(UIComponentBinder binder)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<UIComponentBinding> bindings = binder.Bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                UIComponentBinding binding = bindings[index];
                if (binding == null)
                {
                    EditorGUILayout.HelpBox($"Binding {index} is null.", MessageType.Error);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.Name))
                    EditorGUILayout.HelpBox($"Binding {index} has an empty name.", MessageType.Error);
                else if (!names.Add(binding.Name))
                    EditorGUILayout.HelpBox($"Binding name '{binding.Name}' is duplicated.", MessageType.Error);

                if (binding.Component == null)
                    EditorGUILayout.HelpBox($"Binding '{binding.Name}' does not reference a component.", MessageType.Error);
                else if (!binding.Component.transform.IsChildOf(binder.transform))
                    EditorGUILayout.HelpBox($"Binding '{binding.Name}' references a component outside this panel prefab.", MessageType.Error);
            }
        }
    }

    /// <summary>
    /// 为所有 Unity Component 的 Inspector 右键菜单提供 Add To Binder 命令，使组件引用无需手动拖入绑定表。
    /// </summary>
    public static class UIComponentBinderContextMenu
    {
        private const string AddToBinderMenuPath = "CONTEXT/Component/Add To Binder";

        /// <summary>
        /// 将右键目标组件添加到其父层级中最近的 UIComponentBinder，并自动生成不冲突的绑定名称。
        /// </summary>
        /// <param name="command">Unity 传入的组件右键菜单上下文。</param>
        [MenuItem(AddToBinderMenuPath, false, 1000)]
        private static void AddToBinder(MenuCommand command)
        {
            UnityEngine.Component component = command.context as UnityEngine.Component;
            if (component == null)
                return;

            UIComponentBinder binder = FindOwningBinder(component);
            if (binder == null)
            {
                EditorUtility.DisplayDialog("UIKit Binder", $"Component '{component.name}' is not inside a hierarchy whose root contains UIComponentBinder.", "OK");
                return;
            }

            if (component == binder)
            {
                EditorUtility.DisplayDialog("UIKit Binder", "UIComponentBinder cannot add itself to its component table.", "OK");
                return;
            }

            if (ContainsComponent(binder, component))
            {
                Debug.Log($"[UIKit Binder] Component '{GetComponentDisplayName(component)}' already exists in binder '{binder.name}' and was not added again.", binder);
                return;
            }

            string bindingName = CreateUniqueBindingName(binder, component);
            Undo.RecordObject(binder, "Add Component To UI Binder");
            SerializedObject serializedBinder = new SerializedObject(binder);
            SerializedProperty bindings = serializedBinder.FindProperty("bindings");
            int newIndex = bindings.arraySize;
            bindings.arraySize = newIndex + 1;
            SerializedProperty newBinding = bindings.GetArrayElementAtIndex(newIndex);
            newBinding.FindPropertyRelative("name").stringValue = bindingName;
            newBinding.FindPropertyRelative("component").objectReferenceValue = component;
            serializedBinder.ApplyModifiedProperties();
            EditorUtility.SetDirty(binder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder);
            Debug.Log($"[UIKit Binder] Added component '{GetComponentDisplayName(component)}' as binding '{bindingName}' to '{binder.name}'.", binder);
        }

        /// <summary>
        /// 仅当右键目标位于某个 Binder 层级中、不是 Binder 本身且尚未被添加时启用菜单命令。
        /// </summary>
        /// <param name="command">Unity 传入的组件右键菜单上下文。</param>
        /// <returns>当前组件可以安全添加时返回 true。</returns>
        [MenuItem(AddToBinderMenuPath, true)]
        private static bool ValidateAddToBinder(MenuCommand command)
        {
            UnityEngine.Component component = command.context as UnityEngine.Component;
            if (component == null)
                return false;

            UIComponentBinder binder = FindOwningBinder(component);
            return binder != null && component != binder && !ContainsComponent(binder, component);
        }

        /// <summary>
        /// 从目标组件开始向父层级查找最近的 UIComponentBinder，支持处于未激活状态的 Prefab 节点。
        /// </summary>
        private static UIComponentBinder FindOwningBinder(UnityEngine.Component component)
        {
            return component.GetComponentInParent<UIComponentBinder>(true);
        }

        /// <summary>
        /// 按 Unity 对象引用检查目标组件是否已经存在于绑定表中，避免同一组件以不同名称重复添加。
        /// </summary>
        private static bool ContainsComponent(UIComponentBinder binder, UnityEngine.Component component)
        {
            IReadOnlyList<UIComponentBinding> bindings = binder.Bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                UIComponentBinding binding = bindings[index];
                if (binding != null && binding.Component == component)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 直接使用 GameObject 原名生成绑定名，并仅在名称冲突时追加递增序号。
        /// </summary>
        private static string CreateUniqueBindingName(UIComponentBinder binder, UnityEngine.Component component)
        {
            string objectName = string.IsNullOrWhiteSpace(component.gameObject.name) ? "Component" : component.gameObject.name.Trim();
            string baseName = objectName;
            string candidate = baseName;
            int suffix = 2;

            while (ContainsBindingName(binder, candidate))
            {
                candidate = baseName + suffix;
                suffix++;
            }

            return candidate;
        }

        /// <summary>
        /// 检查绑定表中是否已经使用指定名称，比较过程区分大小写并与运行时 Binder 契约保持一致。
        /// </summary>
        private static bool ContainsBindingName(UIComponentBinder binder, string bindingName)
        {
            IReadOnlyList<UIComponentBinding> bindings = binder.Bindings;
            for (int index = 0; index < bindings.Count; index++)
            {
                UIComponentBinding binding = bindings[index];
                if (binding != null && string.Equals(binding.Name, bindingName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 构建同时包含层级对象名和组件类型的日志名称，便于定位重复或新增的目标组件。
        /// </summary>
        private static string GetComponentDisplayName(UnityEngine.Component component)
        {
            return $"{component.gameObject.name}.{component.GetType().Name}";
        }
    }
}
