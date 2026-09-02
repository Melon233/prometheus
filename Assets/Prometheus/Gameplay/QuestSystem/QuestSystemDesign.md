# QuestSystem 设计说明

## 定位

QuestSystem 是纯逻辑系统，负责任务定义、接取条件、目标进度、完成/失败状态、任务链和持久化。它不直接控制 Timeline、镜头、UI 或 NPC GameObject。

## 数据模型

- `QuestDefinition`：静态配置，包括接取、目标、完成、失败条件和动作。
- `QuestRuntimeState`：当前状态、目标进度、已处理事件和版本。
- `QuestCondition`：可组合的条件节点。
- `QuestObjective`：事件类型、目标 ID、数量和进度规则。
- `QuestAction`：接取、阶段切换和完成时的逻辑动作。

任务状态至少包括 `Unavailable`、`Available`、`Accepted`、`Active`、`Completed`、`Failed`、`Abandoned` 和 `Expired`。

## 依赖方向

QuestSystem 只依赖领域事件、只读状态查询和动作接口，不依赖 `NpcLogic`、`FilmSystem` 的具体类。使用适配器连接外部系统：

- `QuestFilmAdapter`：把 `PlayFilmAction` 转换为 FilmSystem 调用。
- `QuestNpcAdapter`：把 NPC 对话和状态事件转换为任务事件。
- `QuestWorldAdapter`：把进入区域、POI 激活等事件转换为任务事件。

`QuestSystem` 是程序集内部实现，由 `GameplayKit` 以 `IQuestSystem` 注册。`QuestNpcAdapter` 持有 `IQuestSystem`，公共 `QuestEventAdapters` 的全部入口也接收 `IQuestSystem`，因此外部发布任务事件不需要引用或向下转换具体实现。

## 事件驱动

任务目标通过 `DialogueCompleted`、`FilmCompleted`、`ItemAdded`、`EnemyDefeated`、`EnteredRegion` 和 `NpcStateChanged` 等事件更新。事件必须携带稳定 ID，并进行幂等处理，避免重连或重复回调造成重复计数。

## 演出结果

任务动作启动 Film 后，由适配器监听完成结果。`Completed` 是否推进、`Skipped` 是否算完成、`InteractionFailed` 是否允许重试，都由任务配置决定，不能由 FilmSystem 固定解释。

## 存档与多人

存档应保存任务实例状态、目标进度、已处理事件 ID、任务变量和版本号。多人模式下必须区分玩家任务、队伍任务和世界任务；权威状态由服务端或任务状态服务持有，客户端 Film 仅负责表现。

## 第一阶段实现

当前已实现 `QuestDefinition`、`QuestRuntimeState`、`QuestSystem` 和 `QuestNpcAdapter`。任务配置可在 Unity 中通过 `Prometheus/Quest/Quest Definition` 创建，配置稳定任务 ID、前置任务和事件目标。运行时通过 `RegisterDefinition` 注册配置，通过 `TryAccept` 接取任务，通过 `PublishEvent` 注入领域事件；系统会按 `EventId` 幂等计数，所有目标达到要求后自动进入 `Completed`。

`QuestNpcAdapter` 已订阅 `NpcSystem.InteractionRequested`，会把 NPC 交互转换为 `NpcInteraction` 事件。Film 完成、道具获得、敌人击败和区域进入等其他事件由 `QuestEventAdapters` 调用 `PublishEvent`，因此 QuestSystem 不直接依赖 FilmSystem 或表现对象。

## 第二阶段实现

新增 `QuestCatalog` 用于批量校验和注册任务配置；新增 `Fail`、`Expire`、完成奖励通知以及 `CaptureSnapshot`/`RestoreSnapshot` 存档接口。任务完成后通过 `RewardGranted` 发布物品、货币或经验奖励，实际写入由背包、货币或经验适配器负责。任务事件可通过 `QuestEventAdapters` 接入 Film、对话、物品、敌人和区域逻辑；任务 UI 仍由外部订阅状态事件实现，已有角色预制体可作为 NPC 表现占位进行场景手测。
