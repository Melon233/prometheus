# 剧情系统在闭环中的接入

> 状态：**L1 已实现**（2026-09-08）；本文同时是设计说明与落地记录
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

> **`NarrativeSystem/` 下除 `Ports/Runtime/` 外，不得出现 `Xuan.Prometheus.Quest` 或 `Xuan.Prometheus.Npc` 的任何类型；
> `Ports/Runtime/` 作为适配层可以认识玩法系统，但整个 `NarrativeSystem/` 目录**在任何位置**都不得出现 Quest 类型。**

剧情影响任务的唯一通道是**变量**（`flag.*` / `var.*`，经共享的 `VariableStore` 被任务条件读取）。剧情不调用任务 API，不发任务事件，不知道任务存在。

> **修订记录（2026-09-08）**：本条最初写作「`NarrativeSystem/` 目录下不得出现 Quest 或 Npc 的任何类型」，
> 但 L1 落地时 `Ports/Runtime/GameplayActorResolver` 必须读 `PoiConfig.Npc.NpcId` 才能把 `ActorKind.Npc` 解析成场景对象，
> 直接违反了原文。原文把「内核」和「适配层」混为一谈——**适配层认识两侧正是它存在的理由**，
> 一条禁止适配器认识被适配对象的规则是写错了，不是实现写错了。现按上文重新划界：内核零业务依赖，适配层只对 Quest 封闭。

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

装配落在适配层的 `Ports/Runtime/NarrativeRuntimePorts`，由 `NarrativePlayback` 在每次演出开始时构造、结束时释放。

**不放在剧情内核**：端口实现要引用 `ICameraSystem`、`IInputSystem`、`ITeamSystem`、`IPoiSystem`，
让 `NarrativeSystem.cs` 装配它们等于让内核认识半个玩法层。

**也不放在 NpcSystem**（本文早期版本的方案）：那样每个想演一段剧情的调用方都要自己重复一遍装配，
而装配顺序、释放顺序、特效兜底父节点这些细节全是可以出错的地方。收在一处后，
第三期 NpcSystem 的交互编排只需调用 `NarrativePlayback.PlayAsync`。

### 3.2 `Ports/` 目录的划分

```
Ports/
  Scene/      现有 5 个 MonoBehaviour 端口，Demo 场景专用
  Runtime/    新增，接玩法系统
  NarrativeAudioPorts.cs   两个实现都与场景无关，留在原地
```

这次移动会改动 `NarrativeDemo.unity` 里的组件引用——`.meta` 随文件一起移动即可保住 GUID（`ARCH-UNITY-001`），场景不会断链。

---

## 4. 缺口三：`quest.*` 与 `flag.*` 的互读

> **修订记录（2026-09-10，P1）**：本节原先描述的是「两个存储互为投影」，该模型已被单一 `VariableStore` 取代。

任务条件读 `flag.*` / `var.*`，剧情条件读 `quest.*`。两边写进**同一份** `VariableStore`，
各自只拥有自己的根段：`StoryVariables` 拥有 `flag` 与 `var`，`QuestVariables` 拥有 `quest`。

因此谁都读得到谁，却没有人需要认识谁——所有权只由路径根段决定，铁律 R6 不受影响。
存储由组合根创建并构造注入（见 `../QuestSystem/QuestLoopIntegration.md` §4.2），**不需要任何交叉接线**。

快照仍按所有者切片，因此剧情档仍只含 `flag.*` / `var.*`。

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

| 文件 | 动作 | 状态 |
|---|---|---|
| `Ports/Scene/` | 新建目录，移入 5 个场景端口（`.meta` 一起移动，GUID 已核对与 HEAD 一致） | 完成 |
| `Ports/Runtime/NarrativeRuntimePorts.cs` | 新建：装配全套端口为一份 `StageServices` | 完成 |
| `Ports/Runtime/NarrativeRuntimeScreen.cs` | 新建 | 完成 |
| `Ports/Runtime/GameplayActorResolver.cs` | 新建 | 完成 |
| `Ports/Runtime/GameplayCameraPort.cs` | 新建 | 完成 |
| `Ports/Runtime/AssetKitPort.cs` | 新建 | 完成 |
| `Ports/Runtime/NarrativeVfxPort.cs` | 新建（由 `PrefabVfxPort` 去 Mono 化而来） | 完成 |
| `Ports/Runtime/GameplayWorldPort.cs` | 新建；`FreezeAi` 未实现，见 §3 | 完成 |
| `NarrativePlayback.cs` | 新建：演一段剧情图的统一入口 | 完成 |
| `NarrativeEvents.cs` | 新建：`NarrativeHudVisibilityEvent` | 完成 |
| `Dialogue/IDialogueHost.cs` | 新建：对话界面宿主端口 | 完成 |
| `../../UI/DialoguePanel/DialoguePanel.cs` | 新建，实现 `IDialogueView` | 完成 |
| `../../UI/DialoguePanel/DialoguePanelBase.g.cs` | 由 UIKit 代码生成器产出 | 完成 |
| `../../UI/DialoguePanel/ChoiceOptionMono.cs` | 新建 | 完成 |
| `../../UI/DialoguePanel/DialoguePanelHost.cs` | 新建：`IDialogueHost` 的 UI 侧实现 | 完成 |
| `../../UI/HudPanel/HudPanel.cs` | 订阅 `NarrativeHudVisibilityEvent` 并在 `OnUnbind` 退订 | 完成 |
| `../../Bootstrap/PrometheusSystemInstaller.cs` | 安装阶段按地址加载全局文案表 | 完成 |
| `INarrativeSystem.cs` / `NarrativeSystem.cs` / `Core/` / `Actions/` / `Demo/` | **不改** | — |

