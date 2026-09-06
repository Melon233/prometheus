# NarrativeSystem

剧情与演出系统。完整设计见 [NarrativeSystemDesign.md](NarrativeSystemDesign.md)。

当前实现范围：**第 0~5 期**（地基 / 对话 / 舞台与表现 / Timeline 演出片段 / 跳过·续演·预览·存档 / 图资产与编辑器）。
第 6 期（任务系统对接与世界状态联动）尚未实现。

---

## 演示场景

`Demo/NarrativeDemo.unity` —— 打开后直接按 Play 即可运行，不需要 Core 启动流程、不依赖 YooAsset。
该场景是剧情与演出系统的常驻测试场景，后续阶段的能力应继续在此验证。

| 操作 | 作用 |
|---|---|
| 鼠标左键 / 空格 / 回车 | 打字机进行中：立即显示全文；全文已显示：推进到下一节拍 |
| `R` 或「重播」 | 复位变量，重新进入舞台演绎 |
| `A` 或「自动播放」 | 点击推进降级为按文本长度自动推进 |
| `S` 或「跳过」 | 剩余节点全部走 `Settle`，观察物件是否直接落到终态 |
| `Esc` | 中止（区别于跳过：不补齐剩余状态，并把当前节拍记为续演点） |
| `F5` 或「存档」 | 捕获剧情存档（变量、已选选项、选择结果、已看过剧情、续演点） |
| `F9` 或「读档续演」 | 读档并从续演点继续；存档无续演点时从头演绎 |

**跳过是有权限的**：第一次演绎时按 `S` 会被系统拒绝（提示「该剧情还没完整看过一遍」），
完整看完一遍之后再按才生效——与原神一致。

演示剧情 `Demo/DemoStory.cs` 覆盖第 0~3 期的全部表现能力（第 4 期的跳过 / 续演 / 存档由控制条按钮驱动）：

- 叙述行、台词、并发表现（`.With`）、阻塞表现（`.Await`，期间点击被吞）
- 并发组合子、分离组合子（承载不会自然结束的循环动画）
- 自动推进、带条件与一次性限制的选项、锁定态选项、条件分支、变量写入
- 舞台接管：淡入淡出、黑边、输入锁、镜头接管、角色摆位
- 镜头切换与混合、特效生成、音效、Spine 角色动画
- **一段 Timeline 演出被 cue 切成两半，中间插一句对话**

演示场景中的实测结果（自动播放跑完一遍）：

| 检查点 | 结果 |
|---|---|
| 演出片段第一段停在 `cue_mid` | 祭坛 y = 1.40（cue 处关键帧值） |
| 对话后第二段从原处续演 | 祭坛 y = 2.10、旋转 220°（片段末尾值） |
| 舞台退出后角色还原 | 主角、长老、祭坛、相机全部回到进入前的位姿 |
| 舞台退出后运行时对象 | `[NarrativeStage]` 根节点已销毁，无残留 director / 特效 |
| 首次演绎按跳过 | 被拒绝：「该剧情还没完整看过一遍，不能跳过」 |
| 完整看完一遍后按跳过 | 生效，结果为 `Skipped`，变量与完整演绎一致 |
| 中途 `Esc` 中止 | 续演点记为 `demo_altar/elder_first`，世界完整还原 |
| 读档续演 | 直接从 `elder_first` 开始，之前节拍只落终态：主角与长老已就位、镜头已在 `cam_altar` |

> **演示场景的已知告警**：`Problematic material setup at ElderSpine: Premultiply-alpha atlas textures not supported in Linear color space`。
> 这是所选 Spine 图集用了预乘 alpha、而工程是 Linear 色彩空间导致的**美术资源配置**问题，只影响该角色的显示效果，与剧情系统无关。
> 修复方式是把该图集材质的 `Straight Alpha Texture` 勾上或重导出为直通 alpha；因为它是多处共用的第三方美术资源，这里没有擅自改动。

---

## 代码结构

