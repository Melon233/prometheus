# 世界系统（World System）

## 概述

世界系统负责大世界相关逻辑，核心是兴趣点（POI）的场景注册、状态同步与交互管理：

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

大世界场景加载完成后，`WorldSystem` 扫描全部场景 `PoiMono`，创建对应的 `PoiEntity` 或 `NpcEntity` 并注册到 `EntitySystem`。客户端不再按照玩家所在 Region、3×3 邻域或兴趣半径切换 POI GameObject 的显隐。

一次性 POI 的消费隐藏、可刷新 POI 的冷却隐藏与重生恢复均由对应 Logic 管理。玩家附近 3×3 chunk 仍用于向服务器拉取 POI 状态，但网络同步范围不决定场景 POI 是否显示。

注意：`WorldSystem` 负责 POI 注册、服务器状态同步和交互入口；每个 POI 的具体状态与表现通过 **entity-logic-component 架构**（即 `EntitySystem`）实现。若未来需要控制大世界常驻资源规模，应引入独立的场景/资源流送系统。

## 持久化

暂不考虑持久化，每次运行都使用初始化数据。
