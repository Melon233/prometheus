# NpcSystem 设计说明

> 状态：设计稿。当前实现只有会话串行化（`NpcSystem.cs`，51 行）
> 定位：NPC 的身份、**交互编排**与任务接取入口
> 不承担：任务规则（`QuestSystem`）、演出（`NarrativeSystem`）、NPC 的场景显隐与生命周期（`PoiSystem`）
> 命名空间：`Xuan.Prometheus.Npc`
> 参考对象：原神的 NPC 对话分流、头顶任务标记与对话内接取
> 日期：2026-09-07

---

## 0. 一句话架构

**NpcSystem 是剧情闭环里唯一的编排者：向任务系统「查」这个 NPC 现在该说什么，向剧情系统「下单」把这段演出来，再把演出结果「报」回任务系统。**

它自己不保存任务进度，也不实现任何演出。三个动词分别对应三次跨系统调用，除此之外它只做一件本职工作：保证同一时刻只有一个活动交互会话。

---

## 1. 三系统的职责与依赖方向

| 系统 | 回答的问题 | 明确不做 |
|---|---|---|
| **NpcSystem** | 玩家点了这个 NPC，接下来该发生什么？这个 NPC 头顶挂什么？ | 不保存任务进度，不实现演出 |
| **QuestSystem** | 任务在哪一步？这一步要做什么？完成了吗？ | 不认识 NPC 实体，不认识剧情图，不碰 UI |
| **NarrativeSystem** | 把这段剧情演出来 | 不认识任务，不认识 NPC 业务 |

```
                     ┌───────────────┐
   ①查询对话绑定      │  QuestSystem  │  零向外依赖
   ┌────────────────>│   （状态机）   │
   │  ③上报事件       │               │
   │  ┌─────────────>└───────────────┘
┌──┴──┴──────┐
│  NpcSystem │  唯一编排者
│ （交互编排） │
└──┬─────────┘
   │ ②下单演出
   v
┌─────────────────┐
│ NarrativeSystem │  零向外依赖
└─────────────────┘
```

### 铁律 N1：依赖单向

> **NpcSystem 依赖另外两个；另外两个既不依赖 NpcSystem，也不互相依赖。**

QuestSystem 与 NarrativeSystem 都只是被调用方。这不是风格偏好——它决定了这两个系统能否脱离场景在 EditMode 下单测（现在能，且必须保持能）。

### 因此必须删除的反向依赖

| 位置 | 内容 | 处置 |
|---|---|---|
| `../QuestSystem/QuestSystem.cs:26-33` | `AfterNew()` 里解析 `INpcSystem` 并订阅 `InteractionRequested` | 删除 |
| `../QuestSystem/QuestSystem.cs:150` | `Dispose()` 里的对应退订 | 删除 |
| `../QuestSystem/QuestNpcAdapter.cs` | 整个文件 | 删除 |

替代方案：NpcSystem 在演出结束后主动发布一条任务事件（§4.3）。方向从「任务系统偷听 NPC」翻转为「NPC 主动上报」，环随之消失。

顺带修掉 `QuestNpcAdapter.cs:16` 的既有缺陷——它拼的 `EventId` 是 `npc-interaction:{poiId}:{interactionId}`，跨整局恒定，会被幂等表当作重复事件丢弃，于是「与某人对话 3 次」永远卡在 1。新的事件模型没有幂等表（见 `../QuestSystem/QuestSystemDesign.md` §5.3）。

---

## 2. 当前实现与目标的差距

