# 剧情系统在闭环中的接入

> 状态：设计稿
> 关系：`NarrativeSystemDesign.md` 描述剧情系统**自身**的模型与执行器（已实现）；本文只描述把它**接进玩法**所缺的东西，是前者 §15「与现有系统的集成」的落地清单
> 闭环的端到端定义与分期见 `../NpcSystem/NpcSystemDesign.md` §9 / §10
> 日期：2026-09-07

---

## 0. 结论先行：闭环不改剧情系统的契约

剧情系统在闭环里是**纯被调用方**：

```
NpcSystem ──> INarrativeSystem.PlayAsync(root, storyId, ct)
```

`INarrativeSystem`（`INarrativeSystem.cs`）已经具备闭环需要的全部能力——演绎、跳过、断点续演、已看过判定、存档、舞台外置。**本文不提议修改它的任何一个成员。**

缺的不是能力，是**接线**：剧情系统现在只能在 `Demo/NarrativeDemo.unity` 里跑，在 `MainWorld` 里跑不起来。

### 铁律 R6：剧情不认识任务

> **`NarrativeSystem/` 目录下不得出现 `Xuan.Prometheus.Quest` 或 `Xuan.Prometheus.Npc` 的任何类型。**

剧情影响任务的唯一通道是**变量**（`flag.*` / `var.*`，经互投影被任务条件读取）。剧情不调用任务 API，不发任务事件，不知道任务存在。

对偶规则见 `../QuestSystem/QuestLoopIntegration.md` 铁律 Q6 与 `../NpcSystem/NpcSystemDesign.md` 铁律 N1。

---

## 1. 阻塞点：在 MainWorld 里现在调 `PlayAsync` 必然抛异常

```csharp
// NarrativeSystem.cs:218-222
private StoryContext RequireContext()
{
    if (view == null) throw new InvalidOperationException(
        "NarrativeSystem requires a dialogue view before playing a story.");
    return EnsureContext();
}
```

全工程的 `IDialogueView` 实现只有两个：

| 实现 | 位置 | 用途 |
|---|---|---|
| `SimpleDialogueView` | `Demo/SimpleDialogueView.cs` | Demo 场景，运行时用 `GetComponent` 挂上 |
| `NullDialogueView` | `Dialogue/NullDialogueView.cs` | 空实现，仅供存档读写时占位 |

`PrometheusSystemInstaller.cs:95` 注册了 `NarrativeSystem`，但没有人调用 `SetView`。**这是闭环的第一个硬性阻塞点（L1）。**

---

## 2. 缺口一：`DialoguePanel`

### 2.1 定位

UI 层新目录 `../../UI/DialoguePanel/`，遵循既有的一面板一目录约定（`UI/BagPanel/`、`UI/HudPanel/` …）。

```
UI/DialoguePanel/
  DialoguePanel.cs        UIPanel 子类，实现 IDialogueView
  ChoiceOptionMono.cs     单个选项列表项，比照 InteractMono 的写法
```

### 2.2 为什么不直接提升 `SimpleDialogueView`

`SimpleDialogueView` 用代码构建整套 UI（Demo 需要「按下 Play 就能跑，不依赖任何预制体」）。正式面板走 `IUIKit.OpenPanel<T>()` + 预制体 + Binder 生成的 `DialoguePanelBase.g.cs`，两者的构造方式根本不同。

**两者都保留**：Demo 场景继续用 `SimpleDialogueView` 作为剧情系统的常驻测试场景（`NarrativeSystemDesign.md` §20 的测试策略依赖它）；正式流程用 `DialoguePanel`。共享的是 `IDialogueView` 契约，不是实现。

`SimpleDialogueView` 已经把两段点击、打字机补全、选项锁定态这三处易错逻辑跑通过一遍，`DialoguePanel` 照它的语义实现即可——契约注释（`Dialogue/IDialogueView.cs:19-31`）已经把这些约束写死。

### 2.3 注入时机

`DialoguePanel` 由 `IUIKit` 在打开时创建，而 `SetView` 要求在首次演绎之前注入且演绎期间不可替换（`NarrativeSystem.cs:102`）。

因此注入发生在 **NpcSystem 编排交互的第 4 步之前**：打开面板 → `SetView` → 进舞台 → 演出 → 退舞台 → 关面板。面板的开闭与舞台作用域同生命周期，挂在同一个 `await using` 上。

---

## 3. 缺口二：运行时端口

`Ports/` 下现有 5 个场景端口全部是 `MonoBehaviour`，且依赖 Inspector 里预先登记的条目表：

| 端口 | 类 | 解析方式 |
|---|---|---|
| 角色 | `SceneActorResolver` | Inspector 上的 `SceneActorEntry` 列表 |
| 镜头 | `SceneCameraPort` | Inspector 上的 `SceneCameraEntry` 列表 |
| 资源 | `SceneAssetPort` | Inspector 上的 `SceneAssetEntry` 列表 |
| 特效 | `PrefabVfxPort` | 转发给资源端口 |
| 世界 | `SceneWorldPort` | 场景内直接查找 |
| 屏幕 | `NarrativeScreenView` | 自建 UI |

**这些是 Demo 端口，不是运行时端口。** 它们要求每段演出的参演对象都预先摆在场景里并手工登记，这在开放世界里不成立。

`NarrativeSystemDesign.md` §15 已经指明了运行时端口应当接什么，本文只把它变成文件清单：

