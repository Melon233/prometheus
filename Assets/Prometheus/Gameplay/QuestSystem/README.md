# QuestSystem（任务系统）

## 目标

推进线性任务：一个任务由若干**节点**顺序组成，当前节点的**全部目标**完成后进入下一个节点，最后一个节点完成即任务完成。
节点上可以挂**演出触发器**，决定「用什么方式播放哪段演出」。任务只存在于内存中，单机不存档（ARCH-NET-013），每次进入游戏从第一个节点开始。

## 数据结构

```
QuestCatalog (SO，地址 "QuestCatalog")
└─ autoAcceptQuests: List<QuestDefinition>     会话建立时自动接取（主线）

QuestDefinition (SO)
├─ questType                 目前只有 Main
├─ nameTextId / descTextId   TbText id
└─ nodes: List<QuestNodeDef>
     ├─ titleTextId          HUD 追踪条上显示的节点标题
     ├─ triggers             [SerializeReference] List<QuestCutsceneTrigger>
     │    ├─ OnEnterTrigger(cutscene)                         节点激活时播放（正在播放别的演出时排队）
     │    ├─ AreaTrigger(area, cutscene)                      首次进入区域时播放，每次节点激活只触发一次
     │    └─ InteractTrigger(anchorId, offset, radius, promptTextId, cutscene)
     │                                                        在锚点旁生成交互点，点击播放，可反复交互
     └─ objectives           [SerializeReference] List<QuestObjective>，全部完成才进入下一节点
          ├─ CutsceneObjective(cutscene)   看完演出（跳过也算）
          └─ ReachObjective(area)          进入区域（激活时已在区域内也算）

QuestArea
├─ anchorId     可选：区域跟随的 WorldAnchor；为空时 offset 即世界坐标
├─ offset       球心相对锚点的偏移（米，世界轴向）
└─ radius       球半径（米）
```

触发器只负责**开始**演出，是否算完成由 `CutsceneObjective` 判定。两者解耦，因此节点可以有「可反复观看但不影响推进」的闲聊演出。

**任务专属的区域与交互点都写在任务资产里**，场景中只需要被引用的 `WorldAnchor`（NPC、地标等世界内容，见 `Gameplay/WorldAnchorSystem/`）。加任务不改场景，删任务不留孤儿对象；同一个 NPC 可以同时被多个任务挂交互，交互栏各占一条。

## 运行链路

1. `AfterNewAsync`：按地址加载 `QuestCatalog`（ARCH-COMP-006）。
2. `AfterNew`：订阅 `CutsceneFinishedEvent`；接取目录中的自动任务。场景锚点已由先注册的 `WorldAnchorSystem` 索引，本系统不扫描场景。
3. 接取：`QuestDefinition.Validate` 失败直接抛出；进入第一个节点。
4. 进入节点：清空节点状态；`OnEnterTrigger` 的演出入队；检查区域引用的锚点存在；为每个 `InteractTrigger` 生成一个 `QuestInteractable`（挂到锚点下随之移动，带触发球作为交互范围）；发布 `QuestNodeChangedEvent`。
5. `OnUpdate`（演出播放中整体跳过）：
   1. 有排队演出时播放队首，本帧结束。
   2. 取当前上场角色位置（没有上场角色时跳过），按「到球心（锚点当前位置 + 偏移）的距离不超过半径」判定 `ReachObjective` 与未触发过的 `AreaTrigger`（区域跟随锚点移动）；触发了演出即结束本帧。
   3. 推进全部目标已完成的节点。
6. 演出结束（`CutsceneFinishedEvent`）：命中所有进行中任务当前节点里等待这段演出的 `CutsceneObjective`，然后推进完成的节点。
7. 交互点被点击：演出播放中忽略；否则播放该交互触发器配置的演出。
8. 离开节点：销毁该节点生成的交互点，并主动从玩家 `InteractComponent` 移除——Unity 销毁对象时不会发出 `OnTriggerExit`。
9. 最后一个节点完成：任务移出进行中列表，发布 `QuestCompletedEvent`。

### 对外状态与事件

| 名称 | 说明 |
| --- | --- |
| `IQuestSystem.ActiveQuests` | 全部进行中的任务进度（只读） |
| `IQuestSystem.TrackedQuest` | HUD 追踪的任务，当前取第一个 |
| `QuestNodeChangedEvent` | 进入新节点（含接取时的第一个节点） |
| `QuestCompletedEvent` | 任务完成 |

HudPanel 的任务追踪条（`QuestTrackerRoot` / `QuestTitleText` / `QuestStepText`）在打开时读取 `TrackedQuest`，并监听上面两个事件刷新；没有任务时隐藏。

## 依赖方向

任务 → 演出，单向。任务调用 `ICutsceneSystem.Play`，演出通过 `CutsceneFinishedEvent` 广播结束事实，**演出系统不认识任务**。
这是吸取旧任务/剧情系统互投影、需要重入守卫的教训（见 `Docs/Arch/GameFlow.md` 第 10 节）。

## 场景与编辑

- 场景中**只摆 `WorldAnchor`**：给 NPC、地标、广场中心等世界内容一个稳定 id，演出与任务共用。没有任何任务专属组件。
- 交互点是运行时对象 `QuestInteractable`，通过 `IInteractable` 接入玩家交互栏，与 POI 共用「感应进入 → 交互栏显示 → 点击」链路（见 `Gameplay/Common/Interaction/IInteractable.cs`）。
- **场景视图编辑**：在 Project 中选中任务资产，场景视图会按当前打开场景中的锚点画出各节点的区域与交互范围（都是球体线框），节点按颜色区分、标签形如「N1 区域触发」。拖动位置手柄改偏移、拖动球面上的半径拖点改半径，改动写回任务资产并支持撤销。引用的锚点不在当前场景时，在世界原点显示红色提示。

## 校验

- 运行时：接取任务前调用 `QuestDefinition.Validate`；进入节点时检查引用的锚点存在。失败均直接抛出。
- 编辑期：菜单 `Prometheus/Narrative/校验全部演出与任务`（资产结构），`Prometheus/Narrative/校验场景锚点`（全部演出与任务引用的锚点 id 与当前打开场景对照：缺失的报错并指向资产，未被引用的列出供清理）。
- `QuestType` 按下标序列化，已加入 `SerializedEnumLayoutTests` 黄金名单，只允许末尾追加。

## 测试数据

`Assets/BundleResources/Narrative/Quest_Prologue.asset`，在 `CutsceneTest` 场景中运行：

| 节点 | 触发器 | 目标 |
| --- | --- | --- |
| 0 与巴巴罗萨交谈 | OnEnter → `Cutscene_Intro` | 看完 `Cutscene_Intro` |
| 1 前往空地中央 | Area(锚点 `anchor_square` + (0, 1, 10)，半径 2) → `Cutscene_Center` | 到达同一区域 |
| 2 再次与巴巴罗萨交谈 | Interact(锚点 `npc_barbarossa` + (0, 1, 0)，半径 1.5) → `Cutscene_Return` | 看完 `Cutscene_Return` |

## 尚未实现

- 支线、日常任务（前置条件、刷新）与奖励发放。
- Talk / Kill / Collect 目标，需要等对应系统就绪。
- 多任务并行时的追踪切换（当前固定追踪第一个）。