| 能力 | 现状 | 目标 |
|---|---|---|
| 会话串行化 | 已实现 `NpcSystem.cs:18-43` | 保留，转为内部机制 |
| 交互入口选择 | 缺失。`NpcDefinition` 只有一个 `defaultInteractionId` 字符串，全工程无人解释它 | 向 QuestSystem 查询对话绑定 |
| 演出 | 缺失。只发 `InteractionRequested` 事件，唯一订阅方是待删除的适配器 | 直接编排舞台与 `PlayAsync` |
| 头顶标记 | 缺失 | `NpcMarker` + 失效通知 |
| 接取任务 | 缺失 | 三条入口，§6 |
| 运行时状态 | `NpcRuntimeState`（`NpcContracts.cs:7`）从未被读写 | 转为 `npc.stage(id)` 投影的后备存储 |

文档层面的差距同样记一笔：本文件上一版描述的 `NpcEntity` / `NpcLogic` / `NpcInteractionCoordinator` 三个类**已在 POI 转 Mono 的重构中删除**，那份文档整体失效，本次为完全重写。

---

## 3. NPC 模型

### 3.1 静态定义

```csharp
public sealed class NpcDefinition : ScriptableObject
{
    string npcId;                  // 跨场景、存档、任务的稳定标识
    string displayName;            // 展示名（接入 TextMap 后改为文案 key）
    string idleStoryLocation;      // 兜底闲聊剧情的 YooAsset 地址
    ActorRefData actor;            // 该 NPC 在剧情舞台上的角色引用
}
```

`defaultInteractionId`（`NpcDefinition.cs:12`）删除——它是 Film 时代的遗留，语义是「默认演哪段」，但从来没有解释它的人。它的职责由 `idleStoryLocation` 与任务侧的对话绑定共同取代。

### 3.2 为什么对话分流不配在 NpcDefinition 上

原神的 `TalkExcelConfigData` 按任务组织，不按 NPC 组织。理由是纯粹的工程理由：

> **「这个 NPC 现在该说什么」是当前任务状态的函数，不是 NPC 的属性。**

若把分流表配在 NPC 上，新增一个任务就要回头改所有涉及的 NPC 资产，改动面随任务数线性增长，且两份配置必然漂移。配在任务步骤上，新增任务只动一个资产。

因此：**对话绑定 `DialogueBinding` 归 QuestSystem**（`../QuestSystem/QuestSystemDesign.md` §7.3），NpcDefinition 只提供无任务时的兜底闲聊。

代价：NpcSystem 必须能向 QuestSystem 发起一次查询。这正是 §1 里唯一允许的那条依赖边。

### 3.3 运行时状态

```csharp
public sealed class NpcRuntimeState
{
    int stage;      // 由任务的 SetNpcStageAction 写，经 npc.stage(id) 投影被表达式读取
}
```

`IsUnlocked`（`NpcContracts.cs:10`）删除——NPC 是否可交互已经由「有没有一条对话绑定命中」表达，再加一个布尔就有了两个真相源。

`stage` 保留，作为**任务条件里 `npc.stage("elder")` 的后备存储**。它是唯一属于 NpcSystem 的可存档状态。

---

## 4. 交互编排

### 4.1 时序

```
PoiMono.OnInteract()                                   PoiMono.cs:80
  └─> INpcSystem.InteractAsync(poi, ct)

  1. 会话串行化：已有活动会话则直接返回 Rejected
  2. 查询对话绑定
       IQuestSystem.ResolveDialogue(npcId)
         → { storyLocation, marker, bindingId } 或 空
       空则回落 NpcDefinition.idleStoryLocation
  3. 加载剧情图
       IAssetKit 按 location 异步加载 StoryGraph
       graph.BuildOrThrow()  →  IStoryAction root
  4. 进入舞台
       await using StageScope scope = await Stage.EnterAsync(graph.BuildStage(), services, ct)
       INarrativeSystem.Stage = scope
  5. 演出
       StoryResult result = await INarrativeSystem.PlayAsync(root, graph.StoryId, ct)
  6. 上报
       IQuestSystem.Emit("npc.talk_finished", { npcId, storyId, bindingId, result })
  7. 退出舞台（await using 自动逆序还原）、释放资源句柄、结束会话
```

