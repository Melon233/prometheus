# 任务系统对剧情闭环的对外接线

> 状态：**已实现**（2026-09-08）。四条接线全部落地，端到端在 Play 模式验证通过；未实现的部分逐条标在 §7
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
| 3 | 共享变量存储 | 双向只读 | 组合根注入，双方都不认识对方 |
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

| 位置 | 处置 | 状态 |
|---|---|---|
| `QuestSystem.cs` `AfterNew` 解析并订阅 `INpcSystem` | 删除（`AfterNew` 重载随之整体消失） | **已完成 2026-09-08** |
| `QuestSystem.cs` `Dispose` 中的退订与两个字段 | 删除 | **已完成 2026-09-08** |
| `QuestNpcAdapter.cs` | 删除整个文件 | **已完成 2026-09-08** |
| `QuestEventAdapters.cs` | 删除整个文件（静态转换函数在「名字 + 载荷」模型下无存在意义，且 `PublishFilmCompleted` 引用的 FilmSystem 已不存在；删除前确认全工程零调用方） | **已完成 2026-09-08** |

结果：`QuestSystem/` 目录下已无任何 `Core.Gameplay` 调用，铁律 Q6 当前成立。

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

## 4. 接线三：共享变量存储

> **修订记录（2026-09-10，P1）**：本节原先描述的是「两个存储互相注册为投影」。
> 该模型已被单一 `VariableStore` 取代，原因见 4.4。

### 4.1 所有权

会话内只有**一份** `VariableStore`（`../Expression/VariableStore.cs`），按路径**根段**路由到命名空间所有者：

| 命名空间 | 所有者 | 任务侧 | 剧情侧 |
|---|---|---|---|
| `quest.*` | `QuestVariables`（`Core/QuestVariables.cs`） | 读写 | 只读 |
| `flag.*` `var.*` | `StoryVariables`（`../NarrativeSystem/Core/StoryVariables.cs`） | 只读 | 读写 |

所有者**不持有存储**：值统一存在 `VariableStore` 的一张字典里（路径自带根段，天然唯一）。
所有者只回答本命名空间独有的三件事——拥有哪些根、未存值时怎么现算、算不出时缺省是什么。

写权限由所有者的门面强制：`QuestVariables.Set` 拒绝非 `quest.*` 的路径，`StoryVariables.Set` 拒绝非 `flag.*`/`var.*` 的路径。

### 4.2 建立时机

组合根创建 `VariableStore` 并**构造注入**给两个系统，两个系统在自己的构造函数里把命名空间登记进去：

```csharp
VariableStore variables = new VariableStore();
registry.AddSystem<INarrativeSystem>(new NarrativeSystem(variables));
registry.AddSystem<IQuestSystem>(new QuestSystem(variables));
```

**没有任何交叉接线**。原先的三行（两次 `AddProjection` + 一次 `Changed` 转发）整体消失：
任务系统订阅的是整个存储的变化通知，剧情旗标的改动因此自动弄脏引用它的任务，不需要谁替谁转发。

注册顺序也不再有约束——登记发生在构造期，等到 `AfterNew` 注册任务目录做校验时，全部根段必然已知。

### 4.3 快照仍然分开

存储是共享的，**档不是**：`Capture(owner)` 按所有者切片导出，因此任务快照仍只含 `quest.*`，剧情快照仍只含 `flag.*`/`var.*`。
读一份档而不读另一份时，每份仍然自洽，缺失的一侧表现为路径解析失败，由表达式求值器的既有路径处理。

### 4.4 为什么放弃互投影

互投影在图上是一个**环**：解析 `flag.x` 走到剧情存储，剧情存储的投影里又有任务存储，于是无限递归（实测栈溢出）。
它只能靠一个不重入守卫勉强成立，而守卫的存在本身就是模型错误的证据（`ARCH-META-001`）。
此外两个存储的字典、快照、清理代码约 85% 逐行重复。

按根段路由之后：解析 = 取根段 + 一次字典查找，图上没有环，守卫与全部投影代码一并删除。

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

