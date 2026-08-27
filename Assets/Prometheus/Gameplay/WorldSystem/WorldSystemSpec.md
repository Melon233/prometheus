# 世界系统（World System）

## 概述

世界系统负责大世界相关逻辑，核心是**八种兴趣点（POI）的生命周期管理**：

- 传送锚点
- 七天神像
- 宝箱
- 神瞳
- 采集物
- 副本
- 地图 Boss
- 怪物营地

世界以 `(0, 0, 0)` 为原点，将分布在世界中的内容按**网格**分割，每个格子边长为 `100m`（可配置）。

## 数据结构

### PoiConfig —— 兴趣点配置（基类）

| 字段 | 类型 | 说明 |
|------|------|------|
| `PoiId` | `string` | 唯一 id，格式为 `网格坐标_局部id`，如 `1x1_1`、`12x345_1`（局部 id 从 1 开始） |
| `PoiType` | `enum` | 兴趣点枚举，即八种兴趣点之一 |
| `StatueConfig` | `StatueConfig` | 七天神像配置 |
| `TeleAnchorConfig` | `TeleAnchorConfig` | 传送锚点配置 |
| `ChestConfig` | `ChestConfig` | 宝箱配置 |
| `SpiritCoreConfig` | `SpiritCoreConfig` | 神瞳配置 |
| `GatheringConfig` | `GatheringConfig` | 采集物配置 |
| `DungeonConfig` | `DungeonConfig` | 副本配置 |
| `MonsterCampConfig` | `MonsterCampConfig` | 怪物营地配置 |

> ⚠️ 待确认：PoiType 声明为**八种**兴趣点，但此处仅列出 **7 个**专属 Config，缺少"地图 Boss"对应的 Config（如 `MapBossConfig`）。已在设计阶段标记，需补充。

### RegionConfig —— 区域配置

| 字段 | 类型 | 说明 |
|------|------|------|
| `RegionId` | `string` | 唯一 id，用网格坐标表示，如 `1x1`、`12x345` |
| `Pois` | `List<PoiConfig>` | 区域内兴趣点列表 |

### WorldRegionsConfig —— 世界区域配置（烘焙产物）

包含一个 `List<RegionConfig>`，是烘焙阶段生成的整表数据。

## 流程

### 1. 编辑器阶段

在编辑器中，于大世界场景里摆放八种兴趣点。每种兴趣点都挂一个 `PoiMono` 脚本，脚本内包含一个 `PoiConfig` 用于配置该兴趣点。

### 2. 烘焙阶段

在编辑器工具中配置 `Region Size`（一个区域的边长），然后点击**烘焙**按钮，生成一个 `WorldRegionsConfig`，其中包含一个 `List<RegionConfig>`。

编辑器根据不同的 `PoiType` 显示对应的 Config 面板；烘焙和加载时使用对应的 Config 数据即可。

### 3. 加载阶段

大世界场景加载完成后，开始加载烘焙出来的数据。POI 的加载遵循**两层过滤**：

1. **区域过滤**：POI 只会加载"玩家当前所在 Region"以及其 **3×3 范围内的其他 8 个 Region**（共 9 个 Region）内的 POI。
2. **兴趣半径过滤**：在这 9 个 Region 内，仅位于**玩家兴趣半径（配置项）**内的 POI 会被实例化并执行相应逻辑。

注意：`WorldSystem` 只负责这些 POI 的**生命周期管理**；每个 POI 的具体逻辑通过 **entity-logic-component 架构**（即 `EntitySystem`）实现。

## 持久化

暂不考虑持久化，每次运行都使用初始化数据。