| 目录 | 内容 |
|---|---|
| `Expression/` | 条件表达式的词法、语法与求值。**零 Unity 依赖**，为将来服务端权威求值预留 |
| `Text/` | `TextKey` / `ITextMap` / `TextMap` / `TextMapAsset` 本地化文本表 |
| `Core/` | `IStoryAction` 原语、`StoryAction` 基类、`StoryPath`、`StoryTree`、`StoryVariables`、`StoryContext`、`StoryRunner` |
| `Actions/` | 组合子与全部能力叶子：流程、屏幕、镜头、特效、音频、角色、动画 |
| `Dialogue/` | `Beat` 节拍、`Choose` 选项、`IDialogueView` 表现端口 |
| `Actors/` | `ActorRef`、`ActorHandle`、`IActorResolver`、`Anchor` |
| `Stage/` | 能力端口定义、`StageSpec`、`StageScope`、`Stage`、`ActorAnimationHandover` |
| `Cinematic/` | `CinematicAction`、`CinematicCueMarker`、自研 `StorySpineTrack` |
| `Ports/` | 端口的场景注册表实现，可直接用于测试场景与轻量关卡 |
| `Core/NarrativeSnapshot.cs` | 存档格式与往返；**不含任何演出播放进度** |
| `Demo/` | 演示场景、演示剧情、运行时构建的对话视图 |
| `Definitions/` | `StoryGraph` 图资产、节点模型与编辑期校验 |
| `Editor/` | 剧情图编辑器窗口与检视面板 |
| `Tests/Editor/` | EditMode 单测（83 个用例） |

`Story.cs` 是 DSL 静态入口，用 `using static Xuan.Prometheus.Narrative.Story;` 引入。

---

## 舞台：`await using` 的作用域接管

```csharp
await using (StageScope stage = await Stage.EnterAsync(spec, services))
{
    context.Stage = stage;
    await runner.RunAsync(root, context, "ch1_s3");
}
// 作用域退出 → 严格逆序还原；异常、取消、跳过三条路径同样走完整套还原
```

进入顺序（退出严格逆序）：淡出到黑 → HUD 与黑边 → 输入锁 → AI 冻结 → 镜头接管 →
角色解析与摆位 → 资源预加载 → 演出播放器创建与轨道绑定 → 淡入。

单个还原步骤失败不会阻断其余步骤，避免一个异常导致输入或镜头永久卡死。
进入过程中途失败会把已经生效的接管全部回滚。

### 端口

舞台不直接依赖任何玩法系统，全部能力通过端口注入：

| 端口 | 场景实现 | 正式实现应对接 |
|---|---|---|
| `INarrativeScreen` | `NarrativeScreenView` | 可直接复用 |
| `IActorResolver` | `SceneActorResolver` | 补一个查询 `IEntitySystem` 与 `NpcDefinition` 生成替身的解析器 |
| `INarrativeCameraPort` | `SceneCameraPort` | 包装 `ICameraSystem` + Cinemachine |
| `INarrativeAssetPort` | `SceneAssetPort` | 包装 `IAssetKit`（YooAsset） |
| `INarrativeVfxPort` | `PrefabVfxPort` | 可直接复用，资源改走 AssetKit |
| `INarrativeAudioPort` | `LoggingNarrativeAudioPort` / `FmodNarrativeAudioPort` | FMOD 实现已可用，需加载对应 Bank |
| `INarrativeWorldPort` | `SceneWorldPort` | 对接 `IInputSystem` 的 Cutscene 上下文与 AI 系统 |

缺省端口只有在剧情真正用到对应能力时才报错，且错误信息会指出缺少哪一个端口。

---

## 演出片段（第 3 期）

```csharp
// 一整段
Cinematic("seq_altar")

// 按 cue 切段，中间插对话；两段复用同一个 PlayableDirector，姿态与镜头保持连续
Cinematic("seq_altar", null, "cue_mid"),
Say(elder, "demo.e1"),
Cinematic("seq_altar")
```

**铁律 R1：Timeline 只承载可插值、可 `Evaluate` 的连续表现，不得包含一次性副作用。**
`StageScope` 在绑定阶段会对 `SignalTrack` / `ControlTrack` 发出告警。