| 端口 | 运行时实现 | 接到 |
|---|---|---|
| `IActorResolver` | `Ports/Runtime/GameplayActorResolver.cs` | `IEntitySystem` 查活动实体，`ITeamSystem` 解析 `ActiveMember` / `TeamSlot`，NPC 经 `PoiMono` |
| `INarrativeCameraPort` | `Ports/Runtime/GameplayCameraPort.cs` | `ICameraSystem.AcquireCutsceneCamera`，租约挂 `StageScope` |
| `INarrativeAssetPort` | `Ports/Runtime/AssetKitPort.cs` | `IAssetKit` 按 location 异步加载，句柄登记进作用域 |
| `INarrativeVfxPort` | 复用 `PrefabVfxPort` 的逻辑，改为非 Mono | 底层走 `EffectSystem` |
| `INarrativeWorldPort` | `Ports/Runtime/GameplayWorldPort.cs` | HUD 显隐经 `IUIKit`，输入锁经 `IInputSystem` 的 `InputContexts.Cutscene` 租约 |
| `INarrativeAudioPort` | 已有 `FmodNarrativeAudioPort`（`Ports/NarrativeAudioPorts.cs:81`） | 直接可用 |

音频端口是唯一已经具备运行时形态的——它走 `FmodAudioRuntime`（`../Audio/FmodAudioRuntime.cs`），不依赖场景登记。

### 3.1 谁装配 `StageServices`

`StageServices`（`Stage/StageServices.cs:11`）是一个纯数据容器，构造时需要 `INarrativeScreen` 与 `IActorResolver`，其余端口是可选属性。

装配落在 **NpcSystem**，而不是 NarrativeSystem 自己：端口的实现要引用 `IEntitySystem`、`ICameraSystem`、`IInputSystem`、`IUIKit`，让 NarrativeSystem 装配它们等于让它认识半个玩法层，违反铁律 R6 的精神。

编排者装配自己下单时要用的服务，这是一致的。

### 3.2 `Ports/` 目录的划分

```
Ports/
  Scene/      现有 5 个 MonoBehaviour 端口，Demo 场景专用
  Runtime/    新增，接玩法系统
  NarrativeAudioPorts.cs   两个实现都与场景无关，留在原地
```

这次移动会改动 `NarrativeDemo.unity` 里的组件引用——`.meta` 随文件一起移动即可保住 GUID（`ARCH-UNITY-001`），场景不会断链。

---

## 4. 缺口三：`quest.*` 变量投影

任务条件读 `flag.*` / `var.*`，剧情条件读 `quest.*`。两个存储互为投影，各存各的档。

`StoryVariables.AddProjection`（`Core/StoryVariables.cs:31`）已经支持这个用法，注释里给的例子恰好就是这个场景。**因此这条接线不需要改动剧情系统一行代码。**

建立时机与落点见 `../QuestSystem/QuestLoopIntegration.md` §4.2：由组合根 `PrometheusSystemInstaller` 在两个系统都注册完成后建立。

---

## 5. 舞台作用域的所有权

`INarrativeSystem.Stage`（`INarrativeSystem.cs:42`）是一个可写属性，注释明确要求「调用方用 `await using` 进入舞台后写入本属性，退出作用域前置空」。

闭环里这个调用方就是 NpcSystem。所有权链条：

```
NpcSystem.InteractAsync
  ├─ await using StageScope scope = await Stage.EnterAsync(spec, services, ct)
  ├─ narrative.Stage = scope
  ├─ await narrative.PlayAsync(root, storyId, ct)
  └─ narrative.Stage = null        // 作用域退出前
     scope 自动逆序还原：镜头租约、输入租约、HUD、黑边、角色动画通道、运行时根节点
```

**剧情系统不创建也不销毁舞台。** 纯对话剧情可以不进舞台，此时任何依赖舞台的动作会给出明确错误——这是设计意图，不是缺陷。

---

## 6. 资源生命周期

`NarrativeSystemDesign.md` §16 已定：定义资产不硬引用大资源，只存 location；句柄登记在 `StageScope`，退出时统一释放。

闭环新增一条：**`StoryGraph` 资产本身也按 location 加载。**

原因在 `../NpcSystem/NpcSystemDesign.md` §4.2：`NpcDefinition` 是 `PoiConfig` 的直接字段，随场景常驻，硬引用 `StoryGraph` 会把全地图 NPC 的剧情树在进场景时一次性拖进内存。

`StoryGraph` 句柄与舞台句柄同期释放。

---

## 7. `Demo/` 的处置

**保留，不动。**

`Demo/NarrativeDemo.unity` 是剧情系统的常驻测试场景：不依赖 Core 启动流程，按下 Play 就跑，覆盖对话、选项、Timeline、跳过、存档续演。它的价值不随闭环接入而下降——闭环让剧情能在真实世界里跑，Demo 让剧情能在**没有真实世界**的情况下跑，两者互补。

唯一的改动是 §3.2 的目录移动。

---

## 8. 本文涉及的改动清单

| 文件 | 动作 |
|---|---|
| `../../UI/DialoguePanel/DialoguePanel.cs` | 新建，实现 `IDialogueView` |
| `../../UI/DialoguePanel/ChoiceOptionMono.cs` | 新建 |
| `Ports/Scene/` | 新建目录，移入现有 5 个场景端口（连 `.meta` 一起移动） |
| `Ports/Runtime/GameplayActorResolver.cs` | 新建 |
| `Ports/Runtime/GameplayCameraPort.cs` | 新建 |
| `Ports/Runtime/AssetKitPort.cs` | 新建 |
| `Ports/Runtime/GameplayVfxPort.cs` | 新建（由 `PrefabVfxPort` 去 Mono 化而来） |
| `Ports/Runtime/GameplayWorldPort.cs` | 新建 |
| `INarrativeSystem.cs` / `NarrativeSystem.cs` / `Core/` / `Actions/` | **不改** |
| `Demo/` | 不改 |

剧情系统本体零改动，这是它 `Core/` + `Ports/` 分层设计得当的直接证据——接入一个新宿主只需要新增端口实现。
