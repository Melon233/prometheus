# FilmSystem 设计说明

## 职责

FilmSystem 负责 Timeline 演出实例的创建、绑定、播放、暂停、停止、跳过、快照恢复，以及镜头租约、输入锁和交互等待。它是表现编排系统，不拥有 NPC 状态、任务状态或奖励发放逻辑。

## 对外契约

- `IFilmSystem.Play`：使用运行时绑定和 `FilmFlowContext` 启动演出。
- `FilmHandle`：管理单次演出的暂停、恢复、停止、跳过和快照捕获。
- `FilmPlaybackSnapshot`：保存演出 ID、时间、状态和流程变量，供存档或网络层消费。
- `IFilmSystem.SnapshotCaptured`：向外部同步层发送快照通知。
- `IFilmInteractionService`：承接对话和 QTE UI。
- `IFilmFlowService`：承接外部事件等待。

`FilmSystem` 是程序集内部实现，由 `GameplayKit` 以 `IFilmSystem` 注册。NPC、任务或其他业务只能查询和持有 `IFilmSystem`；系统内部对输入和镜头的依赖分别使用 `IInputSystem` 与 `ICameraSystem`，不向外暴露具体实现。

## 与 NPC 的关系

NPC 通过 InteractionCoordinator 请求播放 Film，并提供 `NpcId`、`DialogueId`、`QuestId` 等上下文。FilmSystem 不直接查询 `NpcLogic`，也不直接创建对话框；对话服务在收到请求后返回选择结果。

## 与任务的关系

任务系统可以发起 Film 动作，也可以消费 Film 完成事件。FilmSystem 只返回 `Completed`、`Skipped`、`Requested`、`InteractionFailed` 等结果，是否推进任务由任务配置决定。

## 生命周期约束

停止、系统销毁、交互取消和演出自然结束必须统一释放 PlayableDirector、输入租约、镜头租约和异步等待。任何外部系统不得直接销毁 FilmSystem 创建的运行时对象。

## 后续扩展

后续可增加跳过确认、断点策略、对话选择结果结构化传递和多人主从同步，但不应把网络协议或任务业务规则写入 FilmSystem。
