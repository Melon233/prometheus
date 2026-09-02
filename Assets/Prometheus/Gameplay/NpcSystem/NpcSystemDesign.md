# NpcSystem 设计说明

## 定位

NpcSystem 负责 NPC 定义、运行时状态、交互入口和 NPC 领域事件；NPC 的场景显隐与 POI 生命周期由 WorldSystem 负责，EntitySystem 负责实体托管。

## 主要组件

- `NpcDefinition`：NPC 静态配置，包括 NPC ID、显示信息、对话入口和任务入口。
- `NpcRuntimeState`：解锁、当前阶段、离场和持久化字段。
- `NpcEntity`：绑定场景对象并组合 `NpcComponent` 与 `NpcLogic`。
- `NpcLogic`：判断交互条件、选择入口、发布状态变化。
- `InteractionCoordinator`：保证同一玩家只有一个活动交互会话，并统一处理取消。

## 依赖规则

`NpcSystem` 是程序集内部实现，由 `GameplayKit` 以 `INpcSystem` 注册。系统内部的 `NpcInteractionCoordinator` 只依赖 `IFilmSystem` 和 `IEntitySystem`，不直接操作具体 `FilmSystem`、`CameraSystem` 或 Dialogue UI；DialogueSystem 负责呈现对话并返回结果。

## 任务接口

`INpcSystem` 对外提供只读 NPC 状态和 NPC 领域事件；任务系统通过 `QuestNpcAdapter` 消费这些事件。任务系统不应直接持有或修改 `NpcSystem`、`NpcLogic`。

## 生命周期

NPC 被 WorldSystem 回收时，NpcSystem 必须先取消相关交互会话，再释放事件监听和表现引用；NPC 的持久状态不能因为表现对象回收而丢失。

## 第一阶段实现

第一阶段已实现 `NpcDefinition`、`NpcRuntimeState`、`NpcComponent`、`NpcEntity`、`NpcLogic` 和 `NpcSystem`。`PoiType.Npc` 已接入 WorldSystem 场景加载，`NpcSystem.InteractionRequested` 提供外部演出/对话适配入口；第二阶段在此基础上接入 Film 自动播放协调器。

## 第二阶段实现

第二阶段通过 `NpcInteractionCoordinator` 将 NPC 交互请求连接到 `FilmSystem`。`NpcSystem.AfterNew` 创建协调器并订阅 `InteractionRequested`，调用 `TryBeginInteraction` 后会自动执行以下流程：

1. 根据 `NpcInteractionContext.EntityId` 从 `EntitySystem` 定位 `NpcEntity`。
2. 从当前 `IGameplayKit.Player` 获取玩家绑定对象，并使用 `NpcDefinition.PlayerBindingKey` 与 `NpcBindingKey` 写入 `FilmBindingContext`。
3. 使用 `NpcDefinition.InteractionFilm` 调用 `FilmSystem.Play`，同时向 `FilmFlowContext` 写入 `NpcId` 和 `InteractionId`，供 Timeline 轨道或后续逻辑读取。
4. 异步等待 `FilmHandle.WaitForCompletionAsync`，在 Film 完成、停止或异常退出时统一调用 `CompleteInteraction` 释放 NPC 会话占用。

当 NPC 被场景系统回收或外部逻辑需要打断演出时，应调用 `INpcSystem.CancelInteraction(entityId)`。该接口会先停止当前 Film，再清理活动交互状态，保证输入、镜头和 Timeline 资源由 `IFilmSystem` 统一释放。

当 `InteractionFilm` 为空时，协调器不会启动 Film，会保留 `InteractionRequested` 的外部订阅能力，适用于由 Dialogue UI 或其他交互适配器接管的 NPC。当前阶段尚未实现真实 Dialogue UI、玩家范围触发器和多入口分支配置。
