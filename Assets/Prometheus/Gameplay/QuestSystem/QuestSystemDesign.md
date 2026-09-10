# 任务系统设计（重写）

> 状态：**第 1 期（内核）已实现**（2026-09-08）：定义模型、条件求值、步骤推进、快照、校验器全部落地，18 个 EditMode 用例覆盖。
> 第 2 期的事件总线与触发器一并提前实现（它们是闭环 L3 的前置）。第 4 期世界绑定、第 5 期 UI、第 7 期章节与委托仍未开始。
> 定位：承担任务的**决策**——什么时候开始、什么时候推进、推进以后世界变成什么样
> 不承担：剧情演出（`NarrativeSystem`）、背包与货币写入（各自系统）、网络同步（NetworkKit）
> 命名空间：`Xuan.Prometheus.Quest`
> 对外接线（对话绑定解析、事件投喂、共享变量存储、奖励落地）见同目录 `QuestLoopIntegration.md`
> 日期：2026-09-07

---

## 0. 一句话架构

**任务是一台状态机，它的转移条件是表达式，它对世界的影响是声明式映射而不是累积的副作用。**

三个词展开：

| 词 | 含义 | 反面教材 |
|---|---|---|
| **状态机** | 一个任务在任一时刻处于唯一一个步骤；步骤间靠 `transitions` 跳转，分支、回滚、失败是同一种机制 | 原神的 acceptCond/finishCond/failCond 三元组 + 并行 subquest |
| **表达式** | 条件是一段编译过的表达式文本，不是枚举 | 原神 50 种 `QUEST_COND_*` |
| **声明式映射** | 「任务处于某状态时世界长什么样」是一张可随时重算的表，不是一串执行过就算数的动作 | 原神 `QUEST_EXEC_REFRESH_GROUP_SUITE` |

第三条是本设计与原神差异最大、也最重要的地方，理由见 §4。

---

## 1. 为什么重写而不是改

现有实现 667 行，模型是「任务 → 并行计数器目标」。它做对了三件事，应当保留：

1. **纯逻辑边界**：只抛 `RewardGranted`，背包与货币写入交给外部适配器。
2. ~~**适配器模式**：`QuestNpcAdapter` 把 NPC 事件翻译成任务事件，任务规则不侵入 `NpcLogic`。~~ **本条已作废**——应当保留的是「任务规则不侵入 NPC 逻辑」这个目的，而不是 `QuestNpcAdapter` 这个实现。剧情闭环要求 NpcSystem 反查任务状态，与本订阅叠加即成环，故搬运方向翻转为「NPC 主动上报」。详见 `QuestLoopIntegration.md` §1。
3. **快照只存运行时状态**，不复制静态配置。

但数据模型的形状不对：需要的是「**有序步骤 ×（条件, 动作）**」，现有的是「**无序计数器集合**」。从后者演进到前者，`QuestDefinition` / `QuestObjectiveDefinition` / `QuestRuntimeState` / `PublishEvent` 全部要换掉。

决定性因素：**全工程零个 `QuestDefinition` / `QuestCatalog` 资产，除 `GameplayKit.cs:270` 的注册行外零个消费方。** 没有内容、没有 UI、没有存档接线。重写的沉没成本接近零。

顺带记录评估时查到的实际缺陷，作为「现有实现未经使用检验」的证据，重写时逐条不要重犯：

