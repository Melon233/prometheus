# NpcSystem 设计说明

## 定位

NpcSystem 负责 NPC 定义、运行时状态、交互入口和 NPC 领域事件；NPC 的场景显隐与 POI 生命周期由 PoiSystem 负责，EntitySystem 负责实体托管。

## 主要组件

- `NpcDefinition`：NPC 静态配置，包括 NPC ID、显示信息、对话入口和任务入口。
- `NpcRuntimeState`：解锁、当前阶段、离场和持久化字段。
- `NpcEntity`：绑定场景对象并组合 `NpcComponent` 与 `NpcLogic`。
- `NpcLogic`：判断交互条件、选择入口、发布状态变化。
- `InteractionCoordinator`：保证同一玩家只有一个活动交互会话，并统一处理取消。

## 依赖规则

`NpcSystem` 是程序集内部实现，由玩法组合根以 `INpcSystem` 注册。它**不依赖任何演出系统**：交互请求通过 `InteractionRequested` 事件发布，由叙事适配器接管；NpcSystem 不直接操作 `NarrativeSystem`、`CameraSystem` 或 Dialogue UI。

## 任务接口

`INpcSystem` 对外提供只读 NPC 状态和 NPC 领域事件；任务系统通过 `QuestNpcAdapter` 消费这些事件。任务系统不应直接持有或修改 `NpcSystem`、`NpcLogic`。

## 生命周期

NPC 被 PoiSystem 回收时，NpcSystem 必须先取消相关交互会话，再释放事件监听和表现引用；NPC 的持久状态不能因为表现对象回收而丢失。

## 第一阶段实现

第一阶段已实现 `NpcDefinition`、`NpcRuntimeState`、`NpcComponent`、`NpcEntity`、`NpcLogic` 和 `NpcSystem`。`PoiType.Npc` 已接入 PoiSystem 场景加载，`NpcSystem.InteractionRequested` 提供外部演出/对话适配入口。

## 交互演出的接管方式

NpcSystem 只负责**唯一交互会话的开闭**，不负责演出本身：

1. `TryBeginInteraction` 建立唯一活动会话并发布 `InteractionRequested`。
2. 叙事适配器订阅该事件，自行决定播放哪段剧情、如何绑定玩家与 NPC 对象。
3. 演出结束后适配器调用 `INpcSystem.CompleteInteraction(entityId)` 释放会话。
4. NPC 被回收或需要外部打断时调用 `INpcSystem.CancelInteraction(entityId)`；适配器负责停止自己启动的演出并释放输入与镜头租约。

> 历史说明：早期由内部的 `NpcInteractionCoordinator` 直接驱动 `FilmSystem` 播放 Timeline 演出，`NpcDefinition` 上也带有 `interactionFilm`、`playerBindingKey`、`npcBindingKey` 三个字段。FilmSystem 已被 NarrativeSystem 取代并整体移除，协调器与这三个字段一并删除。NpcSystem 因此退回到"只发布事实、不驱动表现"的边界，这也是它原本应有的职责范围。

当前阶段尚未实现真实 Dialogue UI、玩家范围触发器和多入口分支配置，叙事适配器本身也尚未接入。