轨道绑定按约定自动完成：**Timeline 输出轨道名与 `ActorRef.Id` 相同即自动绑定**，
目标按轨道要求的组件类型在角色对象上解析。

演出片段必须在 `StageSpec.Cinematics` 中声明——同步的 `Settle` 需要寻址到同一个 director。

### 动画

| 对象 | 驱动方式 | Timeline 轨道 | Action 叶子 |
|---|---|---|---|
| 场景物体、道具、机关 | 原生 `AnimationClip` 按时刻采样 | 内置 `AnimationTrack` | `PropAnim` |
| Spine 角色 | `Spine.Animation.Apply()` 按时刻采样 | 自研 `StorySpineTrack` | `SpineAnim` |
| 相机 | 机位插值 | — | `CameraTo` / `CameraCut` |

两条路径都是**时间的纯函数**：可任意 scrub，`Settle` 就是在末尾采样一次。

同一角色同时只允许一段剧情动画驱动骨架。`StageScope.BeginActorAnimation` 为每个角色维护一条动画通道，
开启新动画会自动顶掉上一段，因此循环动画可以直接用 `Par(ParallelMode.Detached, SpineAnim(...))` 启动，
不需要显式停止。

---

## 跳过 / 续演 / 预览（第 4 期）

三者共用同一条落终态代码路径：

```csharp
// 跳过：仅对已完整看过一次的剧情开放
if (!narrative.RequestSkip()) { /* 尚未看过，被拒绝 */ }

// 断点续演：续演点之前的节点只落终态
narrative.RestoreSnapshot(json);
await narrative.ResumeAsync(root, storyId, narrative.ResumePath);

// 编辑器预览：从任意节拍开始，节拍不等待玩家输入
await narrative.PreviewAsync(root, storyId, someBeatPath);
```

续演的快进由 `StoryContext.DecideResume` 裁决：**是续演点本身**就恢复正常演绎，
**是其祖先**就向下递归，**其余**一律只 `Settle`。两处特别处理：

- `Par` 的并发子节点没有先后关系，不含续演点的一律落终态。
- `Choose` **不会替玩家重新选择**，而是沿用存档中记录的结果；缺少记录时直接报错。

快进走完整棵树仍未碰到续演点时返回 `Failed` 并报错——通常意味着存档与当前分支状态不一致。

### 存档

`NarrativeSnapshot` 只记录：变量与旗标、已看过的剧情、一次性选项记录、各选项节点的选择结果、续演点。
**不记录** Timeline 时间、镜头位置或特效句柄——这些都由 `Settle` 重建。实际存档很小：

```json
{"variables":[{"path":"var.trust","kind":2,"value":"1"}],"seenStories":[],
 "chosenOptions":[],"choices":[],"resumeStoryId":"demo_altar","resumePath":"demo_altar/elder_first"}
```

---

## Timeline 编写约束（实测踩过的坑）

要让一段 Timeline 真正成为「时间的纯函数」，除了铁律 R1，资产本身还必须这样配置：

| 设置 | 取值 | 原因 |
|---|---|---|
| `AnimationTrack.trackOffset` | `ApplyTransformOffsets` | `ApplySceneOffsets` 以物体**当前**变换为原点，重复进入舞台会累积漂移 |
| `AnimationTrack.position` / `rotation` | 保持零 | 偏移写进资产会让片段取值不再等于场景绝对坐标 |
| `AnimationPlayableAsset.removeStartOffset` | **`false`** | 默认 `true` 会把片段当作相对首帧的位移（`clip(t) − clip(0)`），物体会被拽到原点附近 |
| 位移/旋转曲线 | **三个分量都要给曲线** | 只给 `.y` 打曲线时，Timeline 会把未打曲线的 `.x`/`.z` 写成 0 |

按上表配置后，同一时刻反复 `Evaluate` 得到完全相同的姿态，`Settle` 才能安全地「推到区间末尾求值一次」。
演示用的 `Demo/Assets/AltarSequence.playable` 已按此配置，可作为参考。

---

## 图资产与编辑器（第 5 期）