| # | 位置 | 问题 | 新设计如何避免 |
|---|---|---|---|
| 1 | `QuestSystem.cs:138` | `RestoreSnapshot` 先 `states.Clear()` 再只恢复快照条目 → 版本更新后新增的任务读档后状态条目消失，`TryAccept` 抛裸 `KeyNotFoundException` | **Q5**：默认状态可推导，存档只记录偏离 |
| 2 | `QuestNpcAdapter.cs:16` | EventId 是 `npc-interaction:{实体}:{交互}`，跨整局恒定；幂等去重把它当重复事件丢弃 → 「对话 3 次」永远卡在 1 | 删除事件去重表；计数即变量，天然可存档（§5.3） |
| 3 | `QuestSystem.cs:51` | `RegisterDefinition` 无条件设 `Available`，`QuestState.Unavailable` 是从未被赋值的死枚举 | 状态收敛为 5 个，且 `Locked`/`Available` 为推导值（§3.2） |
| 4 | `QuestDefinition.cs:117` | `processedEventIds` 无上限增长且整体入档 | 同 #2，该表不存在 |
| 5 | 状态机 | `Abandoned` 后无法重接 | `Abandoned` 不是状态，是「回到 Available」的操作（§3.2） |
| 6 | `PublishEvent` | 发奖回调若写背包并回抛物品事件，会在 `foreach` 内重入，无守卫 | **Q4/§6**：失效累积到帧末统一 flush，动作永不嵌套在变量写入里 |
| 7 | `QuestEventType` | 封闭枚举开在核心契约上，加一种触发方式就要改核心并全量重编译 | 事件总线用「名字 + 载荷」，零枚举（§5.1） |

另记：`NpcRuntimeState.Stage` 同样是从未被读写的死字段。`FilmSystem` 与 `NpcDefinition.InteractionFilm` 已随 NarrativeSystem 取代 FilmSystem 一并移除，本次重写只需接管 `Stage`（§7.3）。

---

## 2. 原神任务系统能力清单与本设计的对应

来源说明：对原神的了解来自**公开的配置表结构**（`MainQuestExcelConfigData` / `QuestExcelConfigData` / `TalkExcelConfigData` 等社区数据挖掘产物，亦被开源服务端复刻项目大量引用）与玩家可观察行为。字段名可能与内部命名有出入，结构和能力边界可信。

| 原神能力 | 本设计的承载物 | 期 |
|---|---|---|
| Chapter 章节分组与开启条件 | `QuestChapter` 纯元数据 + 开启表达式，无运行时状态 | 7 |
| MainQuest 类型 / 标题 / 总奖励 | `QuestDefinition` | 1 |
| SubQuest 有序步骤、步骤描述、隐藏步骤 | `QuestStep`（`hidden` 标志） | 1 |
| acceptCond / finishCond / failCond + AND/OR | `QuestStep.transitions`：单一有序转移列表（§3.3） | 1 |
| 50 种 `QUEST_COND_*` | 条件表达式 + 命名空间投影（§5.2） | 1 |
| 80 种 `QUEST_EXEC_*`（世界类） | **声明式世界绑定**（§4） | 4 |
| 80 种 `QUEST_EXEC_*`（一次性类） | `[SerializeReference]` 动作节点 + 执行记录（§5.4） | 3 |
| questVar 无名整型数组 | 命名有类型变量 `quest.<id>.<name>`（§5.3） | 1 |
| timeVar 时间变量、限时任务 | `time.*` 投影 + 步骤 `timeLimit` → `quest.<id>.remaining` | 7 |
| rewind 任务回滚 | 转移可指向任意步骤，含更早的（§3.3） | 1 |
| **SceneGroup / Suite 换世界内容** | `WorldGroup` / `WorldSuite` + 绑定表（§4.2） | 4 |
| 任务专属实体生命周期 | suite 成员即生命周期（§4.2） | 4 |
| 解锁传送点 / 区域 / 地图标记 | 一次性动作 → `IQuestWorldPort` | 3 |
| Talk 按任务状态分流 NPC 对话 | `DialogueBinding`（§7.3） | 3 |
| 触发方式：主动 / 进范围自动 / 任务开始 | `DialogueBinding.beginWay` | 3 |
| CG、演出触发与「已看过」、跳过、回放 | 直接复用 `NarrativeSystem`（`HasSeen` / `RequestSkip` / `PreviewAsync` 已具备） | 3 |
| 任务追踪（同时只追一个） | `IQuestSystem.TrackedQuestId` | 5 |
| 导航指引（NPC / 坐标 / group / 实体） | `QuestGuide` + `IQuestGuidePort`（§7.2） | 5 |
| 任务列表、详情、已完成归档、更新提示 | UI 层消费 `QuestChanged` 事件 | 5 |
| 每日委托：池 + 随机分配 + 城市声望 | `CommissionPool` 独立模块，不进内核 | 7 |
| 试用角色、临时锁队、秘境 checkpoint | 一次性动作，各自加一个动作类 | 按需 |