| 文件 | 动作 | 状态 |
|---|---|---|
| `QuestNpcAdapter.cs` | 删除 | 完成（L0） |
| `QuestEventAdapters.cs` | 删除 | 完成（L0） |
| `QuestSystem.cs` | 删除 `AfterNew` 的 NPC 订阅与 `Dispose` 的退订 | 完成（L0） |
| `QuestSystemDesign.md` | §1 第 2 条结论作废，指向本文 | 完成（L0） |
| `IQuestSystem.cs` | 新增 `ResolveDialogue` / `DialogueBindingsInvalidated` / `Emit` / `QuestChanged` / `TrackedQuestId`；删除 `PublishEvent` 与三个枚举事件 | 待 L2 |
| `Events/QuestEventNames.cs` | 新建 | 待 L2 |
| `Definitions/QuestBindings.cs` | 新建，含 `DialogueBinding` | 待 L2 |
| `Ports/QuestRewardAdapter.cs` | 新建 | 待 L5 |
| `../../Bootstrap/PrometheusSystemInstaller.cs` | 新增变量互投影的建立 | 完成（L2） |

### 7.1 实现时相对本文的三处偏离

| 项 | 设计原文 | 实际实现 | 原因 |
|---|---|---|---|
| ~~互投影的环~~ | 只说「两边互相注册为投影」 | ~~`QuestVariables` 增加了一个**不重入**保护~~ | **已于 P1 消除**：改为单一 `VariableStore` 按根段路由，图上不再有环，守卫已删除（见 §4.4） |
| ~~`quest.*` 的兜底顺序~~ | 未规定 | ~~未写入的 `quest.*` 读作 0 的兜底**排在查投影之前**~~ | **已于 P1 消除**：不再有投影，缺省值由 `quest` 根段的所有者独家回答，顺序问题不存在 |
| 失效通知的来源 | 只列了变量变化 | 追加了「注册任务配置」这一条 | 新注册的任务带来新的对话绑定，NPC 标记随之改变。漏掉它的表现是：启动后第一次看 NPC 头顶是空的，直到任何一个变量恰好变化才刷新 |

### 7.2 尚未实现

| 项 | 期 | 说明 |
|---|---|---|
| `QuestRewardAdapter` | L5 | `RewardGranted` 目前仍**零消费方**：任务完成会发出奖励声明，但没有人写进背包 |
| ~~区域触发器~~ | ~~L4~~ | **已实现**：`../PoiSystem/RegionTriggerMono` 用触发器碰撞体在玩家进入时投喂事件；场景里尚未摆放任何区域，验收时仍可用一条手动 `Emit` 代替 |
| `QuestChapter` / 限时 / 每日委托 | 7 | 见 `QuestSystemDesign.md` §10 |
| 世界绑定 `WorldGroup` / `WorldSuite` | 4 | 本期只实现了**对话绑定**这一种声明式绑定 |
| 任务界面 | — | 按需求明确不做；追踪条已能覆盖「玩家知道现在该干什么」这件事 |

### 7.3 L4 实现时踩到的一处 Unity 约束

`QuestCatalog` 最初和 `DialogueBinding` 同放在 `QuestBindings.cs` 里。代码能编译、`CreateInstance` 也能建出资产，
但那份 `.asset` 在下一次域重载后主资产变成 **null**，YooAsset 的模拟构建随之报 `Found invalid asset`，整个启动流程挂掉。

原因是 Unity 每个 `.cs` 文件只产出一个与**文件名同名**的 `MonoScript`，而 ScriptableObject 资产序列化的
`m_Script` 指向的正是它。类名与文件名不一致时，资产反序列化找不到脚本。

因此 **每个 ScriptableObject 子类必须独占一个同名文件**。`QuestCatalog` 已拆到 `QuestCatalog.cs`。
这条约束对普通 `[Serializable]` 类不适用，所以 `DialogueBinding` 留在原文件没有问题。

内核部分（`QuestDefinition` / `QuestStep` / `QuestTransition` / 表达式 / flush）的改动清单见 `QuestSystemDesign.md` §10，本文不重复。