第 4 步的 `await using` 是硬性要求：`StageScope`（`../NarrativeSystem/Stage/StageScope.cs:19`）把还原动作压栈、退出时严格逆序弹出，异常与取消路径同样走完整套还原。手写 try/finally 做不到这一点。

### 4.2 资源生命周期

剧情图按**地址**加载，不由 `NpcDefinition` 硬引用。原因是 `NpcDefinition` 是 `PoiConfig` 的直接字段（`../PoiSystem/Data/PoiConfig.cs:56`），随场景常驻；硬引用会把整棵剧情树连同它引用的 Timeline、动画、特效一起拖进场景包，全地图 NPC 的剧情会在进场景时全部加载。

句柄登记在本次交互的作用域内，第 7 步统一释放。这与 `../NarrativeSystem/NarrativeSystemDesign.md` §16 的约定一致。

### 4.3 演出结果如何推进任务

**不新增任何机制。** 复用 QuestSystem 已设计的「事件名 + 载荷」总线（`../QuestSystem/QuestSystemDesign.md` §5.1），NpcSystem 只是一个普通发布方：

| 事件名 | 载荷 | 时机 |
|---|---|---|
| `npc.talk_started` | `npcId`, `storyId` | 演出开始前 |
| `npc.talk_finished` | `npcId`, `storyId`, `bindingId`, `result` | 演出正常结束 |
| `npc.talk_aborted` | `npcId`, `storyId` | 演出被中止 |

任务步骤上的 `QuestTrigger` 按名字匹配、按 `filter` 表达式过滤载荷（例 `e.npcId == "elder"`），写入自己的变量，转移条件随之成立。**NarrativeSystem 全程不知道任务存在，QuestSystem 全程不知道 NPC 实体存在。**

### 4.4 剧情内部产生的分支结果

剧情图里的选项会写剧情变量（`flag.*` / `var.*`）。任务条件可以直接读它们——两个系统的变量存储互为投影（`../QuestSystem/QuestSystemDesign.md` §5.2），不需要 NpcSystem 转手搬运。

所以「玩家在对话里选了什么」不走事件载荷，走变量。**事件表达「发生了什么」，变量表达「现在是什么」**，两者不重叠。

### 4.5 取消

三个来源：玩家主动退出、NPC 所在 chunk 被卸载、系统释放。统一走同一个 `CancellationToken`——舞台还原、剧情中止、资源释放、会话关闭都挂在它上面，不存在第二条清理路径。

NPC 被 `PoiSystem` 回收时必须先取消会话再回收表现对象；顺序反了会让舞台在还原时拿到已销毁的 `Transform`。

---

## 5. 头顶标记

### 5.1 枚举

```csharp
public enum NpcMarker
{
    None,                  // 无
    QuestAvailable,        // 金色感叹号：可接主线 / 传说任务
    WorldQuestAvailable,   // 蓝色感叹号：可接世界任务
    QuestInProgress,       // 灰色：任务进行中，但当前步骤不指向本 NPC
    QuestTurnIn            // 问号：可交付 / 可推进
}
```

### 5.2 标记的来源

**标记不由 NpcSystem 计算。** 它是 `IQuestSystem.ResolveDialogue` 返回值的一个字段，与 `storyLocation` 同源。

理由：若标记和对话内容各算各的，就会出现「头顶挂着问号，点进去却是闲聊」这类不一致，而且这种缺陷只在特定任务状态组合下出现，测不出来。同一次解析同时产出两者，不一致在构造上不可能。

NpcSystem 只做两件事：缓存解析结果、转发失效通知。

```csharp
NpcMarker GetMarker(string npcId);        // 纯查询，读缓存
event Action<string> MarkerChanged;       // 参数为 npcId
```

失效由任务状态变化驱动（`../QuestSystem/QuestSystemDesign.md` §6 的帧末 flush 之后统一推一次），**不轮询**。

### 5.3 表现