---

## 3. 内核模型

### 3.1 三层

```
Chapter   —— 纯元数据：分组、开启条件、展示。无运行时状态。
  └ Quest —— 状态机实例。有 status、当前步骤、自己的变量、一次性动作执行记录。
      └ Step —— 唯一活动单元。持有目标行、转移列表、触发器、绑定。
          └ Objective —— 展示用进度行（击败丘丘人 3/5）。不是状态机节点。
```

**为什么 Objective 不是状态机节点**：并行目标只影响*显示*和*完成判定*，不影响*在哪一步*。把它降级成展示行，「当前步骤」就永远是单值，于是存档、追踪、导航、断点恢复全部退化成单点问题。

代价：无法表达「两条真正独立、可乱序推进且各有独立后续分支的支线」。逃生口是让一个任务启动另一个任务（§3.5）。这是自觉接受的限制，记在 §11。

### 3.2 状态

```csharp
public enum QuestStatus
{
    Locked,     // 解锁条件未满足（推导值）
    Available,  // 可接（推导值）
    Active,     // 进行中，带 CurrentStepId
    Completed,
    Failed      // 带 FailReason 字符串
}
```

从 8 个收敛到 5 个：

- `Accepted` 删除——原实现里它在同一次调用中立刻转 `Active`，是个不存在的状态。
- `Expired` 删除——是 `Failed` 的一种原因，用 `FailReason` 表达。
- `Abandoned` 删除——放弃不是终态，是「回到 `Available`」的操作。`resetOnAbandon` 决定是否清掉该任务的变量与步骤进度。

**关键性质：`Locked` 与 `Available` 是推导值，不入档。**

```
status(q) = 存档里有 q 的记录 ? 记录值
          : Eval(q.unlockCondition) ? Available : Locked
```

于是版本更新新增的任务，在旧存档上自然处于正确状态，不需要迁移脚本——直接消掉缺陷 #1 与 #3。

`acceptMode`：`Auto`（条件满足即自动进入 `Active`，绝大多数剧情任务）/ `Manual`（需玩家显式接取，如委托）。

### 3.3 步骤与转移

**单一 `transitions` 列表取代 accept/finish/fail 三元组。**

```csharp
[Serializable]
public sealed class QuestTransition
{
    public string condition;          // 表达式；空 = 恒真
    public QuestTransitionKind kind;  // ToStep | CompleteQuest | FailQuest
    public string targetStepId;       // kind == ToStep 时有效
    public string failReason;         // kind == FailQuest 时有效
    [SerializeReference] public List<QuestAction> actions;  // 转移时执行的一次性动作
}
```

求值规则：**按声明顺序取第一个成立者**。顺序即优先级，写法即语义，不需要额外的优先级字段。

一条 `transitions` 覆盖全部形态：

| 形态 | 写法 |
|---|---|
| 线性推进 | 单条 `ToStep(下一步)`，条件为完成判定 |
| 完成任务 | `CompleteQuest` |
| 分支 | 多条 `ToStep`，条件互斥或按顺序兜底 |
| 失败 | `FailQuest`，条件写失败判定 |
| **回滚 rewind** | `ToStep` 指向更早的步骤——无需任何额外机制 |
| 超时 | 条件写 `quest.<id>.remaining <= 0` |

步骤的 `finish` 字段是语法糖：不填则默认「全部非隐藏目标完成」，可被转移条件里的 `step.objectivesDone` 引用。

### 3.4 目标行

```csharp
[Serializable]
public sealed class QuestObjective
{
    public string objectiveId;
    public string descTextKey;    // 文案 key，可插值变量
    public string progressExpr;   // 可空。例如 quest.q_hunt.kills
    public string targetExpr;     // 可空。例如 5
    public string doneExpr;       // 可空。缺省为 progress >= target
    public QuestGuide guide;
    public bool hidden;
}
```

目标行本身**不持有状态**——进度全部来自变量。所以目标行是纯展示 + 纯判定，改配置不会让存档失效。

### 3.5 任务之间

