# 配表管线（Luban）

> 状态：生效
> 适用范围：全部玩法配表。项目原有的自定义 Excel 配表工具（`Assets/Editor/ExcelSystem`）已移除，不再使用。

## 1. 目录职责

| 目录 | 内容 | 是否入库 |
| --- | --- | --- |
| `Tools/Luban/` | 导表工具与导表脚本 | 是 |
| `Tools/Luban/v4.10.2/Luban/` | Luban 4.10.2 本体（`Luban.dll` 及其依赖） | 是 |
| `Tools/Luban/gen.bat` / `gen.ps1` | 导表入口脚本 | 是 |
| `Data/Excel/` | 策划维护的 Excel 源文件 | 是 |
| `Data/Excel/luban.conf` | 导表配置：分组、schema 文件、目标 | 是 |
| `Data/Excel/Defines/` | 三张定义表：`__tables__` / `__beans__` / `__enums__` | 是 |
| `Data/Excel/Datas/` | 数据表，按业务域分子目录 | 是 |
| `Assets/Trd/Luban/` | Luban Unity 运行时（`Luban.Runtime` / `Luban.Editor` 程序集） | 是 |
| `Assets/Gen/Config/` | 程序集定义 `Prometheus.Config.Generated.asmdef` | 是 |
| `Assets/Gen/Config/Generated/` | 导表生成的 C# 代码 | 是 |
| `Assets/BundleResources/Table/` | 导表生成的二进制数据（`.bytes`），随资源包出包 | 是 |

`Assets/Gen/Config/Generated` 与 `Assets/BundleResources/Table` 都是**生成物**：不要手工编辑，任何修改都会在下次导表时被覆盖。

**Luban 每次导表都会清空 `outputCodeDir`**，因此 `.asmdef` 必须放在它的上一层（`Assets/Gen/Config/`），而不能和生成代码放在一起——放进去会被连带删除。

## 2. 导表

三种等价入口：

- Unity 编辑器菜单 `Prometheus/Luban/导出配表`
- 双击 `Tools/Luban/gen.bat`
- 命令行 `pwsh Tools/Luban/gen.ps1`

导表参数固定为 `-t client -c cs-bin -d bin`：客户端目标、C# 二进制读取代码、二进制数据。前置依赖只有 .NET SDK（`dotnet` 命令可用）。

导表失败时编辑器**不会刷新资源**，工程停留在上一份可用数据上，避免半份数据进入打包。

## 3. 表结构规范

### 3.1 定义表（`Data/Excel/Defines/`）

三张表的表头列集必须与 Luban 4.10.2 的内建 schema 完全一致，缺列会直接导表失败：

| 文件 | 列 |
| --- | --- |
| `__tables__.xlsx` | `full_name` `value_type` `read_schema_from_file` `input` `index` `mode` `group` `comment` `tags` `output` |
| `__enums__.xlsx` | `full_name` `flags` `unique` `group` `comment` `tags` + `*items{name, alias, value, comment, tags}` |
| `__beans__.xlsx` | `full_name` `parent` `valueType` `alias` `sep` `comment` `tags` `group` + `*fields{name, alias, type, group, comment, tags, variants}` |

**`*items` / `*fields` 这类多列子字段的列跨度由第一行的合并单元格声明**（例如 `__enums__.xlsx` 的 `H1:L1`）。取消合并会导致 Luban 只识别第一个子列，报「缺失列」。这是本管线最容易踩的坑，复制或新建定义表时务必保留合并区间。

**`full_name` 不要写 `topModule` 前缀。** `luban.conf` 的 `topModule` 已经是 `Prometheus.Config`；若 `full_name` 再写成 `Prometheus.Config.TbXxx`，两段会叠加成 `Prometheus.Config.Prometheus.Config`。`full_name` 只填类型名（`TbCharacterBase`、`ElementType`），数据表 `##type` 行同理只填 `ElementType` 而不是 `Prometheus.Config.ElementType`。

这个错误**不会在导表时报错**，只在 C# 引用生成类型时才暴露，而且 `Tables` 本身命名空间正确、只有 Row 与枚举被多包了一层，用 `var` 接收时完全看不出来。

### 3.2 数据表（`Data/Excel/Datas/`）

`read_schema_from_file = True`，因此字段定义写在数据文件自己的表头里，一张表一个文件，策划不需要同步维护 bean 定义。表头三行固定：

| 行 | 首列标记 | 内容 |
| --- | --- | --- |
| 第 1 行 | `##` | 字段名（camelCase） |
| 第 2 行 | `##type` | 字段类型 |
| 第 3 行 | `##` | 中文注释 |
| 第 4 行起 | 空 | 数据 |

首列为标记列，数据行首列留空。