`NpcMarkerMono` 挂在 NPC 的 POI 表现对象上，订阅 `MarkerChanged`，切换图标显隐。它属于表现层，不保存任何状态。

---

## 6. 接取任务的三条入口

原神里玩家可见的「接任务」有三种形态。本设计把它们收敛为三条路径，**其中两条不需要任何专门机制**：

### 6.1 对话中隐式接取（主线 / 传说，绝大多数）

任务的 `acceptMode = Auto`，`unlockCondition` 引用一个剧情旗标：

```
quest.q_wind_01.unlockCondition = flag.met_elder
```

剧情图里一个 `SetFlag("met_elder")` 节点就完成了接取。任务系统在帧末 flush 时发现条件成立，自动进入 `Active`。

**NpcSystem 不参与，QuestSystem 不认识剧情，NarrativeSystem 不认识任务。** 这是默认路径。

### 6.2 对话中显式接取（委托 / 可拒绝的任务）

任务的 `acceptMode = Manual`。对话绑定带一个 `offersQuestId`：

```csharp
public sealed class DialogueBinding
{
    // ...
    public string offersQuestId;   // 可空
}
```

演出结束且剧情变量 `var.accepted == true` 时，NpcSystem 调用一次 `IQuestSystem.Accept(offersQuestId)`。

这是 NpcSystem 唯一一处**写**任务状态的地方，其余全部是查询与事件上报。之所以允许，是因为「玩家在这次对话里点了接受」这个事实只有编排者知道。

### 6.3 任务面板接取

`QuestPanel` 直接调 `IQuestSystem.Accept(questId)`。与 NpcSystem 无关，列在这里只为说明入口是完备的。

---

## 7. 契约目标形态

```csharp
public interface INpcSystem : ISystemContract
{
    /// 头顶标记发生变化；参数为 npcId。
    event Action<string> MarkerChanged;

    /// 当前是否有活动交互会话。
    bool HasActiveInteraction { get; }

    /// 读取 NPC 当前的头顶标记。
    NpcMarker GetMarker(string npcId);

    /// 读取 NPC 阶段值，供任务表达式的 npc.stage(id) 投影使用。
    int GetStage(string npcId);

    /// 由任务的一次性动作写入 NPC 阶段值。
    void SetStage(string npcId, int stage);

    /// 执行一次完整交互：查询绑定、加载剧情、进入舞台、演出、上报、还原。
    UniTask<NpcInteractionResult> InteractAsync(PoiMono npc, CancellationToken cancellationToken = default);

    /// 中止当前交互；chunk 卸载与外部打断使用。
    void CancelInteraction();

    string CaptureSnapshot();
    void RestoreSnapshot(string json);
}
```

删除的成员及理由：

| 成员 | 理由 |
|---|---|
| `InteractionRequested` | 唯一订阅方是要删除的 `QuestNpcAdapter`；编排改在系统内部完成，不再需要把半成品状态广播出去 |
| `ActiveInteraction` | 外部只需知道「忙不忙」，暴露上下文会引诱外部自行编排 |
| `TryBeginInteraction` / `CompleteInteraction` | 开闭成对出现在 `InteractAsync` 内部，不再是公共契约。把「必须成对调用」的义务交给调用方本身就是设计缺陷 |

---

## 8. 存档

只存 `npcId → stage` 的偏离项，沿用工程约定的 `CaptureSnapshot() → JSON 字符串`：

```csharp
[Serializable] public sealed class NpcSnapshot
{
    public List<NpcStageEntry> stages;   // 只包含 stage != 0 的 NPC
}
```

未知 npcId（配置被删）记 Warning 并丢弃，不抛异常——与 `../QuestSystem/QuestSystemDesign.md` 铁律 Q5 一致。

活动交互会话**不入档**：读档后玩家总是站在世界里，不在对话中。

---

## 9. 分期

期号与 `../QuestSystem/QuestSystemDesign.md` §10 的任务系统期号独立，用 L（Loop）前缀区分。