- `unlockCondition`：表达式，可引用 `quest.<id>.status`、`player.level`、`flag.*` 等。取代原实现的 `prerequisiteQuestIds` 列表（后者是前者的一个特例）。
- 一个任务可以用一次性动作 `StartQuestAction` 启动另一个任务——这是「并行支线」的逃生口，也是章节内多任务编排的手段。

---

## 4. 核心分歧：世界状态必须可推导

### 4.1 铁律 Q1

> **世界的可见状态必须能从任务状态纯函数地推导出来，而不是由动作累积出来。**

原神把「刷新场景 suite」做成 `QUEST_EXEC_REFRESH_GROUP_SUITE`——一个执行过就算数的命令式动作。这导致三类问题：读档时必须重放、任务回滚时世界不跟着回滚、场景重新加载时状态可能丢失。

本设计把世界影响拆成两类，**分别用不同机制**：

| 类别 | 例子 | 机制 | 幂等性来源 |
|---|---|---|---|
| **声明式绑定** | 村口的 NPC 换一批、桥修好了、怪物刷出来、NPC 说不同的话 | 一张「条件 → 世界配置」的映射表，任何时候都可以重算 | 构造上就是纯函数 |
| **一次性动作** | 发奖励、扣道具、解锁传送点、播剧情、启动另一个任务 | `[SerializeReference]` 动作节点 | 执行记录进存档，恰好一次 |

判据很清晰：**如果这件事「重算一遍还是同一个结果」，它就是绑定；如果它改变了任务系统之外的持久状态，它就是动作。**

这条与 `NarrativeSystem` 的 `Settle()` 是同一个思想的两次应用——都是「从位置重建正确状态，而不是重放历史」。

### 4.2 WorldGroup / WorldSuite

这是原神 SceneGroup/Suite 的对应物，也是本次唯一必须新建、没有现成对应物的一层。

```
WorldGroupMono (场景组件)
  Id           : UUID（沿用 PoiConfig 的 UUID 分配与导出管线）
  Members      : List<GameObject>   // NPC、道具、怪物刷新点、POI 载体
  Suites       : List<WorldSuite>
  DefaultSuite : string

WorldSuite
  Name
  ActiveMembers : 成员索引集合
  Overrides     : 每成员可选覆盖（位置、朝向、NPC 对话集）
```

**当前 suite 的解析是纯函数：**

```
suite(group) = 绑定表中按优先级第一条条件成立者的 suiteName
             : group.DefaultSuite
```

`IQuestGroupPort.ApplySuite(groupId, suiteName)` 必须幂等：反复用同一个 suite 调用等价于调用一次。

重算时机（**只有这四个**）：绑定表条件被失效通知弄脏、group 所在场景/区块加载完成、任务状态变化、显式 `Refresh()`。

### 4.3 与 POI 服务器权威的关系

工程里 POI 的可变状态（解锁/开启/收集/击败）是**服务器权威**的（`WorldPersistenceDesign.md`：Go 服务器 + MongoDB，UUID 主键，客户端发请求、服务器确认后才做表现）。

**界定：suite 决定「世界里存在哪些对象」，POI 状态决定「这些对象各自是什么状态」。两者正交，互不覆盖。**

推论：
- suite 切换不得写 POI 状态；POI 状态同步不得增删 group 成员。
- 一个宝箱被 suite 隐藏，它在服务器上的「已开启」状态不受影响；重新显示时仍是已开启。
- 若任务需要「重置一个宝箱」，那是一次性动作走服务器请求，不是 suite 的事。

---

## 5. 机制细节

### 5.1 事件：名字 + 载荷，零枚举

现有 `QuestEventType` 是开在核心契约上的封闭枚举（缺陷 #7）。改为：

```csharp
public readonly struct QuestEvent
{
    public string Name { get; }                                      // "combat.enemy_defeated"
    public IReadOnlyDictionary<string, StoryValue> Payload { get; }  // { id: "hilichurl", count: 1 }
}
```

触发器按名字匹配，按表达式过滤载荷（载荷在 `e.*` 命名空间下可见）：

