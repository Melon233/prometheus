# 任务系统对剧情闭环的对外接线

> 状态：设计稿
> 关系：`QuestSystemDesign.md` 描述任务系统**自身**的模型与内核；本文只描述它**对外**的四条接线，是前者 §7「与其他系统的对接」的展开
> 闭环的端到端定义与分期见 `../NpcSystem/NpcSystemDesign.md` §9 / §10
> 日期：2026-09-07

---

## 0. 任务系统在闭环里的位置

**任务系统是闭环的脊柱，但它谁也不认识。**

它只做四件对外的事，全部是被动的：

| # | 接线 | 方向 | 调用方 |
|---|---|---|---|
| 1 | `ResolveDialogue(npcId)` | 被查询 | NpcSystem |
| 2 | `Emit(name, payload)` | 被投喂 | 任意发布方（NpcSystem、PoiSystem、CombatSystem…） |
| 3 | 变量互投影 | 双向只读 | NarrativeSystem |
| 4 | 状态变化事件 | 被订阅 | NpcSystem、UI、存档 |

没有第五条。任务系统不持有任何玩法系统的引用，`AfterNew()` 里不解析任何其他系统。

### 铁律 Q6：任务系统零向外依赖

> **`QuestSystem` 不得通过 `Core.Gameplay.TryGetSystem` 解析任何其他 System。**

这是新增的第六条铁律（前五条见 `QuestSystemDesign.md` §13），也是 `../NpcSystem/NpcSystemDesign.md` 铁律 N1 的任务侧对偶。

它的可执行形式：`QuestSystem/` 目录下不出现 `Core.Gameplay`，可由架构测试断言（比照 `Sources_DoNotUseRequestChannelOutsideGateways` 的写法）。

---

## 1. 对 `QuestSystemDesign.md` §1 的一处修正

原文 §1 把「适配器模式：`QuestNpcAdapter` 把 NPC 事件翻译成任务事件」列为**现有实现做对的三件事之一，应当保留**。

**这条结论作废。** 保留的应当是「任务规则不侵入 NPC 逻辑」这个*目的*，而不是 `QuestNpcAdapter` 这个*实现*。

原因：闭环要求 NpcSystem 反向查询任务状态才能决定对话与标记（`../NpcSystem/NpcSystemDesign.md` §3.2）。这条边加上现有的 `QuestSystem → INpcSystem` 订阅，就是一个双向环。

翻转后目的完全保住：NPC 事件仍然只是「名字 + 载荷」，任务规则仍然不出现在 NPC 代码里，只是搬运方向从「任务系统拉」变成「NPC 系统推」。

| 位置 | 处置 |
|---|---|
| `QuestSystem.cs:26-33`（`AfterNew` 解析并订阅 `INpcSystem`） | 删除 |
| `QuestSystem.cs:150`（`Dispose` 中的退订） | 删除 |
| `QuestNpcAdapter.cs` | 删除整个文件 |
| `QuestEventAdapters.cs` | 删除整个文件（静态转换函数在「名字 + 载荷」模型下无存在意义，且 `PublishFilmCompleted` 引用的 FilmSystem 已不存在） |

---

## 2. 接线一：对话绑定解析

### 2.1 契约

```csharp
/// 解析一个 NPC 当前应当呈现的对话与标记。纯查询，无副作用。
/// 无任何绑定命中时返回 false，由调用方回落到 NPC 的兜底闲聊。
bool ResolveDialogue(string npcId, out QuestDialogueResolution resolution);

public readonly struct QuestDialogueResolution
{
    public string QuestId { get; }          // 命中绑定所属的任务
    public string BindingId { get; }        // 绑定的稳定标识，用于事件载荷回填
    public string StoryLocation { get; }    // StoryGraph 的 YooAsset 地址
    public NpcMarker Marker { get; }        // 头顶标记
    public string OffersQuestId { get; }    // 显式接取用；可空
}
```

### 2.2 解析规则

绑定来自两处（`QuestSystemDesign.md` §7.3 的 `DialogueBinding`）：