| 期 | 内容 | 依赖 | 完成标志 |
|---|---|---|---|
| **L0** | 断开反向依赖：删 `QuestNpcAdapter` 与 `QuestSystem.AfterNew` 的订阅 | — | 编译通过，QuestSystem 零向外依赖 |
| **L1** | `DialoguePanel` 实现 `IDialogueView`；运行时 `StageServices` 装配 | — | 在 `MainWorld` 里能演一段 `StoryGraph` |
| **L2** | 任务内核（= 任务系统第 1 期） | — | EditMode 全测通过 |
| **L3** | `InteractAsync` 编排 + `ResolveDialogue` 查询 + 事件上报 | L1, L2 | **对话推进任务**，闭环主干贯通 |
| **L4** | 头顶标记 + HUD 任务追踪 + 地图指引点 | L3 | 玩家不看文档也知道去哪 |
| **L5** | 奖励落地到 `BagSystem` + 两份快照并入存档 | L3 | 读档后追踪与标记正确恢复 |
| **L6** | `IQuestGateway`，任务状态服务器权威 | L5 | — |

L1 与 L2 互不依赖，可并行。

**L1 是当前的硬性阻塞点**：`NarrativeSystem.cs:220` 在没有注入 `IDialogueView` 时直接抛 `InvalidOperationException`，而全工程唯一的实现是 Demo 场景里的 `SimpleDialogueView` 与测试用的 `NullDialogueView`。在 `MainWorld` 里现在调 `PlayAsync` 必然抛异常。

---

## 10. 闭环验收用例

「闭环」的定义就是下面这条路径能端到端跑通。它同时是 L3～L5 的验收用例。

| # | 步骤 | 涉及系统 |
|---|---|---|
| 1 | 走近长者 NPC，头顶显示金色感叹号 | Quest → Npc → 表现 |
| 2 | 交互，加载剧情图，进入舞台，播放对话 | Npc → Asset → Narrative |
| 3 | 对话中选「我去看看」，剧情写入 `flag.accepted_bottle` | Narrative |
| 4 | 帧末 flush，任务 `q_lost_bottle` 转入 `Active` 的第一步 | Quest |
| 5 | HUD 追踪条显示「前往风起地寻找酒瓶」，地图出现指引点 | Quest → UI |
| 6 | 玩家进入区域，发布 `world.entered_region`，第一步完成，转入第二步 | Poi → Quest |
| 7 | 长者头顶变为问号 | Quest → Npc → 表现 |
| 8 | 再次交互，命中第二步的对话绑定，演交付剧情 | Npc → Quest → Narrative |
| 9 | 上报 `npc.talk_finished`，任务转 `Completed`，抛 `RewardGranted` | Npc → Quest |
| 10 | 奖励适配器写入背包；存档、读档后追踪与标记均正确恢复 | Quest → Bag |

第 6 步需要一个区域触发器发布方。它归 `PoiSystem` 还是独立的区域系统，在 L4 之前决定，不影响闭环主干。

---

## 11. 已知限制

| 项 | 说明 | 缓解 |
|---|---|---|
| 单活动会话 | 同一时刻只能与一个 NPC 交互 | 与原神一致，不是限制 |
| 无「进范围自动触发」 | `DialogueBinding.beginWay` 的 `AutoOnEnter` 需要范围触发器，排在 L4 之后 | 主干只走 `Interact` |
| 标记只反映任务 | 商店、锻造等功能性标记未纳入 | 后续在 `ResolveDialogue` 之外并联一张功能表，不改本设计 |
| `displayName` 未本地化 | 当前是裸字符串 | 接 `TextMap` 时改为文案 key，与剧情共用一张表 |
| 剧情图加载有延迟 | 首次交互需要一次异步加载 | 放在舞台淡入期间，与 `StageSpec.Preload` 同一时机 |