```csharp
[Serializable]
public sealed class QuestTrigger
{
    public string eventName;      // 精确匹配
    public string filter;         // 表达式，可空。例：e.id == "hilichurl"
    public string variablePath;   // 要写的变量，如 quest.q_hunt.kills
    public QuestTriggerOp op;     // Set | Increment | Max
    public string valueExpr;      // 可空，缺省 e.count 或 1
}
```

加一种触发方式 = 加一个发布方，核心零改动。

触发器的作用域：挂在**步骤**上（仅该步骤活动期间生效）或**任务**上（整个任务活动期间生效）。

**字符串名的代价与对策**：拼错不会编译报错。对策是各发布方在一个常量类里注册已知事件名清单，编辑期校验器据此检查触发器引用的名字（§9）。

### 5.2 条件：表达式 + 命名空间投影

直接复用 `NarrativeSystem` 的 `StoryExpression`（编译缓存、`CollectIdentifiers`、`Validate` 带命名空间承认检查）与 `IStoryVariableResolver`。

| 命名空间 | 归属 | 读 | 写 | 举例 |
|---|---|---|---|---|
| `flag.*` `var.*` | **剧情系统**（`INarrativeSystem.Variables`） | 任务可读 | 任务可写 | `flag.met_elder` |
| `quest.*` | **任务系统** | 剧情可读（只读） | 任务写 | `quest.q_wind.status`、`quest.q_hunt.kills` |
| `player.*` | 成长系统投影 | 只读 | — | `player.level` |
| `item.*` | 背包系统投影 | 只读 | — | `item.count("apple")`（函数） |
| `world.*` | 世界系统投影 | 只读 | — | `world.poiUnlocked(...)` |
| `npc.*` | NPC 系统投影 | 只读 | — | `npc.stage("elder")` |
| `time.*` | 时间投影 | 只读 | — | `time.hour`、`time.isDay` |
| `e.*` | 当前事件载荷 | 只读，**仅触发器 filter 内可见** | — | `e.id` |

**互投影而非合并存储**：任务系统持有自己的 `StoryVariables` 实例（根 `quest`），把它注册为剧情存储的投影，同时把剧情存储注册为自己的投影。两个系统各存各的档，不产生重复数据，也**不需要改动已完成的 `NarrativeSystem` 一行代码**（`StoryVariables.AddProjection` 的注释里写的例子恰好就是这个用途）。

### 5.3 变量即计数器

「击败 5 只丘丘人」不是一个特殊的目标类型，而是：

```
trigger:    combat.enemy_defeated, filter e.id == "hilichurl" → Increment quest.q_hunt.kills
objective:  progress = quest.q_hunt.kills, target = 5
transition: quest.q_hunt.kills >= 5 → CompleteQuest
```

由此**事件去重表整个消失**（缺陷 #2、#4）：计数是变量，变量进存档，重复事件的处理责任回到发布方——本来就该在那里。网络重包去重是 NetworkKit 的职责，不是任务系统的。

### 5.4 一次性动作

```csharp
[Serializable]
public abstract class QuestAction
{
    public string actionId;   // 在所属任务内唯一，用作执行记录的键
    public abstract void Execute(QuestActionContext context);
}
```

内置动作（每个一个类，加动作不改核心）：
`GrantRewardAction` / `ConsumeItemAction` / `SetVariableAction` / `StartQuestAction` / `FinishQuestAction` / `PlayStoryAction` / `UnlockPoiAction` / `UnlockAreaAction` / `SetNpcStageAction` / `NotifyAction`。

**恰好一次**：执行前查该任务的 `executed` 集合，执行后写入，`executed` 进存档。任务回滚到更早步骤时，是否清除执行记录由动作的 `rerunOnRollback` 决定（发奖励不清，设变量清）。

### 5.5 奖励

保留现有实现最正确的边界：**任务系统不写背包**。`GrantRewardAction` 抛 `RewardGranted`，由适配器落地。

新增 `claimMode`：`Auto`（完成即发）/ `Manual`（进待领取列表，玩家在 UI 点领取）。后者需要一个 `pendingRewards` 列表进存档。

---

## 6. 推进时机与重入