**忽略列**：字段名以 `#` 开头的列不参与导出，用于放置策划自用的辅助信息（如 `#confidence` 数值置信度标记）。

### 3.3 类型写法

| 写法 | 说明 |
| --- | --- |
| `int` / `long` / `float` / `double` / `bool` / `string` | 基础类型 |
| `Prometheus.Config.XxxEnum` | 枚举，须在 `__enums__.xlsx` 中定义；单元格填枚举成员名 |
| `list,int` / `(list#sep=;),int` | 列表 |
| `map,int,string` | 字典 |
| `XxxBean` | 结构体，在 `__beans__.xlsx` 中定义 |

字段名后可追加 `#default=xxx` 指定默认值，例如 `priority#default=0`。

### 3.4 主键与表模式

`__tables__.xlsx` 的 `mode` 列决定表的组织方式：

| mode | index | 生成的访问方式 |
| --- | --- | --- |
| `map` | 单个字段 | `Get(key)` / `GetOrDefault(key)` / `DataMap` / `DataList` |
| `list` | 留空 | 仅 `DataList` |
| `one` | 留空 | 单行全局配置，直接访问字段 |

**Luban 不支持复合主键**。`index` 填两个字段（如 `curveId,level`）在 `map` 模式下报「是单主键表」，在 `list` 模式下会被当作两个各自唯一的索引而报主键重复。

因此逐级表、逐阶段表这类天然需要两个键的数据，采用 **`mode = list` 且不声明 `index`**，行布局保持「一行一级」（Excel 里最易编辑），查询索引在消费方加载时构建一次：

```csharp
// 在 System 的 AfterNew 中建一次复合键索引，之后 O(1) 查询。
private readonly Dictionary<(string, int), CharacterLevelCurveRow> levelCurve = new();

public override void AfterNew()
{
    foreach (CharacterLevelCurveRow row in Core.Config.Tables.TbCharacterLevelCurve.DataList)
        levelCurve[(row.CurveId, row.Level)] = row;
}
```

不要为了迁就单主键而给策划表加一列冗余的拼接 ID——那是把工具限制转嫁给填表的人。

## 4. 代码生成结果

顶层模块为 `Prometheus.Config`，生成物：

```text
Assets/Gen/Config/
 ├─ Prometheus.Config.Generated.asmdef  # 手工维护，不在生成目录内
 └─ Generated/
     ├─ Tables.cs                       # 表管理器，持有全部表实例
     └─ Prometheus/Config/
         ├─ <枚举名>.cs                  # 各枚举
         ├─ <记录名>Row.cs               # 单行记录类型
         └─ Tb<表名>.cs                  # 单表容器，提供 Get / DataList / DataMap
```

程序集 `Prometheus.Config.Generated` 只引用 `Luban.Runtime`。需要读配表的程序集引用它即可；当前 `Prometheus.Framework`（ConfigKit 持有 `Tables`）与 `Prometheus.Gameplay` 已引用。

## 5. 运行时加载

### 5.1 ConfigKit

配表由 `IConfigKit` 管理，静态入口 `Core.Config`，注册顺序紧随 `AssetKit`（表数据来自资源包）。

```csharp
public interface IConfigKit : IKitContract
{
    bool IsLoaded { get; }
    Prometheus.Config.Tables Tables { get; }
    void Load();
}
```

`ConfigKit` **直接持有生成的 `Tables`**。`ARCH-LAYER-005` 明确把 `Prometheus.Config.Generated` 排除在 `ARCH-LAYER-002` 之外：它是导表产物，只含只读数据类型，没有行为、没有依赖、不表达任何玩法规则，为它套一层端口只会让每个调用点都背上类型参数。

`ConfigKit` 的职责因此收敛为两件事：把 Luban 表名解析为资源地址（地址规则是 `AddressByFileName`，**表名即地址**，无需映射），以及通过 `Core.Asset` 读出字节流交给生成的构造函数。

> `Unload` 暂不提供。它只对「不重启进程重载配表」这一个调试场景有意义，当前没有调用方，先不引入没人用的 API；将来做配表热重载时再补，届时需要同时处理表资源句柄的归还与持表引用的失效。

### 5.2 加载时机

加载点在 `GameFlow.RunHotUpdateAsync` 的 `core.AfterNew()` 之后（`ARCH-CONFIG-001`）：

```csharp
Core.Config.Load();
```

选这里的原因是它恰好夹在两个条件之间——资源包刚刚就绪（可以读 `.bytes`），而全部玩法 System 还没有构造（不会有人读到半份配置）。配表属于 App 段，跨越登录与全部世界，不随会话重建。

### 5.3 读表

需要读表的程序集引用 `Prometheus.Config.Generated`，直接经静态入口读取，不需要类型参数，也不需要各系统自行缓存一份引用：