1. **活动步骤上的绑定**——仅该步骤是当前步骤时参与解析
2. **任务级绑定**——该任务处于 `Active` 期间一直参与解析

```
候选 = 全部活动任务的（活动步骤绑定 ∪ 任务级绑定）
     |> 筛选 npcId 匹配
     |> 筛选 condition 求值为真（condition 为空视为恒真）
     |> 筛选 once 且已执行过的排除
     |> 按 priority 降序，同 priority 按任务追踪状态优先，再按 questId 字典序
胜出者 = 候选首项
```

同 priority 时**先看是否为当前追踪任务**——这是原神的实际行为：玩家追踪哪条线，NPC 就优先说哪条线的话。字典序兜底只为让结果确定，不为表达意图。

### 2.3 标记与对话必须同源

`Marker` 是解析结果的字段，不是另算的。

若标记与对话各算各的，就会出现「头顶问号、点进去是闲聊」这类只在特定任务状态组合下复现的缺陷。同一次解析同时产出两者，不一致在构造上不可能。

标记值本身配在 `DialogueBinding.marker` 上，由任务作者显式声明——不从任务类型或步骤序号推导。推导规则看似省事，但「这一步该挂问号还是感叹号」是叙事决定，不是结构决定。

### 2.4 失效通知

```csharp
/// 任务状态变化后，可能受影响的 NPC 集合发生了变化。
event Action<IReadOnlyCollection<string>> DialogueBindingsInvalidated;
```

在帧末 flush 完成后（`QuestSystemDesign.md` §6）推一次，参数是本帧受影响的 npcId 集合。NpcSystem 据此让标记缓存失效。

**不提供轮询接口。** 这是铁律 Q4 在对外接线上的延伸。

---

## 3. 接线二：事件投喂

### 3.1 事件名注册表

`QuestSystemDesign.md` §5.1 把事件改成「名字 + 载荷」以消除封闭枚举，代价是拼错不编译报错。对策是一张集中的常量表，编辑期校验器据此检查触发器引用的名字（§9 第 8 条）。

```csharp
public static class QuestEventNames
{
    // NPC 域，发布方：NpcSystem
    public const string NpcTalkStarted  = "npc.talk_started";   // npcId, storyId
    public const string NpcTalkFinished = "npc.talk_finished";  // npcId, storyId, bindingId, result
    public const string NpcTalkAborted  = "npc.talk_aborted";   // npcId, storyId

    // 世界域，发布方：PoiSystem
    public const string PoiInteracted   = "world.poi_interacted";   // poiId, poiType
    public const string PoiUnlocked     = "world.poi_unlocked";     // poiId
    public const string EnteredRegion   = "world.entered_region";   // regionId

    // 战斗域，发布方：EntitySystem
    public const string EnemyDefeated   = "combat.enemy_defeated";  // id, count

    // 背包域，发布方：BagSystem
    public const string ItemAdded       = "bag.item_added";         // id, count
}
```

**这张表是唯一真相源。** 新增一种触发方式 = 加一个常量 + 一个发布方，任务内核零改动。

### 3.2 发布方的义务

| 义务 | 说明 |
|---|---|
| 载荷键名稳定 | 触发器的 `filter` 表达式用 `e.<键名>` 引用，改名等于改配置 |
| 不做去重 | 任务系统没有幂等表（`QuestSystemDesign.md` §5.3）。重复投递会重复计数，去重是发布方与网络层的责任 |
| 不在 flush 期间发布 | 事件进入 dirty 集合，由帧末统一处理；发布方无需关心时序，但也不得假设立即生效 |

---

## 4. 接线三：变量互投影

### 4.1 所有权

| 命名空间 | 存储归属 | 任务侧 | 剧情侧 |
|---|---|---|---|
| `quest.*` | 任务系统的 `StoryVariables` 实例 | 读写 | 只读 |
| `flag.*` `var.*` | `INarrativeSystem.Variables` | 只读 | 读写 |

两个存储各存各的档，互相注册为投影，不产生重复数据。

