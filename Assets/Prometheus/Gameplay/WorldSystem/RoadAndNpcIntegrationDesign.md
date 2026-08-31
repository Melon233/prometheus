# WorldSystem 与 NPC 集成设计

## 目标

WorldSystem 负责世界区域、POI 的加载和回收。NPC 可以作为一种 POI 被 AOI 管理，但 NPC 的身份、行为和交互规则由 NpcSystem 持有。

## 边界

- `WorldSystem`：决定 NPC 是否进入当前世界、何时实例化和何时回收。
- `EntitySystem`：托管 `NpcEntity` 生命周期和逐帧逻辑。
- `NpcSystem`：加载 NPC 定义，维护 NPC 运行时状态和交互入口。
- `NpcLogic`：判断可交互条件并生成交互上下文。

WorldSystem 不负责对话、任务推进和镜头控制；NPC 回收前必须通知 NpcSystem，使其取消未完成交互并释放订阅。

## 数据关系

`PoiConfig` 只保存 `PoiId`、位置和 `PoiType`。当类型为 NPC 时，通过 `NpcId` 查找 `NpcDefinition`。NPC 的对话、任务入口、阵营和日程不应堆入通用 PoiConfig。

## 事件

WorldSystem 发布 POI 激活、失活和区域变化事件；NpcSystem 将这些事件转换为 NPC 可用状态变化。任务系统只消费领域事件，不直接依赖 WorldSystem 内部集合。

## 网络与存档

世界层负责区域和实体可见性，NPC 状态由服务端或存档层决定。客户端回收 NPC 表现对象不能清除 NPC 的持久状态。