**铁律 Q4：不存在轮询。任何条件的重算都必须由一次显式的失效通知驱动。**

失效来源：

1. 任务自己的变量存储 `Changed` 事件（`StoryVariables` 已有）
2. 剧情变量存储 `Changed` 事件（订阅即可）
3. 投影主动推送：`IQuestProjection { event Action<string> Invalidated; }`——背包、等级、时间等变化时推送受影响路径
4. 步骤进入、任务状态变化
5. 显式 `Poke(questId)`

**依赖倒排索引**：`StoryExpression.CollectIdentifiers` 已经能列出表达式引用的全部路径。为每个活动步骤的转移条件建 `路径 → 步骤` 的倒排表，失效时只弄脏真正相关的步骤。

**帧末统一 flush，绝不立即求值**：

```
失效通知 → 加入 dirty 集合（仅此而已，不求值、不执行动作）

XSystem.OnUpdate 末尾 → Flush():
    轮次 = 0
    while dirty 非空 && 轮次 < 16:
        取出 dirty 快照，清空 dirty
        对每个脏任务：求值转移条件 → 执行转移 → 执行动作 → 重算世界绑定
        （动作可能再次弄脏，进入下一轮）
        轮次++
    若超过 16 轮：报错并挂起该任务，防止配置写出死循环
```

这直接消灭缺陷 #6：动作永远在一个明确的阶段执行，不会嵌套在某个变量的 setter 里，也不会在遍历字典时改变状态。

代价：任务推进有最多一帧延迟。对剧情任务无感知；若某处需要立即推进（如对话结束后立刻要看到任务更新），提供 `FlushNow()` 显式调用。

---

## 7. 与其他系统的对接

全部走端口接口，任务系统不引用任何玩法系统的具体类型。

### 7.1 端口清单

| 端口 | 职责 |
|---|---|
| `IQuestTextPort` | 文案 key → 显示文本（复用剧情系统的 `TextMap`） |
| `IQuestNarrativePort` | 播放一段剧情：地址 → `StoryGraph.BuildOrThrow()` → `INarrativeSystem.PlayAsync` |
| `IQuestWorldPort` | 解锁 POI / 区域、地图标记；内部走服务器请求 |
| `IQuestGroupPort` | `ApplySuite(groupId, suiteName)`，幂等 |
| `IQuestRewardPort` | 奖励落地（背包、货币、经验） |
| `IQuestGuidePort` | 导航目标 → 世界坐标 |
| `IQuestProjection` × N | 只读变量投影 + 失效推送 |

### 7.2 导航

```csharp
public enum QuestGuideKind { None, Npc, Position, Poi, Group }
```

挂在目标行上，步骤级作为兜底。`IQuestGuidePort` 解析成世界坐标，UI 层画罗盘箭头与地图标记。

### 7.3 NPC 对话分流

这是任务系统与剧情系统的主要接缝。`NpcDefinition.InteractionFilm` 及其绑定键字段已随 FilmSystem 一并移除，NPC 交互改由 `NpcSystem.InteractionRequested` 交给叙事适配器。

```csharp
[Serializable]
public sealed class DialogueBinding
{
    public string npcId;
    public string condition;           // 表达式，可空
    public int priority;
    public string storyLocation;       // StoryGraph 资产地址（YooAsset）
    public DialogueBeginWay beginWay;  // Interact | AutoOnEnter | OnStepEnter
    public bool once;
}
```

绑定可以挂在**步骤**（仅该步骤活动期间生效）或 **NPC 定义**（默认闲聊）上。解析：满足条件的绑定里优先级最高者胜出，无则回落到 NPC 默认。

这是**声明式绑定**（Q1 的第一类），不是动作——NPC 该说什么话永远是当前任务状态的纯函数，读档、回滚、重新进场景都自动正确。

`NpcSystem` 需要的改动：三个 Film 时代的字段（`interactionFilm` / `playerBindingKey` / `npcBindingKey`）已随 FilmSystem 移除；剩余改动是 `NpcLogic.OnInteract` 改为向任务系统查询对话绑定。`NpcRuntimeState.Stage`（当前是死字段）转为 `npc.stage(id)` 投影的后备存储，由 `SetNpcStageAction` 写。