剧情系统本体零改动（`NarrativeSystem.cs` 只多了一个文案表地址常量），这是 `Core/` + `Ports/` 分层设计得当的直接证据——接入一个新宿主只需要新增端口实现。

---

## 9. L1 落地时新增的资产与两处取舍

### 9.1 资产

| 资产 | 地址 | 用途 |
|---|---|---|
| `Assets/BundleResources/Narrative/NarrativeText.asset` | `NarrativeText` | 全局文案表，安装阶段一次性载入 |
| `Assets/BundleResources/Narrative/StoryDemoGreet.asset` | `StoryDemoGreet` | 冒烟剧情：两句台词 + 一次二选一分支 |
| `Assets/BundleResources/UI/Dialogue/Prefabs/DialoguePanel.prefab` | `DialoguePanel` | 对话面板预制体 |

**收集器必须显式登记**：`BundleCollectorSetting.asset` 的根收集器 `Assets/BundleResources` 用的是
`FilterRuleName: CollectPrefab`——**只收预制体**。ScriptableObject 必须单独加一条 `CollectAll` 的收集器条目，
`Config/Effect/EffectLibrary.asset` 就是这个先例。本次为 `Assets/BundleResources/Narrative` 增加了一条同类条目；
漏掉它的表现是运行期 `Location is invalid: 'NarrativeText'`，而不是编辑期报错。

冒烟剧情的舞台声明刻意关掉了 `takeCameraControl`、参演角色留空，因此它不依赖场景里存在任何 NPC 或具名机位，
可以在 `MainWorld` 里独立验证「对话能演出来」这一件事。

### 9.2 取舍一：正文用 uGUI `Text` 而不是 TMP

工程内唯一的 TMP 字体资产是 `LiberationSans SDF`，不含中文字形，中文台词会整片显示为方块。
`NarrativeUiFactory` 早就为此改用了动态系统字体，本面板沿用同一条路径：预制体里绑的是 `UnityEngine.UI.Text`，
字体在 `OnInitialize` 用 `NarrativeUiFactory.ResolveFont()` 解析。

这是**临时取舍，不是终态**。补一个带中文字形的 TMP 字体资产后应整体换回 TMP——届时只需改预制体与重新生成
`DialoguePanelBase.g.cs`，面板逻辑与剧情系统都不受影响。

### 9.3 Play 模式实测记录（2026-09-08）

在 `MainWorld` 里对 `StoryDemoGreet` 走了一遍完整链路，逐项确认：

| 环节 | 结果 |
|---|---|
| 启动加载全局文案表 | 7 条，语言 `zh-CN` |
| 按地址加载剧情图、构建剧情树 | 通过 |
| 打开 `DialoguePanel` 并注入视图 | Popup 层出现面板 |
| 进入舞台 | `HudPanel` 被隐藏（`NarrativeHudVisibilityEvent` 往返成立） |
| 台词显示 | 说话人「蒙德长者」、正文中文正常，字体解析为 `Microsoft YaHei UI` |
| 点击推进到第二拍 | 通过 |
| 选项渲染 | 两项克隆自模板，模板自身保持 inactive |
| 选中分支演绎 | 通过 |
| 演出结束还原 | `[NarrativePorts]` / `[NarrativeScreen]` / `[NarrativeCutsceneCamera]` 全部销毁，面板进入 Cache 层，**HUD 恢复显示** |
| 已看过集合 | `HasSeen("story.demo.greet")` 为真，跳过随之开放 |

调用方是通过反射直接调 `NarrativePlayback.PlayAsync`——玩法内的正式触发点属于 L3 的 `NpcSystem.InteractAsync`，本期不引入临时入口。

### 9.4 取舍二：确认输入只走全屏按钮

面板顶层压了一个透明的全屏 `AdvanceBtn` 承接推进点击，没有采样键盘。
原因是 `GameplayWorldPort` 在演出期间只锁 `InputActionMask.Gameplay`，键盘确认要走 `IInputSystem` 的
Navigation 动作才符合工程的输入仲裁约定，而直接采样 `Keyboard.current`（演示视图的做法）会绕过它。
本项目主目标平台是 Android，全屏点击已经是完整交互；键盘确认留到接 Navigation 动作时一并补。