一段剧情可以用 **C# DSL** 写，也可以用 **图资产**（`StoryGraph`）写。
`StoryGraph.Build()` 是资产与运行时之间唯一的转换点，两种入口产出**同一种运行时表示**，
因此跳过、续演、预览、存档等能力对两者一视同仁。

- 创建：`Create → Prometheus → Narrative → Story Graph`
- 编辑：双击资产，或菜单 `Prometheus → 剧情图编辑器`
- 演示资产：`Demo/Assets/DemoAltarGraph.asset`（41 个节点，等价于 `DemoStory.cs`）。
  把它拖到演示场景 `NarrativeDemo` 组件的「剧情来源」上，场景就改为演绎图资产；留空则演绎 C# DSL 版本。

### 为什么是大纲树，不是节点画布

剧情树是一棵**有序**树，顺序本身就是语义（`Seq` 的先后、选项的排列）。节点画布对「顺序」没有自然表达，
只能靠序号角标或按坐标排序；而对话内容是线性阅读的，写手需要的是一份能从上往下扫的剧本，
不是一张需要平移缩放的图。大纲树在缩进、折叠、重排上直接对应这套模型。

### 编辑期校验

`StoryGraphValidator` 把运行时的硬性前提全部前移到编辑期，点「校验」即可：

| 检查 | 拦住的运行时故障 |
|---|---|
| 结构可构建、全树路径唯一 | 演绎时才发现节点字段没填 |
| 表达式可编译、标识符命名空间被承认 | 演到条件那一步才抛 `StoryExpressionException` |
| 文本键在文本表中存在 | 界面上出现 `#key#` 占位串 |
| 演出片段已在 `Cinematics` 中声明 | `RequireDirector` 拒绝播放 |
| 状态类叶子的资源已在 `Preload` 中声明 | 跳过时同步取不到资源 |
| 被动作引用的角色已在 `Actors` 中声明 | `RequireActor` 解析失败 |

### 数据模型

节点树用 `[SerializeReference]` 做多态序列化，结构与运行时的 `IStoryAction` 树一一对应，
不需要额外的连线表或索引表。节点通过三个收集器参与校验，**新增带对应字段的节点类型时必须一并覆写**：

```csharp
public virtual void CollectExpressions(ICollection<string> expressions)      // 条件表达式
public virtual void CollectTextKeys(ICollection<string> keys)                // 文本键
public virtual void CollectRequirements(StoryNodeRequirements requirements)  // 对舞台声明的要求
```

节点的接纳判定用 `CanAddChild`，是**不改变任何状态**的查询——编辑器创建菜单绝不能用「先加再删」试探，
那会让 `IfNode` 把 else 分支顶到 then 上，静默改坏剧情。

---

## 三条必须遵守的规则

**R1 — Timeline 不得有副作用。** 见上。

**R2 — `Settle` 必须幂等。** 每个动作都要实现「不经过程直接落到终态」，且重复调用结果一致。
跳过、断点续演、编辑器预览共用这条路径，写错会导致跳过后世界状态错乱且难以察觉。
`Tests/Editor/SettleParityTests.cs` 与 `StageScopeTests.cs` 是这条规则的回归保护，新增动作时必须一并覆盖。

区分方式看两类范式：

- **状态类**：`ActorMoveAction`、`CameraBlendAction`、`ScreenFadeAction`、`SetVariableAction`、`PropAnimAction` ——
  终点位置 / 当前机位 / 黑幕不透明度 / 变量值属于世界状态，`Settle` 直接写终值。
- **纯表现类**：`CameraShakeAction`、`VfxSpawnAction`、`SfxAction`、`AmbienceAction` ——
  不留下世界状态，`Settle` 为空实现，且取消路径要自行复位。

**R3 — 环境接管必须走舞台作用域。** 任何对输入、镜头、HUD、AI、角色骨架的接管都要压入
`StageScope` 的还原栈，由 `await using` 保证还原。

### 变量取值策略

`flag.*` 未写入时按 `false` 参与判断；`var.*` 未定义则直接抛错。
因此条件里用到的 `var.*` 必须显式初始化（演示场景里的 `var.trust` 就是例子）。