`StoryVariables.AddProjection`（`../NarrativeSystem/Core/StoryVariables.cs:31`）已经支持这个用法，因此**这条接线不需要改动 NarrativeSystem 一行代码**。

### 4.2 建立时机

投影必须在两个系统都完成 `AfterNew()` 之后建立，且**由 NpcSystem 之外的第三方建立**——任务系统不能持有 `INarrativeSystem` 引用（铁律 Q6）。

落点：组合根 `../../Bootstrap/PrometheusSystemInstaller.cs`。它已经是唯一知道具体 System 类型的地方，接线属于组合职责。

注册顺序约束：`NarrativeSystem` 必须先于 `QuestSystem` 注册（现状 `PrometheusSystemInstaller.cs:95-97` 已满足），投影在两者都注册完成后由安装器建立。

### 4.3 为什么不合并存储

合并成一个存储会更简单，但会让任务快照与剧情快照的边界消失：读一份档而不读另一份时，变量集是残缺的且无法判断残缺在哪。分开存储 + 互投影后，每份档自洽，缺失的投影表现为「路径解析失败」，由表达式求值器的既有容错路径处理。

---

## 5. 接线四：状态变化与追踪

```csharp
event Action<QuestChanged> QuestChanged;   // 状态、当前步骤、目标进度的合并通知
string TrackedQuestId { get; set; }        // 同时只追一个，与原神一致
```

消费方：

| 消费方 | 用途 |
|---|---|
| `UI/HudPanel/QuestTrackerMono` | 追踪条：当前步骤描述 + 目标进度 |
| `UI/QuestPanel` | 任务列表、详情、已完成归档 |
| `UI/MapPanel` | 地图指引点（经 `IQuestGuidePort` 解析出世界坐标，再经 `IWorldMapSystem.WorldToNormalized` 投影） |
| `NpcSystem` | 通过 §2.4 的失效通知间接消费 |

`QuestChanged` 是**合并通知**而非三个独立事件：状态、步骤、进度在同一次 flush 里可能一起变，分开发会让 UI 在一帧内重建多次。

---

## 6. 奖励落地

`QuestSystemDesign.md` §5.5 已定：任务系统不写背包，`GrantRewardAction` 抛 `RewardGranted`。

当前 `RewardGranted`（`IQuestSystem.cs:15`）**零消费方**。闭环要求补上：

```
QuestRewardAdapter（Ports/ 下）
  订阅 RewardGranted
  Item     → IBagSystem 写入
  Currency → 货币系统（尚不存在，先记 Warning）
  Experience → 成长系统（尚不存在，先记 Warning）
```

适配器持有 `IBagSystem`，任务系统不持有——依赖方向仍然是「适配器依赖两侧」，与 Gateway 的角色一致。

**服务器权威的接缝**：奖励最终必然由服务器发放。届时 `QuestRewardAdapter` 改为经 `IQuestGateway` 提交领取请求、等服务器回执后再写本地背包。适配器是唯一需要改的文件，这正是它存在的理由。

---

## 7. 本文涉及的改动清单

| 文件 | 动作 |
|---|---|
| `QuestNpcAdapter.cs` | 删除 |
| `QuestEventAdapters.cs` | 删除 |
| `QuestSystem.cs` | 删除 `AfterNew` 的 NPC 订阅与 `Dispose` 的退订 |
| `IQuestSystem.cs` | 新增 `ResolveDialogue` / `DialogueBindingsInvalidated` / `Emit` / `QuestChanged` / `TrackedQuestId`；删除 `PublishEvent` 与三个枚举事件 |
| `Events/QuestEventNames.cs` | 新建 |
| `Definitions/QuestBindings.cs` | 新建，含 `DialogueBinding` |
| `Ports/QuestRewardAdapter.cs` | 新建 |
| `../../Bootstrap/PrometheusSystemInstaller.cs` | 新增变量互投影的建立 |
| `QuestSystemDesign.md` | §1 第 2 条结论作废，指向本文 |

内核部分（`QuestDefinition` / `QuestStep` / `QuestTransition` / 表达式 / flush）的改动清单见 `QuestSystemDesign.md` §10，本文不重复。
