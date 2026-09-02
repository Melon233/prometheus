using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Excel
{
    /// <summary>单张 Excel 风格数据表；每个资产只保存一张表的二维字符串数据。</summary>
    [CreateAssetMenu(fileName = "ExcelSheet", menuName = "Prometheus/Excel/Sheet")]
    public sealed class ExcelSheetAsset : ScriptableObject
    {
        [SerializeField] private string sheetName = "Sheet1";
        [SerializeField] private int columnCount = 4;
        [SerializeField] private List<ExcelColumnDefinition> columns = new List<ExcelColumnDefinition>();
        [SerializeField] private List<ExcelSheetRow> rows = new List<ExcelSheetRow>();

        /// <summary>获取表名。</summary>
        public string SheetName => sheetName;

        /// <summary>获取表格列数。</summary>
        public int ColumnCount => columnCount;

        /// <summary>获取表格行数。</summary>
        public int RowCount => rows.Count;

        /// <summary>获取全部列定义，列定义顺序与数据单元格顺序一致。</summary>
        public IReadOnlyList<ExcelColumnDefinition> Columns => columns;

        /// <summary>获取指定列定义。</summary>
        public ExcelColumnDefinition GetColumn(int index) { EnsureColumnDefinitions(); return columns[index]; }

        /// <summary>获取指定单元格文本；超出当前范围时返回空字符串。</summary>
        public string GetCell(int row, int column)
        {
            if (row < 0 || row >= rows.Count || column < 0 || column >= columnCount) return string.Empty;
            return rows[row].GetCell(column);
        }

        /// <summary>设置指定单元格文本；调用方应在编辑器中记录 Undo 并标记资产已修改。</summary>
        public void SetCell(int row, int column, string value)
        {
            EnsureSize(row + 1, column + 1);
            rows[row].SetCell(column, value ?? string.Empty);
        }

        /// <summary>调整表格尺寸并保留已有数据。</summary>
        public void EnsureSize(int minimumRows, int minimumColumns)
        {
            int targetColumns = Mathf.Max(1, Mathf.Max(columnCount, minimumColumns));
            EnsureColumnDefinitions(targetColumns);
            while (rows.Count < Mathf.Max(0, minimumRows)) rows.Add(new ExcelSheetRow(targetColumns));
            columnCount = targetColumns;
            for (int index = 0; index < rows.Count; index++) rows[index].EnsureSize(columnCount);
        }

        /// <summary>在指定位置插入一行。</summary>
        public void InsertRow(int index)
        {
            EnsureSize(0, columnCount);
            rows.Insert(Mathf.Clamp(index, 0, rows.Count), new ExcelSheetRow(columnCount));
        }

        /// <summary>删除指定行；至少保留一行数据。</summary>
        public void RemoveRow(int index)
        {
            if (rows.Count <= 1 || index < 0 || index >= rows.Count) return;
            rows.RemoveAt(index);
        }

        /// <summary>在指定位置插入一列。</summary>
        public void InsertColumn(int index)
        {
            int insertIndex = Mathf.Clamp(index, 0, columnCount);
            columnCount++;
            columns.Insert(insertIndex, new ExcelColumnDefinition(ColumnName(insertIndex)));
            for (int row = 0; row < rows.Count; row++) rows[row].InsertCell(insertIndex);
        }

        /// <summary>删除指定列；至少保留一列数据。</summary>
        public void RemoveColumn(int index)
        {
            if (columnCount <= 1 || index < 0 || index >= columnCount) return;
            columnCount--;
            EnsureColumnDefinitions();
            columns.RemoveAt(index);
            for (int row = 0; row < rows.Count; row++) rows[row].RemoveCell(index);
        }

        /// <summary>设置指定列的名称、类型和描述。</summary>
        public void SetColumn(int index, string name, ExcelCellType type, string description)
        {
            EnsureColumnDefinitions();
            columns[index].Set(name, type, description);
        }

        /// <summary>设置表名；空值会保留默认表名。</summary>
        public void SetSheetName(string value) { sheetName = string.IsNullOrWhiteSpace(value) ? "Sheet1" : value.Trim(); }

        /// <summary>将数据复制为 JSON 导出模型。</summary>
        public ExcelSheetJson ToJsonModel()
        {
            EnsureColumnDefinitions();
            ExcelSheetJson model = new ExcelSheetJson { sheetName = sheetName, columnCount = columnCount, headers = new List<ExcelColumnJson>(), rows = new List<string[]>() };
            for (int column = 0; column < columnCount; column++) model.headers.Add(columns[column].ToJsonModel());
            for (int row = 0; row < rows.Count; row++) model.rows.Add(rows[row].ToArray(columnCount));
            return model;
        }

        /// <summary>从 JSON 导入模型覆盖当前表格数据。</summary>
        public void ApplyJsonModel(ExcelSheetJson model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            SetSheetName(model.sheetName);
            columnCount = Mathf.Max(1, model.columnCount);
            columns.Clear();
            if (model.headers != null) for (int index = 0; index < model.headers.Count; index++) columns.Add(new ExcelColumnDefinition(model.headers[index]));
            rows.Clear();
            if (model.rows != null) for (int row = 0; row < model.rows.Count; row++) rows.Add(new ExcelSheetRow(model.rows[row], columnCount));
            EnsureSize(rows.Count, columnCount);
        }

        /// <summary>从 Luban 对象数组导入数据；对象属性名必须匹配列名称。</summary>
        public void ApplyLubanRows(IReadOnlyList<Dictionary<string, string>> sourceRows)
        {
            EnsureColumnDefinitions();
            rows.Clear();
            if (sourceRows != null) for (int row = 0; row < sourceRows.Count; row++)
            {
                ExcelSheetRow target = new ExcelSheetRow(columnCount);
                for (int column = 0; column < columnCount; column++) if (sourceRows[row].TryGetValue(columns[column].Name, out string value)) target.SetCell(column, value);
                rows.Add(target);
            }
            EnsureSize(rows.Count, columnCount);
        }

        /// <summary>为旧版本表格补齐默认列定义，并确保列数与定义数量一致。</summary>
        private void EnsureColumnDefinitions() { EnsureColumnDefinitions(columnCount); }

        /// <summary>按目标列数扩展或裁剪列定义。</summary>
        private void EnsureColumnDefinitions(int targetColumns)
        {
            while (columns.Count < targetColumns) columns.Add(new ExcelColumnDefinition(ColumnName(columns.Count)));
            if (columns.Count > targetColumns) columns.RemoveRange(targetColumns, columns.Count - targetColumns);
        }

        /// <summary>生成默认 Excel 列名。</summary>
        private static string ColumnName(int index)
        {
            string result = string.Empty;
            for (int value = index + 1; value > 0; value = (value - 1) / 26) result = (char)('A' + (value - 1) % 26) + result;
            return result;
        }
    }

    /// <summary>表格支持的基础单元格类型。</summary>
    public enum ExcelCellType { String, Integer, Float, Boolean, Long, Double }

    /// <summary>一列的表头元数据。</summary>
    [Serializable]
    public sealed class ExcelColumnDefinition
    {
        [SerializeField] private string name;
        [SerializeField] private ExcelCellType type;
        [SerializeField] private string description;

        public ExcelColumnDefinition(string name) { this.name = name; }
        public ExcelColumnDefinition(ExcelColumnJson source) { name = source.name; type = (ExcelCellType)source.type; description = source.description; }
        /// <summary>获取列名称。</summary>
        public string Name => name;
        /// <summary>获取列类型。</summary>
        public ExcelCellType Type => type;
        /// <summary>获取列描述。</summary>
        public string Description => description;
        /// <summary>更新列元数据。</summary>
        public void Set(string value, ExcelCellType cellType, string text) { name = string.IsNullOrWhiteSpace(value) ? "Column" : value; type = cellType; description = text ?? string.Empty; }
        /// <summary>转换为 JSON 表头模型。</summary>
        public ExcelColumnJson ToJsonModel() { return new ExcelColumnJson { name = name, type = (int)type, description = description }; }
    }

    /// <summary>表格的一行字符串数据。</summary>
    [Serializable]
    public sealed class ExcelSheetRow
    {
        [SerializeField] private List<string> cells = new List<string>();

        public ExcelSheetRow(int columnCount) { EnsureSize(columnCount); }
        public ExcelSheetRow(string[] source, int columnCount)
        {
            EnsureSize(columnCount);
            if (source != null) for (int index = 0; index < Mathf.Min(source.Length, columnCount); index++) cells[index] = source[index] ?? string.Empty;
        }

        public string GetCell(int index) { return index >= 0 && index < cells.Count ? cells[index] ?? string.Empty : string.Empty; }
        public void SetCell(int index, string value) { EnsureSize(index + 1); cells[index] = value ?? string.Empty; }
        public void EnsureSize(int count) { while (cells.Count < count) cells.Add(string.Empty); if (cells.Count > count) cells.RemoveRange(count, cells.Count - count); }
        public void InsertCell(int index) { cells.Insert(Mathf.Clamp(index, 0, cells.Count), string.Empty); }
        public void RemoveCell(int index) { if (index >= 0 && index < cells.Count) cells.RemoveAt(index); }
        public string[] ToArray(int columnCount) { string[] result = new string[columnCount]; for (int index = 0; index < columnCount; index++) result[index] = GetCell(index); return result; }
    }

    /// <summary>JSON 根对象；显式包裹二维数组以兼容 Unity JsonUtility。</summary>
    [Serializable]
    public sealed class ExcelSheetJson
    {
        public string sheetName;
        public int columnCount;
        public List<ExcelColumnJson> headers;
        public List<string[]> rows;
    }

    /// <summary>JSON 中的列定义结构。</summary>
    [Serializable]
    public sealed class ExcelColumnJson
    {
        public string name;
        public int type;
        public string description;
    }
}
