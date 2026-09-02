using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Xuan.Prometheus.Excel.Editor
{
    /// <summary>Excel 风格单表编辑器，负责网格编辑和 JSON 文件交换。</summary>
    public sealed class ExcelSheetWindow : EditorWindow
    {
        private const float RowHeaderWidth = 52f;
        private const float CellWidth = 150f;
        private const float CellHeight = 22f;
        private const float DescriptionHeight = 42f;
        private ExcelSheetAsset sheet;
        private Vector2 scrollPosition;
        private string jsonFileName = "Sheet.json";

        /// <summary>通过菜单打开空白表格编辑器。</summary>
        [MenuItem("Prometheus/Excel Sheet")]
        public static void Open() { GetWindow<ExcelSheetWindow>("Excel Sheet"); }

        /// <summary>选中 ExcelSheetAsset 时从资源上下文打开编辑器。</summary>
        [OnOpenAsset]
        private static bool OpenAsset(int instanceId, int line)
        {
            ExcelSheetAsset asset = EditorUtility.EntityIdToObject(instanceId) as ExcelSheetAsset;
            if (asset == null) return false;
            ExcelSheetWindow window = GetWindow<ExcelSheetWindow>("Excel Sheet");
            window.sheet = asset;
            window.Show();
            return true;
        }

        /// <summary>绘制表选择、行列操作、网格和 JSON 操作按钮。</summary>
        private void OnGUI()
        {
            DrawToolbar();
            if (sheet == null)
            {
                EditorGUILayout.HelpBox("请拖入一个 ExcelSheetAsset，或通过 Assets/Create/Prometheus/Excel/Sheet 创建表格。", MessageType.Info);
                return;
            }
            sheet.EnsureSize(Mathf.Max(1, sheet.RowCount), Mathf.Max(1, sheet.ColumnCount));
            DrawGrid();
        }

        /// <summary>绘制资产选择和文件导入导出工具栏。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUI.BeginChangeCheck();
            sheet = (ExcelSheetAsset)EditorGUILayout.ObjectField(sheet, typeof(ExcelSheetAsset), false, GUILayout.Width(220f));
            if (EditorGUI.EndChangeCheck()) Repaint();
            if (GUILayout.Button("新建表", EditorStyles.toolbarButton, GUILayout.Width(60f))) CreateSheet();
            using (new EditorGUI.DisabledScope(sheet == null))
            {
                if (GUILayout.Button("加行", EditorStyles.toolbarButton, GUILayout.Width(45f))) ModifySheet(() => sheet.InsertRow(sheet.RowCount));
                if (GUILayout.Button("删行", EditorStyles.toolbarButton, GUILayout.Width(45f))) ModifySheet(() => sheet.RemoveRow(sheet.RowCount - 1));
                if (GUILayout.Button("加列", EditorStyles.toolbarButton, GUILayout.Width(45f))) ModifySheet(() => sheet.InsertColumn(sheet.ColumnCount));
                if (GUILayout.Button("删列", EditorStyles.toolbarButton, GUILayout.Width(45f))) ModifySheet(() => sheet.RemoveColumn(sheet.ColumnCount - 1));
                if (GUILayout.Button("导入 JSON", EditorStyles.toolbarButton, GUILayout.Width(75f))) ImportJson();
                if (GUILayout.Button("导出 JSON", EditorStyles.toolbarButton, GUILayout.Width(75f))) ExportJson();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>绘制可滚动的列头、行头和单元格输入框。</summary>
        private void DrawGrid()
        {
            EditorGUILayout.LabelField("表名", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField(sheet.SheetName);
            if (EditorGUI.EndChangeCheck()) ModifySheet(() => sheet.SetSheetName(name));
            float headerHeight = CellHeight * 3f + DescriptionHeight;
            float contentWidth = RowHeaderWidth + sheet.ColumnCount * CellWidth;
            float contentHeight = headerHeight + sheet.RowCount * CellHeight;
            Rect viewport = GUILayoutUtility.GetRect(1f, 100000f, 1f, 100000f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            scrollPosition = GUI.BeginScrollView(viewport, scrollPosition, new Rect(0f, 0f, contentWidth, contentHeight));
            DrawHeaderLabels(headerHeight);
            for (int column = 0; column < sheet.ColumnCount; column++) DrawColumnHeader(column, headerHeight);
            for (int row = 0; row < sheet.RowCount; row++)
            {
                float rowY = headerHeight + row * CellHeight;
                GUI.Label(new Rect(0f, rowY, RowHeaderWidth, CellHeight), (row + 1).ToString(), EditorStyles.toolbarButton);
                for (int column = 0; column < sheet.ColumnCount; column++) DrawCell(row, column, rowY);
            }
            GUI.EndScrollView();
        }

        /// <summary>绘制表头左侧标签，标签高度与各表头控件严格一致。</summary>
        private static void DrawHeaderLabels(float headerHeight)
        {
            GUI.Label(new Rect(0f, 0f, RowHeaderWidth, CellHeight), "列", EditorStyles.toolbarButton);
            GUI.Label(new Rect(0f, CellHeight, RowHeaderWidth, CellHeight), "名称", EditorStyles.toolbarButton);
            GUI.Label(new Rect(0f, CellHeight * 2f, RowHeaderWidth, CellHeight), "类型", EditorStyles.toolbarButton);
            GUI.Label(new Rect(0f, CellHeight * 3f, RowHeaderWidth, headerHeight - CellHeight * 3f), "描述", EditorStyles.toolbarButton);
        }

        /// <summary>使用显式坐标绘制列名、类型和描述，确保与该列数据框完全对齐。</summary>
        private void DrawColumnHeader(int column, float headerHeight)
        {
            ExcelColumnDefinition definition = sheet.GetColumn(column);
            float x = RowHeaderWidth + column * CellWidth;
            GUI.Label(new Rect(x, 0f, CellWidth, CellHeight), ColumnName(column), EditorStyles.toolbarButton);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUI.TextField(new Rect(x, CellHeight, CellWidth, CellHeight), definition.Name);
            ExcelCellType type = (ExcelCellType)EditorGUI.EnumPopup(new Rect(x, CellHeight * 2f, CellWidth, CellHeight), definition.Type);
            string description = EditorGUI.TextArea(new Rect(x, CellHeight * 3f, CellWidth, headerHeight - CellHeight * 3f), definition.Description);
            if (EditorGUI.EndChangeCheck()) ModifySheet(() => sheet.SetColumn(column, name, type, description));
        }

        /// <summary>绘制单个可编辑单元格，并在内容变化时记录 Undo。</summary>
        private void DrawCell(int row, int column, float rowY)
        {
            float x = RowHeaderWidth + column * CellWidth;
            EditorGUI.BeginChangeCheck();
            string value = EditorGUI.TextField(new Rect(x, rowY, CellWidth, CellHeight), sheet.GetCell(row, column));
            if (!EditorGUI.EndChangeCheck()) return;
            Undo.RecordObject(sheet, "Edit Excel Cell");
            sheet.SetCell(row, column, value);
            EditorUtility.SetDirty(sheet);
        }

        /// <summary>创建一个新的持久化表资产。</summary>
        private void CreateSheet()
        {
            string path = EditorUtility.SaveFilePanelInProject("创建 Excel 表", "ExcelSheet", "asset", "请选择表资产路径", "Assets");
            if (string.IsNullOrEmpty(path)) return;
            ExcelSheetAsset asset = CreateInstance<ExcelSheetAsset>();
            asset.EnsureSize(8, 4);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            sheet = asset;
            Selection.activeObject = asset;
        }

        /// <summary>导出当前表为格式化 JSON 文件。</summary>
        private void ExportJson()
        {
            string path = EditorUtility.SaveFilePanel("导出 Excel JSON", Application.dataPath, jsonFileName, "json");
            if (string.IsNullOrEmpty(path)) return;
            jsonFileName = Path.GetFileName(path);
            File.WriteAllText(path, BuildLubanJson());
            AssetDatabase.Refresh();
        }

        /// <summary>从 JSON 文件读取表名、列数和所有单元格。</summary>
        private void ImportJson()
        {
            string path = EditorUtility.OpenFilePanel("导入 Excel JSON", Application.dataPath, "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Undo.RecordObject(sheet, "Import Excel JSON");
                ImportJsonText(File.ReadAllText(path));
                EditorUtility.SetDirty(sheet);
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("JSON 导入失败", exception.Message, "确定");
            }
        }

        /// <summary>生成 Luban 风格的对象数组 JSON，每行对象属性名来自列名称。</summary>
        private string BuildLubanJson()
        {
            JArray rows = new JArray();
            for (int row = 0; row < sheet.RowCount; row++)
            {
                JObject item = new JObject();
                for (int column = 0; column < sheet.ColumnCount; column++)
                {
                    ExcelColumnDefinition definition = sheet.GetColumn(column);
                    if (string.IsNullOrWhiteSpace(definition.Name)) throw new InvalidOperationException($"第 {column + 1} 列缺少字段名称。");
                    item[definition.Name] = ParseLubanValue(sheet.GetCell(row, column), definition.Type, row, column);
                }
                rows.Add(item);
            }
            return rows.ToString(Formatting.Indented);
        }

        /// <summary>将单元格文本按 Luban 基础类型转换为 JSON 原生值。</summary>
        private static JToken ParseLubanValue(string value, ExcelCellType type, int row, int column)
        {
            string text = value ?? string.Empty;
            if (type == ExcelCellType.String) return text;
            if (type == ExcelCellType.Boolean)
            {
                if (string.IsNullOrWhiteSpace(text)) return false;
                if (bool.TryParse(text, out bool booleanValue)) return booleanValue;
                if (text == "1") return true;
                if (text == "0") return false;
                throw new FormatException($"第 {row + 1} 行第 {column + 1} 列的值 '{text}' 不是合法 bool。");
            }
            if (type == ExcelCellType.Integer)
            {
                if (string.IsNullOrWhiteSpace(text)) return 0;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integerValue)) return integerValue;
                throw new FormatException($"第 {row + 1} 行第 {column + 1} 列的值 '{text}' 不是合法 int。");
            }
            if (type == ExcelCellType.Long)
            {
                if (string.IsNullOrWhiteSpace(text)) return 0L;
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longValue)) return longValue;
                throw new FormatException($"第 {row + 1} 行第 {column + 1} 列的值 '{text}' 不是合法 long。");
            }
            if (type == ExcelCellType.Float)
            {
                if (string.IsNullOrWhiteSpace(text)) return 0f;
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue)) return floatValue;
                throw new FormatException($"第 {row + 1} 行第 {column + 1} 列的值 '{text}' 不是合法 float。");
            }
            if (string.IsNullOrWhiteSpace(text)) return 0d;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue)) return doubleValue;
            throw new FormatException($"第 {row + 1} 行第 {column + 1} 列的值 '{text}' 不是合法 double。");
        }

        /// <summary>兼容导入 Luban 对象数组和旧版编辑器包装 JSON。</summary>
        private void ImportJsonText(string json)
        {
            JToken root = JToken.Parse(json);
            if (root is JObject legacy && legacy["rows"] != null)
            {
                sheet.ApplyJsonModel(JsonUtility.FromJson<ExcelSheetJson>(json));
                return;
            }
            JArray array = root as JArray ?? (root["data"] as JArray) ?? (root["rows"] as JArray);
            if (array == null) throw new InvalidOperationException("JSON 根节点必须是 Luban 对象数组，或包含 data/rows 数组。");
            List<Dictionary<string, string>> imported = new List<Dictionary<string, string>>();
            foreach (JObject item in array)
            {
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (JProperty property in item.Properties()) values[property.Name] = property.Value.Type == JTokenType.String ? property.Value.Value<string>() : property.Value.ToString(Formatting.None);
                imported.Add(values);
            }
            sheet.ApplyLubanRows(imported);
        }

        /// <summary>执行表格变更并统一记录撤销与资产脏标记。</summary>
        private void ModifySheet(Action mutation)
        {
            Undo.RecordObject(sheet, "Modify Excel Sheet");
            mutation();
            EditorUtility.SetDirty(sheet);
            Repaint();
        }

        /// <summary>将列索引转换为 Excel 风格字母列名。</summary>
        private static string ColumnName(int index)
        {
            string result = string.Empty;
            for (int value = index + 1; value > 0; value = (value - 1) / 26) result = (char)('A' + (value - 1) % 26) + result;
            return result;
        }
    }
}