---

## 8. 存档

沿用工程约定：系统自身 `CaptureSnapshot()` → JSON 字符串，无中央存档系统。

```csharp
[Serializable] public sealed class QuestSnapshot
{
    public List<QuestRecord> quests;            // 只包含偏离默认状态的任务
    public string trackedQuestId;
    public List<StoryVariableEntry> variables;  // quest.* 命名空间，复用剧情系统的条目格式
}

[Serializable] public sealed class QuestRecord
{
    public string questId;
    public int status;
    public string stepId;
    public string failReason;
    public List<string> executed;        // 已执行的一次性动作 id
    public List<string> pendingRewards;
    public long stepEnteredUtcTicks;     // 限时用
}
```

**铁律 Q5：默认状态可推导，存档只记录偏离。**

后果（都是想要的）：

- 版本更新新增任务 → 旧存档没有它的记录 → 按 `unlockCondition` 推导 → 自然正确。**无需迁移脚本。**
- 存档大小与「玩家实际碰过的任务数」成正比，与配置总量无关。
- 未知 questId 的记录（配置被删）→ 记 Warning 并丢弃，**不抛异常**。现有实现在这里直接抛错污染整个读档流程。

变量条目直接复用 `NarrativeSnapshot` 的 `StoryVariableEntry`（`path` + `kind` + 文本值，用共享的 `StoryValue.Parse` 还原），保证两个系统的值解析规则不产生分歧。

---

## 9. 编辑期校验

比照 `StoryGraphValidator`，在运行之前把配置错误全部暴露：

| # | 检查 |
|---|---|
| 1 | questId / stepId / objectiveId / actionId 非空且在作用域内唯一 |
| 2 | 全部条件表达式可编译 |
| 3 | 表达式引用的命名空间被承认（`StoryExpression.Validate` 已支持） |
| 4 | **表达式引用的每个命名空间都是可推送的**——否则该条件永远不会被重算（Q4 的静态保障） |
| 5 | 转移的 `targetStepId` 存在 |
| 6 | **可达性**：每个步骤从起始步骤可达；每个任务存在至少一条通向 `CompleteQuest` 的路径 |
| 7 | **无恒真自环**：不存在「条件恒真且指向自身」的转移 |
| 8 | 触发器的 `eventName` 在已注册事件名清单中 |
| 9 | 文案 key 已导入 |
| 10 | 引用的 `StoryGraph` 地址存在，且该图自身校验通过 |
| 11 | 世界绑定引用的 groupId / suiteName 存在 |
| 12 | 变量写入路径都在 `quest.*` 或 `flag.*` / `var.*` 下（不许写投影） |

第 4、6、7 条是这套设计**独有**的静态保障——原神那种枚举 + Lua 的结构做不到这类检查，只能靠跑。

---

## 10. 目录与分期

```
QuestSystem/
  QuestSystemDesign.md
  IQuestSystem.cs / QuestSystem.cs
  Core/        QuestStatus / QuestContracts / QuestRuntime / QuestSnapshot
               QuestVariables（quest.* 存储 + 互投影桥接）
               QuestConditionIndex（倒排索引 + dirty 集合 + Flush）
  Definitions/ QuestDefinition / QuestStep / QuestObjective / QuestTransition
               QuestTrigger / QuestGuide / QuestChapter / QuestCatalog
               QuestActions（[SerializeReference] 动作）
               QuestBindings（世界绑定 + 对话绑定）
               QuestValidator
  Events/      QuestEventBus / QuestEventNames
  Ports/       IQuestPorts + 各适配器
  World/       WorldGroupMono / WorldSuite / WorldGroupRegistry   ← 第 4 期
  Editor/      QuestEditorWindow / QuestInspector
  Tests/Editor/
```

