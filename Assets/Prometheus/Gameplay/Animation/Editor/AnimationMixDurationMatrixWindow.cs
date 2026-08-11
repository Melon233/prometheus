using System;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Editor
{
    /// <summary>以完整行列矩阵编辑 AnimationLibrary 的 MixDuration，并把默认值单元格保持为稀疏存储。</summary>
    public sealed class AnimationMixDurationMatrixWindow : EditorWindow
    {
        private const float RowLabelWidth = 110f;
        private const float CellHorizontalPadding = 4f;
        private const float MinimumCellWidth = 34f;
        private const float CellHeight = 22f;
        private const float HeaderHeight = 104f;

        private static readonly AnimationSemantic[] Semantics = (AnimationSemantic[])Enum.GetValues(typeof(AnimationSemantic));
        private static readonly Color OverrideFieldBackgroundColor = new Color(0.48f, 0.82f, 0.55f, 1f);

        [NonSerialized] private GUIStyle headerStyle;
        private float cellWidth;
        private Vector2 scrollPosition;
        private AnimationLibrary selectedLibrary;

        /// <summary>从 Tools 菜单打开矩阵窗口，并优先使用 Project 当前选中的 AnimationLibrary。</summary>
        [MenuItem("Tools/Prometheus/Animation/Open MixDuration Matrix")]
        private static void OpenFromMenu()
        {
            Open(Selection.activeObject as AnimationLibrary);
        }

        /// <summary>打开或聚焦矩阵窗口，并绑定指定动画库。</summary>
        public static void Open(AnimationLibrary library)
        {
            AnimationMixDurationMatrixWindow window = GetWindow<AnimationMixDurationMatrixWindow>();
            window.titleContent = new GUIContent("MixDuration Matrix");
            window.minSize = new Vector2(680f, 360f);
            window.selectedLibrary = library;
            window.Show();
            window.Focus();
        }

        /// <summary>在域重载后清空非序列化布局缓存，等待首次 OnGUI 在 EditorStyles 可用时重建。</summary>
        private void OnEnable()
        {
            headerStyle = null;
            cellWidth = 0f;
        }

        /// <summary>绘制动画库选择、默认值工具栏和完整有向过渡矩阵。</summary>
        private void OnGUI()
        {
            EnsureGuiResources();
            DrawToolbar();
            if (selectedLibrary == null)
            {
                EditorGUILayout.HelpBox("请选择一个 AnimationLibrary，矩阵的行表示源动画，列表示目标动画。", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox("None 表示 Setup Pose。浅绿色单元格是显式覆盖值；把单元格改为默认值会删除覆盖并恢复稀疏存储。", MessageType.None);
            DrawMatrix(selectedLibrary.MixDurationMatrix);
        }

        /// <summary>延迟创建依赖 EditorStyles 的 GUIStyle 与列宽，避免 EditorWindow 构造和域重载阶段访问未初始化皮肤。</summary>
        private void EnsureGuiResources()
        {
            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(EditorStyles.miniLabel);
                headerStyle.alignment = TextAnchor.MiddleCenter;
                headerStyle.wordWrap = false;
                headerStyle.fontStyle = FontStyle.Bold;
            }
            if (cellWidth <= 0f) RecalculateCellWidth();
        }

        /// <summary>绘制资源选择器、默认 MixDuration、覆盖数量和安全清理按钮。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            AnimationLibrary newLibrary = (AnimationLibrary)EditorGUILayout.ObjectField(selectedLibrary, typeof(AnimationLibrary), false, GUILayout.MinWidth(220f));
            if (EditorGUI.EndChangeCheck())
            {
                selectedLibrary = newLibrary;
                scrollPosition = Vector2.zero;
                GUI.FocusControl(null);
            }
            if (selectedLibrary == null)
            {
                EditorGUILayout.EndHorizontal();
                return;
            }
            AnimationMixDurationMatrix matrix = selectedLibrary.MixDurationMatrix;
            GUILayout.Space(8f);
            GUILayout.Label("默认时长", GUILayout.Width(56f));
            EditorGUI.BeginChangeCheck();
            float defaultDuration = EditorGUILayout.FloatField(matrix.DefaultDuration, GUILayout.Width(64f));
            if (EditorGUI.EndChangeCheck())
            {
                RecordChange("Change Default MixDuration");
                matrix.DefaultDuration = defaultDuration;
                MarkLibraryDirty();
            }
            GUILayout.Space(8f);
            GUILayout.Label($"覆盖 {matrix.OverrideCount}", GUILayout.Width(70f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清除全部覆盖", EditorStyles.toolbarButton, GUILayout.Width(92f))) ClearAllOverrides(matrix);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>在双向滚动区域中绘制源语义乘目标语义的完整矩阵。</summary>
        private void DrawMatrix(AnimationMixDurationMatrix matrix)
        {
            float tableWidth = RowLabelWidth + Semantics.Length * cellWidth;
            float tableHeight = HeaderHeight + Semantics.Length * CellHeight;
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, true, true);
            Rect tableRect = GUILayoutUtility.GetRect(tableWidth, tableHeight, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
            DrawCornerHeader(tableRect);
            for (int columnIndex = 0; columnIndex < Semantics.Length; columnIndex++) DrawColumnHeader(tableRect, columnIndex);
            for (int rowIndex = 0; rowIndex < Semantics.Length; rowIndex++) DrawRow(matrix, tableRect, rowIndex);
            EditorGUILayout.EndScrollView();
        }

        /// <summary>绘制左上角方向说明，明确矩阵的行列含义。</summary>
        private void DrawCornerHeader(Rect tableRect)
        {
            Rect cornerRect = new Rect(tableRect.x, tableRect.y, RowLabelWidth, HeaderHeight);
            EditorGUI.DrawRect(cornerRect, new Color(0.18f, 0.18f, 0.18f, 1f));
            GUI.Label(cornerRect, "From ↓ / To →", headerStyle);
        }

        /// <summary>绘制一个目标动画语义列头。</summary>
        private void DrawColumnHeader(Rect tableRect, int columnIndex)
        {
            Rect headerRect = new Rect(tableRect.x + RowLabelWidth + columnIndex * cellWidth, tableRect.y, cellWidth, HeaderHeight);
            EditorGUI.DrawRect(headerRect, columnIndex % 2 == 0 ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f));
            DrawVerticalLabel(headerRect, Semantics[columnIndex].ToString());
        }

        /// <summary>绘制一个源动画语义行和该行的全部目标单元格。</summary>
        private void DrawRow(AnimationMixDurationMatrix matrix, Rect tableRect, int rowIndex)
        {
            AnimationSemantic from = Semantics[rowIndex];
            float rowY = tableRect.y + HeaderHeight + rowIndex * CellHeight;
            Rect labelRect = new Rect(tableRect.x, rowY, RowLabelWidth, CellHeight);
            EditorGUI.DrawRect(labelRect, rowIndex % 2 == 0 ? new Color(0.22f, 0.22f, 0.22f, 1f) : new Color(0.25f, 0.25f, 0.25f, 1f));
            GUI.Label(labelRect, from.ToString(), headerStyle);
            for (int columnIndex = 0; columnIndex < Semantics.Length; columnIndex++) DrawCell(matrix, tableRect, rowIndex, columnIndex);
        }

        /// <summary>绘制一个可编辑单元格，并将等于默认值的输入转换为未覆盖状态。</summary>
        private void DrawCell(AnimationMixDurationMatrix matrix, Rect tableRect, int rowIndex, int columnIndex)
        {
            AnimationSemantic from = Semantics[rowIndex];
            AnimationSemantic to = Semantics[columnIndex];
            bool hasOverride = matrix.TryGetOverride(from, to, out float overrideDuration);
            float currentDuration = hasOverride ? overrideDuration : matrix.DefaultDuration;
            Rect cellRect = new Rect(tableRect.x + RowLabelWidth + columnIndex * cellWidth, tableRect.y + HeaderHeight + rowIndex * CellHeight, cellWidth, CellHeight);
            EditorGUI.DrawRect(cellRect, hasOverride ? new Color(0.22f, 0.42f, 0.28f, 0.9f) : GetDefaultCellColor(rowIndex, columnIndex));
            Rect fieldRect = new Rect(cellRect.x + 2f, cellRect.y + 1f, cellRect.width - 4f, cellRect.height - 2f);
            Color originalBackgroundColor = GUI.backgroundColor;
            if (hasOverride) GUI.backgroundColor = OverrideFieldBackgroundColor;
            EditorGUI.BeginChangeCheck();
            float newDuration = EditorGUI.FloatField(fieldRect, currentDuration);
            GUI.backgroundColor = originalBackgroundColor;
            if (!EditorGUI.EndChangeCheck()) return;
            newDuration = Mathf.Max(0f, newDuration);
            RecordChange($"Change MixDuration {from} To {to}");
            if (Mathf.Approximately(newDuration, matrix.DefaultDuration)) matrix.RemoveMixDuration(from, to);
            else matrix.SetMixDuration(from, to, newDuration);
            MarkLibraryDirty();
        }

        /// <summary>按照当前 Editor 数字输入框字体和内边距计算刚好容纳 0.00 的矩阵列宽。</summary>
        private void RecalculateCellWidth()
        {
            float numberFieldWidth = EditorStyles.numberField.CalcSize(new GUIContent("0.00")).x;
            cellWidth = Mathf.Max(MinimumCellWidth, Mathf.Ceil(numberFieldWidth + CellHorizontalPadding));
        }

        /// <summary>将目标动画语义逆时针旋转九十度，使窄列仍能完整显示列标题。</summary>
        private void DrawVerticalLabel(Rect headerRect, string label)
        {
            Matrix4x4 originalMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-90f, headerRect.center);
            Rect rotatedRect = new Rect(headerRect.center.x - HeaderHeight * 0.5f, headerRect.center.y - cellWidth * 0.5f, HeaderHeight, cellWidth);
            GUI.Label(rotatedRect, label, headerStyle);
            GUI.matrix = originalMatrix;
        }

        /// <summary>返回未覆盖单元格的棋盘底色，方便沿行列追踪数值。</summary>
        private static Color GetDefaultCellColor(int rowIndex, int columnIndex)
        {
            return (rowIndex + columnIndex) % 2 == 0 ? new Color(0.31f, 0.31f, 0.31f, 1f) : new Color(0.34f, 0.34f, 0.34f, 1f);
        }

        /// <summary>记录动画库 Undo，使矩阵的每次编辑都可以由用户撤销。</summary>
        private void RecordChange(string operationName)
        {
            Undo.RecordObject(selectedLibrary, operationName);
        }

        /// <summary>标记动画库资源已修改并立即刷新窗口显示。</summary>
        private void MarkLibraryDirty()
        {
            EditorUtility.SetDirty(selectedLibrary);
            Repaint();
        }

        /// <summary>经用户二次确认后删除全部覆盖单元格，并保留当前默认时长。</summary>
        private void ClearAllOverrides(AnimationMixDurationMatrix matrix)
        {
            if (matrix.OverrideCount == 0) return;
            if (!EditorUtility.DisplayDialog("清除 MixDuration 覆盖", $"确定清除 {selectedLibrary.name} 的 {matrix.OverrideCount} 个覆盖单元格吗？", "清除", "取消")) return;
            RecordChange("Clear MixDuration Overrides");
            matrix.ClearOverrides();
            MarkLibraryDirty();
        }
    }
}