### 需要预加载的资源

被**状态类**叶子引用的资源必须在 `StageSpec.Preload` 中声明——`Settle` 是同步过程，无法等待加载。
`PropAnimAction` 会在缺少预加载时给出明确错误。

---

## 实现过程中对设计文档的修正

| 项 | 说明 |
|---|---|
| `StoryActionClip` **已撤销** | 原设计想让 Action 也能摆到 Timeline 上，构成三处复用。这与 R1 直接冲突（特效、音效都是一次性副作用），改用按 cue 分段的 `Cinematic` + director 复用来表达 |
| `ActorAnimationHandover` **不再触碰战斗动画模块** | 原设计计划调用 `SpineComponent.ClearTrack` 交接。实测 `SpineComponent` 是实体框架内的纯 C# 组件（非 `MonoBehaviour`），`GetComponentInChildren` 会直接抛异常。改为只用 spine-unity 公开 API，剧情侧对战斗动画模块的耦合降为零 |
| 新增每角色动画通道 | `StageScope.BeginActorAnimation`；否则两段循环动画会互相覆盖，结果不可复现 |
| `IStoryAction.Children` | 设计文档原稿未列出；路径绑定与全树遍历需要它 |
| DSL 入口名 | 台词节拍的工厂方法是 `Story.Say` 与 `Story.Narration`（类型仍叫 `Beat`） |
| `AdvanceKind.VoiceEnd` | 尚无配音服务，当前退化为 `TextDuration`；`Beat.VoiceKey` 仅作数据透传 |
| 存档合并旗标 | `NarrativeSnapshot` 只有一个 `variables` 列表（旗标即 `flag.*` 布尔变量），不再像设计文档那样单列 `Flags`，避免两份数据分歧；并新增 `choices` 记录各选项节点的选择结果 |
| 跳过不计入「已看过」 | 只有 `Completed` 才把剧情加入 `seenStories`；跳过一遍不应把没看过的剧情标记成看过 |
| 对话界面 | 仍用 `Demo/SimpleDialogueView`（运行时构建 uGUI + 系统字体）。工程内 TMP 只有 LiberationSans SDF，**不含中文字形**，正式 `DialoguePanel` 需先导入中文 TMP 字体资产并走 UIKit 预制体流程 |

---

## 测试

```
EditMode → Prometheus.Narrative.EditorTests
```

83 个用例：

| 文件 | 覆盖 |
|---|---|
| `StoryExpressionTests` | 表达式优先级、短路、类型语义、编辑期校验、缓存 |
| `StoryActionTests` | 组合子控制流、分支选择、提前结束、路径绑定与唯一性 |
| `BeatTests` | 节拍驱动顺序、推进策略解析、叙述行、选项语义（锁定 / 一次性 / 无可选项） |
| `SettleParityTests` | **完整演绎 与 落终态 的世界状态对拍**、`Settle` 幂等、跳过与中止的区别 |
| `StageScopeTests` | 接管顺序、**严格逆序还原**、进入失败回滚、还原步骤失败不阻断、特效随舞台回收、缺省端口报错 |
| `StageActionParityTests` | 第 2、3 期新增叶子的 `Settle` 与完整演绎一致性 |
| `ResumeTests` | **按剧情树中每个节拍自动参数化**的续演对拍、预览模式、存档往返、未知续演点与缺失选择记录的拒绝 |
| `CinematicTests` | 演出片段的 cue 区间解析、分段连续性、`Settle` 幂等、未声明片段与未知 cue 的拒绝、退出后播放器销毁 |
| `StoryGraphTests` | **图资产与 DSL 产出同一棵运行时树**（路径与行为逐项对拍）、构建错误定位、六类编辑期校验、`CanAddChild` 非破坏性 |

`CinematicTests` 直接构造 `TimelineAsset` 与 `PlayableDirector`，因此不依赖播放时序即可确定性地验证区间与落终态。
`StorySpineTrack` 的实际采样、`Wait` / `WaitFor` 依赖 Unity 播放循环，仍由演示场景做 PlayMode 验证（见上表实测结果）。
