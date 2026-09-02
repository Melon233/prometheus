# Unity Excel 表格系统设计

## 目标

提供一个轻量的 Unity 编辑器表格工具，支持基础网格编辑和 JSON 文件交换。每张表是一个独立的 `ExcelSheetAsset`，可被其他配置系统以普通 ScriptableObject 方式引用。

## 数据模型

- `ExcelSheetAsset`：保存表名、列定义和行列表。
- `ExcelColumnDefinition`：保存每列的名称、基础类型和描述。
- `ExcelSheetRow`：保存一行字符串单元格。
- `ExcelSheetJson`：JSON 导入导出的根对象，包含 `sheetName`、`columns` 和 `rows`。

编辑器内部仍以字符串保存单元格，列定义负责声明 Luban 基础类型；导出时严格转换为 JSON 原生数字、布尔值或字符串，非法值会阻止导出并提示具体行列。

## 编辑器功能

配表工具、表格资产模型和 JSON 读写代码全部位于项目级 `Assets/Editor/ExcelSystem`，不进入运行时程序集。通过 `Prometheus/Excel Sheet` 打开窗口，或双击 `ExcelSheetAsset` 资产：

- 选择/创建表资产。
- 编辑表名和单元格文本。
- 增加、删除行和列。
- 使用 Undo 撤销单元格和结构修改。
- 导入 JSON 覆盖当前表。
- 导出当前表为格式化 JSON。

## JSON 格式

```json
[
  { "id": "item_001", "name": "Potion", "count": 10 }
]
```

导出的 JSON 采用 Luban 常用的单表对象数组形式；每行是一个对象，属性名来自列名称。当前支持 `string`、`int`、`long`、`float`、`double`、`bool` 基础类型。是否能被项目中的 Luban Runtime 直接加载，仍取决于 Luban Schema、生成代码和运行时要求的表文件包装格式；本工具不生成 Luban Bean 代码。