| 期 | 内容 | 完成标志 |
|---|---|---|
| 1 | 内核：定义模型、条件求值、步骤推进、快照、校验器 | 纯 EditMode 可测，零 Unity 运行时依赖 |
| 2 | 事件总线 + 触发器 + 各系统发布方接入 | 「击败 N 只怪」端到端可跑 |
| 3 | 一次性动作 + 奖励 + 对话绑定 + 剧情触发 | 与 `NarrativeSystem` 打通，接管 `NpcSystem.InteractionRequested` |
| 4 | `WorldGroup`/`Suite` + 声明式世界绑定 | **剧情推进改变世界**；动 `PoiSystem` 导出管线 |
| 5 | UI：任务列表、追踪、导航罗盘与地图标记 | |
| 6 | 编辑器窗口 | |
| 7 | 章节、限时、每日委托、声望 | |

第 1 期是自足的：模型 + 求值 + 存档 + 校验全部不依赖任何其他系统，可以在 EditMode 下完整测试，这一点与 `NarrativeSystem` 第 0 期的做法一致。

---

## 11. 自觉接受的限制与风险

| 项 | 说明 | 缓解 |
|---|---|---|
| 单活动步骤 | 无法表达两条真正独立、可乱序推进且各有独立后续分支的支线 | 逃生口：一个任务用 `StartQuestAction` 启动另一个任务 |
| 事件名是字符串 | 拼错不编译报错 | 常量类注册清单 + 校验第 8 条 |
| 推进有一帧延迟 | 帧末 flush 的代价 | 需要即时性的地方调 `FlushNow()` |
| 表达式求值成本 | 条件多时每次 flush 的求值量 | 倒排索引只重算相关步骤；表达式已有编译缓存 |
| `WorldGroup` 要动场景数据 | 第 4 期会改 `PoiSystem` 导出管线，且 POI 是服务器权威 | §4.3 已界定正交边界；沿用 POI 的 UUID 分配机制，不发明第二套 |
| 服务器权威迁移 | 任务状态最终大概率要服务器权威，但服务器是 Go，C# 代码搬不过去 | 保持内核零 Unity 依赖、零 I/O、纯函数决策——能搬的是**模型**不是代码；现实收益是可测试性 |
| 章节 UI 与「章节开启」演出 | 未设计 | 第 7 期，纯表现，走 `NarrativeSystem` |

---

## 12. 与原神的刻意偏离一览

| 原神做法 | 本设计 | 理由 |
|---|---|---|
| 50 种 `QUEST_COND_*` 枚举 | 条件表达式 | 工程已有编译器、缓存与命名空间校验；枚举爆炸是 Excel 三参数配置的产物，不是设计必然 |
| 80 种 `QUEST_EXEC_*` 枚举 | 声明式绑定 + `[SerializeReference]` 动作 | 前者可推导免重放，后者加类不改核心 |
| `questVar` 无名整型数组 | 命名有类型变量 | 可读、可静态校验、可直接插值进文案 |
| accept/finish/fail 三元组 | 单一有序 `transitions` | 分支、回滚、失败统一成一种机制，少三个概念 |
| 并行 subquest | 单活动步骤 + 多目标展示行 | 存档、追踪、导航、断点恢复全部退化成单点问题 |
| Lua 脚本桥接场景 | 世界绑定表 | 无需脚本运行时；可静态校验；可推导 |
| 任务事件去重表 | 不存在 | 计数即变量，存档天然幂等；去重责任归发布方与网络层 |
| 8 状态枚举 | 5 状态 + `FailReason` | `Accepted`/`Expired`/`Abandoned` 都不是独立状态 |
| 世界状态由 exec 累积 | **Q1：世界状态可从任务状态推导** | 读档、回滚、场景重载自动正确——原神在这里是有实际问题的 |

---

## 13. 五条铁律（实现时逐条对照）

- **Q1** 世界的可见状态必须能从任务状态纯函数地推导出来，不得由一次性动作累积。
- **Q2** 条件求值是纯查询，不得有副作用；动作是纯写入，不得读取正在求值的条件。
- **Q3** 一次性动作必须有执行记录且记录进存档，跨读档恰好执行一次。
- **Q4** 不存在轮询：条件重算必须由显式失效通知驱动，且条件里出现的每个命名空间都必须是可推送的（编辑期校验）。
- **Q5** 任务状态的默认值必须可推导，存档只记录偏离默认的部分。
