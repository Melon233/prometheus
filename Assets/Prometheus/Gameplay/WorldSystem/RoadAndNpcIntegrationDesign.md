# WorldSystem 与 NPC 集成设计

## 目标

WorldSystem 负责扫描并注册当前场景的 POI。NPC 可以作为一种场景 POI 常驻注册，但 NPC 的身份、行为和交互规则由 NpcSystem 持有；玩家距离不再决定 NPC 的激活或回收。

## 边界

- `WorldSystem`：扫描场景 `PoiMono`，创建 `NpcEntity` 并注册到 EntitySystem。
- `EntitySystem`：托管 `NpcEntity` 生命周期和逐帧逻辑。
- `NpcSystem`：加载 NPC 定义，维护 NPC 运行时状态和交互入口。
- `NpcLogic`：判断可交互条件并生成交互上下文。

WorldSystem 不负责对话、任务推进和镜头控制；NpcSystem 在自身生命周期结束时取消未完成交互并释放订阅。

## 数据关系

`PoiConfig` 只保存 `PoiId`、位置和 `PoiType`。当类型为 NPC 时，通过 `NpcId` 查找 `NpcDefinition`。NPC 的对话、任务入口、阵营和日程不应堆入通用 PoiConfig。

## 事件

NpcSystem 发布 NPC 领域状态变化；任务系统只消费领域事件，不直接依赖 WorldSystem 内部集合。WorldSystem 不再发布基于玩家距离的 POI 激活或失活事件。

## 网络与存档

NPC 状态由服务端或存档层决定，场景表现的生命周期由 EntitySystem 与 NpcSystem 管理。若后续需要大世界分片加载，应建立独立的场景/资源流送系统，而不是恢复 POI 距离显隐 AOI。