```csharp
// 按主键读一行。
ReactionMatrixRow row = Core.Config.Tables.TbReactionMatrix.Get("VaporizeForward");

// 遍历整张表。
foreach (ReactionMatrixRow r in Core.Config.Tables.TbReactionMatrix.DataList) { }
```

访问 `Tables` 时若尚未加载会抛异常而不是返回空值：读不到表是时序或打包错误，不是可恢复状态。

> 生成代码的命名空间是 `Prometheus.Config`，而项目代码位于 `Xuan.Prometheus.*` 下，直接写 `Prometheus.Config.Tables` 会被解析成 `Xuan.Prometheus.Config`。在 `Xuan.Prometheus` 命名空间内引用时必须写 `global::Prometheus.Config`，或按 `ConfigKit.cs` 的做法用 `using Cfg = global::Prometheus.Config;` 起别名。

## 6. 当前已有的表

## 6. 当前已有的表

### 6.1 战斗

| 表 | 数据文件 | mode | 行数 | 状态 |
| --- | --- | --- | --- | --- |
| `TbGaugeStrength` | `Combat/gauge_strength.xlsx` | map | 3 | 数值已确认，可直接使用 |
| `TbIcdGroup` | `Combat/icd_group.xlsx` | map | 2 | 数值已确认，可直接使用 |
| `TbReactionMatrix` | `Combat/reaction_matrix.xlsx` | map | 31 | 12 行已确认；19 行标 `NeedsReview`，需实机校准。`producedAura` 列声明本行写入哪个伪元素，`effectId` 列声明产物 Effect，两者都被代码直接读取 |
| `TbReactionLevelCoefficient` | `Combat/reaction_level_coefficient.xlsx` | map | 90 | 6 个实机锚点标 `NeedsReview`，其余 84 行按单调三次插值填充标 `Placeholder` |
| `TbPseudoElementState` | `Combat/pseudo_element_state.xlsx` | map | 3 | 冻结时长有闭式解；原激化与草原核时长为 `Placeholder` |
| `TbAttackSegment` | `Combat/attack_segment.xlsx` | list | 22 | 每段攻击的战斗数据与体力消耗；**不含时序**，时序由动画事件承载。键 `(talentId, stageIndex, windowIndex)`，无复合主键故用 list |

字段语义与数值依据见 [04A 反应矩阵配表](../Design/Combat/04A_反应矩阵配表.md)。尚未建表：`ReactionLevelCoefficient`、`AttributeSlot`、`EnemyAttribute`、`TalentMultiplier`，见该文档与 [05A 属性位清单](../Design/Combat/05A_属性位清单.md)。

### 6.2 角色养成

| 表 | 数据文件 | mode | 行数 | 状态 |
| --- | --- | --- | --- | --- |
| `TbCharacterBase` | `Growth/character_base.xlsx` | map | 1 | **占位数据**，仅 `Yefa` 一行 |
| `TbCharacterLevelCurve` | `Growth/character_level_curve.xlsx` | list | 90 | **占位曲线**，需实机校准 |
| `TbCharacterAscension` | `Growth/character_ascension.xlsx` | list | 6 | 阶段结构已确认；属性加值与材料为占位 |
| `TbMaterial` | `Growth/material.xlsx` | map | 20 | 结构已确认；具体材料清单为占位样例 |

**这三张表当前是占位数据，禁止按其数值做任何平衡判断。** 已确认的只有结构部分：

- `TbCharacterAscension` 的 `requiredLevel` / `unlockLevelCap` 六阶段序列（20→40→50→60→70→80→90）与 `secondaryAttrTier` 档位序列（0,1,2,2,3,4）来自策划案，属规则而非数值。
- 其余字段（基础属性、系数、经验、摩拉、材料数量）全部标 `#confidence = Placeholder`，需实机反解后替换。

字段语义见 [Growth/01 角色等级与突破](../Design/Growth/01_角色等级与突破.md) 第 5 节。

## 7. 新增一张表的步骤

1. 在 `Data/Excel/Datas/<业务域>/` 新建数据表 `.xlsx`，按 3.2 写好三行表头与数据。
2. 在 `Data/Excel/Defines/__tables__.xlsx` 追加一行：`full_name` 填 `Prometheus.Config.Tb<表名>`，`value_type` 填 `<记录名>Row`，`read_schema_from_file` 填 `True`，`input` 填相对 `Datas` 的路径，`index` 填主键字段名，`group` 填 `c`。
3. 若用到新枚举，在 `__enums__.xlsx` 追加，注意保持 `H1:L1` 合并。
4. 执行导表，确认生成代码与 `.bytes` 均出现。
5. 在对应设计文档中登记该表的字段语义与数值来源。

新增表不需要改动 `ConfigKit`：它按名字加载，表的增减只体现在生成的 `Tables` 构造函数里。
