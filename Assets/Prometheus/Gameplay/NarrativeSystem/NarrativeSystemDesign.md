# NarrativeSystem 设计方案

> 状态：第 0~5 期已实现（见 §19 分期路线与本目录 `README.md`），第 6 期起仍为设计稿
> 定位：取代 `Assets/Prometheus/Gameplay/FilmSystem`，承担全部剧情演出与对话表现
> 状态：FilmSystem 已整体移除（含 `NpcInteractionCoordinator` 与 `NpcDefinition` 的三个 Film 字段）；本文中对它的引用均为设计依据的历史说明
> 命名空间：`Xuan.Prometheus.Narrative`
> 日期：2026-09-05

---

## 0. 摘要

本方案用**一个原语 + 两个组合子**替换 FilmSystem 的 Timeline-Marker 流程机，把剧情从「时间轴驱动」改为「节拍驱动」。

三条支柱：

1. **唯一原语 `IStoryAction`**：可等待、可取消、可直接落终态。镜头、角色动画、物体动画、特效、音效、对话、分支全部是它的叶子实现。组合子只有 `Seq` 与 `Par`，核心运行时约 50 行。
2. **Timeline 降级为叶子，并受「无副作用」铁律约束**：只承载可插值、可 `Evaluate` 的连续表现。跳过、分支、存档三个老大难由此结构性消失。
3. **`Settle()` 一函数三用**：跳过、断点续演、编辑器任意节拍预览共用同一条代码路径。

对话的编排单位是 `Beat`（节拍），表现挂在句子上而非秒数上——改文案、增删台词、换语言导致配音变长，都不会破坏任何 timing。

---

## 1. 背景与动机

### 1.1 现有 FilmSystem 的问题

FilmSystem 是一套 1900 行的 Timeline 播放器兼流程编排器，**至今没有任何 `FilmDefinition` 或 `TimelineAsset` 资产使用它**，即从未被内容验证过。其设计问题按严重程度排列：

| # | 问题 | 位置 | 后果 |
|---|---|---|---|
| 1 | 流程编排与表现播放揉进同一对象 | `FilmFlowMarkers.cs` | 分支、等事件、子演出、并行全部塞进线性时间轴 |
| 2 | 分支跳转后 marker 索引不重算 | `FilmInstance.ApplyBranch` / `OnUpdate` | `nextInteractionIndex` 单向推进，`director.time` 回跳后所有 marker 触发顺序错乱（真 bug） |
| 3 | `Skip` 不 `Evaluate` | `FilmInstance.Skip` | 设 `time = duration` 后直接 `Stop()`，角色位置、物件开关、后处理停在跳过瞬间 |
| 4 | 时间轴快照不可靠 | `FilmPlaybackSnapshot` / `PlayFromSnapshot` | 一次性副作用不重放；被跳过的 `WaitEventMarker` 永不再等 |
| 5 | 优先级抢占语义危险 | `FilmSystem.Play` | 高优先级演出直接 `StopAll(Replaced)` 干掉正在播的剧情 |
| 6 | QTE 超时终止整段演出 | `FilmInstance.OnUpdate` | 应走失败分支而非中止 |
| 7 | 资源硬引用 | `FilmDefinition.timeline` | 项目用 YooAsset，加载一个定义会把整章 Clip/Audio 拖进内存 |
| 8 | 缺「舞台」概念 | `FilmInstance.AcquireRuntimeLeases` | 只有输入锁 + 镜头租约，无淡黑、HUD、玩家收起、AI 冻结、替身、摆位，也无对称还原 |
| 9 | 绑定靠 Timeline `streamName` 字符串 | `FilmInstance.BindTimelineOutputs` | 需要的是语义解析（主角 / 同伴 / 场景 NPC），且目标可能尚未生成 |
| 10 | 输入判定手写长表达式 | `ManualFilmInteractionService.HasPressed` | 二十余个 `\|\|` 串联，新增动作必须改此处 |

更根本的缺口：**对话系统完全不存在**。`IFilmInteractionService.ShowDialogueAsync` 只是抽象端口，`ManualFilmInteractionService` 是联调占位。项目也**没有本地化文本表**。

### 1.2 为什么不照抄原神

原神的剧情架构（`MainQuestExcelConfigData` / `QuestExcelConfigData` 的 cond-exec 步骤机 / `TalkExcelConfigData` / `DialogExcelConfigData` / SceneGroup Lua）是成熟且经过验证的，但它有两个时代性的包袱：

- **时间轴是唯一主轴**：分支要 seek，对话要 pause，跳过要补执行副作用，存档只能整个 SubQuest 重来。
- **扁平 cond/exec 表达不了结构**：顺序、并发、嵌套只能靠 Lua 兜底，于是引入脚本 VM，逻辑离开静态类型系统，无法单测。

本方案保留其**分层思想**（流程 / 对话入口 / 对话内容 / 演出 / 世界状态 五层职责分离），但换掉执行模型。

> 说明：上述原神表结构来自社区逆向的公开数据（Grasscutter 及相关配置表仓库），非官方文档。结构层面可信，字段级拼写若要照搬需另行核对。

---

## 2. 核心思想

> **时间不是主轴，节拍才是。绝对时间只允许存在于叶子节点内部。**

一段剧情是一棵 **Action 树**，树的执行顺序由结构决定，不由秒数决定。只有当叶子是 `Cinematic`（Timeline）时，其内部才存在连续时间。

由此推出三条硬规则，贯穿全文：

- **R1**：任何一次性副作用（生成、销毁、改变量、发事件、触发对话）只能发生在 Action 树上，禁止写进 Timeline。
- **R2**：任何 Action 必须实现 `Settle()`，即「不经过程直接落到终态」，且该操作幂等。
- **R3**：任何对全局环境的接管（输入、镜头、HUD、AI、角色控制权）必须通过 `Stage` 的作用域获得，由 `await using` 保证还原。

---

## 3. 核心原语

### 3.1 `IStoryAction`

```csharp
namespace Xuan.Prometheus.Narrative
{
    /// <summary>剧情系统的唯一执行原语；镜头、动画、特效、音效、对话与流程控制均实现该接口。</summary>
    public interface IStoryAction
    {
        /// <summary>获取该节点在所属剧情树中的稳定路径标识，用于存档定位与编辑器预览。</summary>
        StoryPath Path { get; }

        /// <summary>获取该节点的直接子节点；叶子节点返回空集合。路径绑定与全树遍历依赖该视图。</summary>
        IReadOnlyList<IStoryAction> Children { get; }

        /// <summary>正常演绎该节点；实现必须响应取消并保证取消后不残留副作用。</summary>
        UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken);

        /// <summary>不经过程直接把该节点的终态应用到世界；跳过、断点续演和编辑器预览共用该入口。</summary>
        void Settle(StoryContext context);
    }
}
```

`Settle` 的语义约定：

- **纯表现类叶子**（特效、音效、镜头震动）：`Settle` 为空实现——跳过就是不播。
- **状态类叶子**（角色摆位、物件开关、变量写入、镜头切换）：`Settle` 必须把终态一次性写入，且**幂等**。
- **对话类叶子**：`Settle` 记录「已读」并跳过，不显示 UI。
- **组合子**：`Settle` 递归调用子节点的 `Settle`。

### 3.2 组合子

只有两个，均为 `IStoryAction`：

```csharp
/// <summary>依次执行子节点，任一子节点取消即整体取消。</summary>
Seq(params IStoryAction[] children)

/// <summary>并发执行子节点；完成策略由 ParallelMode 决定。</summary>
Par(ParallelMode mode, params IStoryAction[] children)
```

```csharp
/// <summary>定义并发组合子的完成条件。</summary>
public enum ParallelMode
{
    /// <summary>等待全部子节点完成。</summary>
    WhenAll,

    /// <summary>任一子节点完成即结束，其余取消。</summary>
    WhenAny,

    /// <summary>启动后立即返回，子节点在后台继续并由 Stage 作用域兜底取消。</summary>
    Detached
}
```

分支不需要单独的组合子，它是一个叶子：

```csharp
/// <summary>按表达式求值结果选择一个分支执行。</summary>
If(string expression, IStoryAction then, IStoryAction otherwise = null)

/// <summary>展示选项并按玩家选择分派。</summary>
Choose(params ChoiceOption[] options)
```

**这就是全部流程能力。** 顺序、并发、条件、选项，四者组合足以表达任意剧情结构，无需脚本 VM。

### 3.3 `StoryContext`

```csharp
/// <summary>承载一次剧情演绎的运行时上下文：变量、条件求值、角色解析与舞台访问。</summary>
public sealed class StoryContext
{
    /// <summary>获取当前剧情的变量与全局旗标视图。</summary>
    public IStoryVariables Variables { get; }

    /// <summary>获取表达式求值器；求值过程为纯函数，不依赖 Unity 运行时。</summary>
    public IStoryExpressionEvaluator Evaluator { get; }

    /// <summary>获取当前演出的角色解析器。</summary>
    public IActorResolver Actors { get; }

    /// <summary>获取当前演出持有的舞台作用域。</summary>
    public StageScope Stage { get; }

    /// <summary>获取本次演绎的模式；跳过与预览模式下叶子实现可省略非必要表现。</summary>
    public StoryPlayMode Mode { get; }
}
```

### 3.4 执行器

```csharp
/// <summary>驱动一棵剧情树的执行，并对外暴露跳过、中止与进度观察。</summary>
public sealed class StoryRunner
{
    /// <summary>当剧情推进到一个新节拍时触发，供存档层记录续演位置。</summary>
    public event Action<StoryPath> BeatEntered;

    /// <summary>从头或指定节拍开始演绎整棵树。</summary>
    public UniTask<StoryResult> RunAsync(IStoryAction root, StoryContext context, StoryPath resumeAt = default);

    /// <summary>请求跳过：当前节点取消，其余节点走 Settle。</summary>
    public void RequestSkip();
}
```

---

## 4. 对话节拍 `Beat`

### 4.1 模型

`Beat` 是对话的编排单位，也是**表现挂载点**：

```csharp
/// <summary>一次对话节拍：一句台词及其伴随的镜头、动画、特效与音效表现。</summary>
public sealed class Beat : IStoryAction
{
    /// <summary>获取说话者。</summary>
    public ActorRef Speaker { get; }

    /// <summary>获取台词文本键；实际文案由 TextMap 按当前语言解析。</summary>
    public TextKey Text { get; }

    /// <summary>获取台词配音事件；为 None 时按字数估算时长。</summary>
    public FmodAudioEvent Voice { get; }

    /// <summary>获取进入本节拍时并发触发、不阻塞推进的表现动作。</summary>
    public IReadOnlyList<IStoryAction> Concurrent { get; }

    /// <summary>获取进入本节拍时必须先完成才允许推进的动作。</summary>
    public IReadOnlyList<IStoryAction> Blocking { get; }

    /// <summary>获取本节拍的推进条件。</summary>
    public AdvancePolicy Advance { get; }
}
```

作者侧的 Fluent 写法（DSL 入口为 `Story.Say`，叙述行为 `Story.Narration`）：

```csharp
Say(Actors.Paimon, TXT.Ch1_012)
    .Voice(FmodAudioEvent.VO_Paimon_Ch1_012)
    .With(ActorAnim(Actors.Paimon, "surprised"))          // 并发，不阻塞
    .With(Vfx("fx_sparkle", at: Anchor.Head(Actors.Paimon)))
    .With(Sfx(FmodAudioEvent.SFX_Poof))
    .With(Camera.PushIn("cam_paimon", 0.8f))
    .Await(ActorAnim(Actors.Hero, "turn_around"))         // 阻塞：转完才允许点击推进
    .AdvanceOn(Advance.Click)
```

**这是本方案对「说完一句话点一下才说下一句并播放相应动画特效音效」需求的直接答案。** 表现绑定在句子上，句子的增删改不影响任何其他句子。

### 4.2 推进策略

```csharp
/// <summary>定义对话节拍的推进方式。</summary>
public enum AdvanceKind
{
    /// <summary>等待玩家确认输入。</summary>
    Click,

    /// <summary>配音播放结束后自动推进；无配音时退化为 TextDuration。</summary>
    VoiceEnd,

    /// <summary>按文本长度估算的阅读时长后自动推进。</summary>
    TextDuration,

    /// <summary>固定延迟后自动推进。</summary>
    Delay,

    /// <summary>不等待，立即推进；用于把多句合并成一段连续独白。</summary>
    Immediate
}
```

全局「自动播放」开关的实现方式：运行时把 `Click` 降级为 `VoiceEnd`（无配音则 `TextDuration`）。**不需要在数据里为自动播放单独配置。**

### 4.3 选项

```csharp
Choose(
    Option(TXT.Ch1_opt_a).When("!flag.asked_about_wind").Then(Seq(...)),
    Option(TXT.Ch1_opt_b).Once().Then(Seq(...)),
    Option(TXT.Ch1_opt_leave).Then(Flow.Break())
)
```

- `.When(expr)`：选项解锁条件，不满足时默认**隐藏**。
- `.Locked(reason)`：需要「灰显但可见」时使用。
- `.Once()`：选中后永久隐藏，记录进存档的已选集合。

### 4.4 打字机与二段点击

对话 UI 的输入语义固定为两段：

| 状态 | 玩家确认输入 | 结果 |
|---|---|---|
| 打字机进行中 | 第一次 | 立即显示全文，配音继续播 |
| 全文已显示 | 第二次 | 推进到下一节拍 |

`Blocking` 动作未完成时确认输入被吞掉，UI 上不给推进提示——这保证「先转身再说话」这类编排不会被玩家点穿。

---

## 5. Timeline 的定位与铁律

Timeline 不再是系统骨架，而是一种**叶子实现**：

```csharp
/// <summary>播放一段纯表现 Timeline；该节点内部允许存在连续时间，但禁止产生一次性副作用。</summary>
Cinematic("seq_gate_open")
```

### 铁律（R1 的具体化）

> **Timeline 只能承载可插值、可 `Evaluate` 的连续表现。**

允许：相机曲线、骨骼与物体动画、位移旋转缩放、后处理参数、音量包络、材质属性。

禁止：生成或销毁对象、写变量、发事件、触发对话、启动子演出、改变任务状态。

### 铁律带来的三个结构性收益

| 老问题 | 为何消失 |
|---|---|
| Skip 丢副作用 | 没有副作用可丢。`director.time = duration; director.Evaluate();` 天然产出正确终态 |
| 分支需 seek 时间轴 | 分支在 Action 树上，Timeline 内部永不跳转 |
| 演出进度需存档 | Timeline 段原子、幂等、可重播，无需记录进度 |

§1.1 表中的 #2 与 #3 两个 bug 在此模型下**不是被修复，而是无法被写出来**。

### Timeline 编写约束（实测）

要让一段 Timeline 真正成为「时间的纯函数」，除了铁律 R1，资产本身还必须这样配置：

| 设置 | 取值 | 原因 |
|---|---|---|
| `AnimationTrack.trackOffset` | `ApplyTransformOffsets` | `ApplySceneOffsets` 以物体**当前**变换为原点，重复进入舞台会累积漂移，落终态不再可复现 |
| `AnimationTrack.position` / `rotation` | 保持零 | 偏移写在资产里会让片段取值不再等于场景绝对坐标 |
| `AnimationPlayableAsset.removeStartOffset` | **`false`** | 默认 `true` 会把片段当作相对首帧的位移（结果是 `clip(t) − clip(0)`），物体会被拽到原点附近 |
| 位移/旋转曲线 | **三个分量都要给曲线** | 只给 `.y` 打曲线时，Timeline 会把未打曲线的 `.x`/`.z` 写成 0 |

按上表配置后，同一时刻反复 `Evaluate` 得到完全相同的姿态，`Settle` 才能安全地「推到区间末尾求值一次」。

### `Cinematic` 的实现要点

- 运行时创建 `PlayableDirector`，`playableAsset` 由 `IAssetKit` 按 location 异步加载，`extrapolationMode = None`。
- 轨道绑定不使用 `streamName` 字符串直配，改为 `SequenceDefinition` 中声明的 `ActorRef → 输出轨道` 映射，由 `IActorResolver` 在 `PlayAsync` 前解析。
- `Settle()` 实现：`director.time = duration; director.Evaluate(); director.Stop();`
- 播放中被取消：`director.Stop()` 后仍执行一次到 `duration` 的 `Evaluate()`，保证世界不停在半程姿态。

---

## 6. 动画方案

### 6.1 现状与约束

**本节结论受项目现状约束，先陈述事实：**

- 角色动画由 **Spine 2D 骨骼**驱动（`Spine.Unity.SkeletonAnimation`），见 `EntitySystem/Character/Component/SpineComponent.cs`。
- 工程内 **`UnityEngine.Animator` 使用量为零**；非第三方的 `.anim` 资产仅有 `Trd/VolumetricLightBeam/Samples` 下 3 个示例。
- Spine-Unity 已安装，但**未包含 spine-timeline 扩展包**，只有 `Runtime/spine-unity/Utility/TimelineExtensions.cs`（一个按时间求值的工具类）。
- `Assets/Art/火环spine合集1/Q版小人/StoryTimeline/` 下已有 Spine 剧情素材，说明这条路径已在试验中。

因此「原生 `AnimationClip` + `Animator`」可以完整覆盖场景物体，但**无法驱动 Spine 骨骼角色**。本方案按此事实分工。

### 6.2 分工

| 对象 | 驱动方式 | Timeline 轨道 | Action 叶子 |
|---|---|---|---|
| 场景物体、道具、机关、门、机械 | **原生 `Animator` + `AnimationClip`** | Unity 内置 `AnimationTrack` | `PropAnim(prop, clip)` |
| Spine 角色 | **`Spine.Animation.Apply()` 按绝对时间采样** | 自研 `StorySpineTrack` | `ActorAnim(actor, animName)` |
| 相机 | Cinemachine | 内置 `CinemachineTrack` | `Camera.*` |

### 6.3 角色动画为什么走 `Animation.Apply()` 而不是 `AnimationState`

这是本方案对「剧情演出是纯表现」这一要求的技术落点。

`Spine.Animation.Apply(skeleton, lastTime, time, loop, events, alpha, blend, direction)` 是**时间的纯函数**：给定时刻直接算出骨架姿态，不依赖播放历史、不入队、不触发事件。这正好满足铁律对「可 `Evaluate`」的要求：

- Timeline 可任意 scrub，编辑器预览与运行时表现一致。
- `Settle()` 就是在 `duration` 处 `Apply()` 一次。
- 多轨混合由 `alpha` 与 `MixBlend` 直接控制，不经过 `AnimationState` 的过渡队列。

而 `SkeletonAnimation.AnimationState` 是事件驱动、有内部状态、不可 scrub 的，**不适合演出**。

### 6.4 与 AnimationLine 的隔离边界

**剧情演出不得引用 `AnimationLine`、`AnimationLibrary`、`AnimationPlayback`、`AnimationSemantic`、`AnimationMixDurationMatrix`、`SpineComponent` 中的任何类型。**

理由：

1. 那套是**战斗行为编排**，带优先级仲裁（`AnimationPriority`）、所有者抢占（`AnimationOwner`）、混合时长矩阵和 FMOD 事件标记——全部是玩法语义，剧情不需要，引入即耦合。
2. 该模块后续可能重构，剧情不应被它的演进绑架。
3. 两者诉求本质冲突：剧情要「按导演给定的时间点精确采样」，战斗要「按状态机仲裁抢占」。

隔离手段：`StorySpineTrack` 与 `SpineSampler` 只依赖 `Spine.Skeleton`、`Spine.Animation`、`Spine.SkeletonData` 三个 spine-csharp 层类型，以及 `SkeletonAnimation` 的公开成员。**不引用战斗动画模块的任何类型。**

> **实现修正（2026-09-06）**：本文档原先计划在舞台接管时调用一次 `SpineComponent.ClearTrack` 做交接。
> 实际实现时发现 `SpineComponent` 是实体框架内的**纯 C# 组件**（继承自 `Xuan.Prometheus.Component.Component`，
> 并非 `MonoBehaviour`），根本无法从 GameObject 上取到——`GetComponentInChildren<SpineComponent>()` 会直接抛异常。
> 因此接管改为只使用 spine-unity 的公开 API（禁用组件 + `AnimationState.ClearTracks()`），
> **剧情侧对战斗动画模块的耦合降为零**，比原设计更干净。

### 6.5 演出期间的角色控制权移交

进入舞台时，参演角色的战斗动画驱动必须被挂起，否则两套系统会争抢同一副骨架：

```csharp
// ActorAnimationHandover 进入时
skeletonAnimation.enabled = false;              // 停止组件自身的 AnimationState 推进
skeletonAnimation.AnimationState.ClearTracks(); // 清空已排入的动画，避免残留姿态参与混合
// 演出期间由 StorySpineTrack 或 SpineAnimAction 直接按时刻写 Skeleton 姿态

// 退出时严格逆序
skeletonAnimation.AnimationState.ClearTracks();
skeletonAnimation.Skeleton.SetToSetupPose();    // 交还干净的初始姿态
skeletonAnimation.enabled = previousEnabled;
```

同一角色同时只允许一段剧情动画驱动骨架。`StageScope.BeginActorAnimation` 为每个角色维护一条动画通道：
开启新动画会自动取消该角色上一段仍在播放的动画，否则两段循环动画会在同一帧互相覆盖，
结果取决于任务调度顺序而不可复现。

这段移交与还原封装在 `StageScope` 中，由 `await using` 保证异常路径同样执行（见 §8）。

> 全部接管逻辑封装在 `ActorAnimationHandover` 中。演出结束后骨架回到 Setup Pose，由玩法侧自行重新驱动动画；
> 实体层面的战斗动画会话清理属于设计文档 §15 的集成工作。

### 6.6 未来迁移

若角色将来改为 3D + `Animator`，只需把 `StorySpineTrack` 换成 Unity 内置 `AnimationTrack`，`ActorAnim(...)` 这一层 Action 接口**保持不变**，作者侧数据不需要改动。这正是能力层抽象存在的意义。

---

## 7. 能力层叶子清单

所有叶子实现 `IStoryAction`，可被 `Seq` / `Par` 组合，也可挂在 `Beat` 上。**能力实现一次，两处复用。**

> **实现修正（2026-09-06）**：本文档原先设想用一个通用的 `StoryActionClip` 把 Action 摆到 Timeline 上，
> 构成「代码 / Beat / Timeline」三处复用。实现时确认这与铁律 R1 直接冲突——特效生成、音效播放都是一次性副作用，
> 放进 Timeline 就会让跳过与断点续演产生不一致。**该设计已撤销。**
> 需要在演出中途插入对话或一次性事件时，改用 `Cinematic(location, fromCue, toCue)` 把片段按 cue 切开，
> 由剧情树在段与段之间插入节点；各段复用舞台上的同一个 `PlayableDirector`，因此镜头与姿态保持连续。

### 镜头

```csharp
Camera.CutTo("cam_a")                       // 硬切
Camera.BlendTo("cam_b", 1.2f, Ease.InOut)   // 混合
Camera.PushIn("cam_paimon", 0.8f)           // 推近
Camera.Shake(ShakePreset.Impact)            // 震屏
Camera.Follow(actor, offset)                // 跟随
Camera.Release()                            // 交还玩法镜头
```

底层通过 `ICameraSystem.AcquireFilmCamera(CinemachineCamera, priority)` 取得租约，租约生命周期挂在 `StageScope` 上。

### 角色

```csharp
ActorAnim(actor, "walk", loop: true)        // Spine 采样播放
ActorFace(actor, FaceDir.Left)              // 朝向
ActorMoveTo(actor, anchor, duration)        // 位移，可 Evaluate，Settle 直接落点
ActorSpawn(actorRef, anchor)                // 生成演出替身
ActorDespawn(actorRef)
```

### 场景物体

```csharp
PropAnim(prop, clip)                        // 原生 Animator + AnimationClip
PropActive(prop, visible)
PropMoveTo(prop, anchor, duration)
```

### 特效

```csharp
Vfx("fx_sparkle", at: Anchor.Head(actor))
Vfx("fx_explosion", at: Anchor.World(pos)).Await()   // 阻塞到播完
VfxStop("fx_sparkle")
```

接入现有 `EffectSystem`；剧情特效句柄登记在 `StageScope`，退出时统一回收。

### 音效

```csharp
Sfx(FmodAudioEvent.SFX_Poof)                        // 一次性事件
Bgm(FmodAudioEvent.BGM_Ch1, fadeIn: 1.5f)           // 持续事件
BgmStop(fadeOut: 2f)
Voice(FmodAudioEvent.VO_Paimon_Ch1_012)             // 一般由 Beat.Voice 隐式调用
```

一次性事件复用 `FmodAudioRuntime.PlayOneShot`；持续事件需新增 `EventInstance` 句柄层，同样登记在 `StageScope`。

### 对话

```csharp
Say(...) / Narration(...)                   // 见 §4
Choose(...)
Narration(TXT.xxx)                          // 黑屏叙述
```

### 流程

```csharp
If(expr, then, otherwise)
SetVar("flag.seen_intro", true)
Wait(1.5f)
WaitFor(expr)                               // 等待条件成立
Emit(QuestEventType.FilmCompleted, "ch1_s3")
Flow.Break()                                // 提前结束当前 Seq
```

### 舞台

```csharp
Stage.FadeOut(0.3f)
Stage.FadeIn(0.3f)
Stage.Letterbox(true)
Stage.Hud(false)
```

---

## 8. `Stage` 作用域接管

```csharp
await using StageScope stage = await Stage.EnterAsync(new StageSpec
{
    FadeOut        = 0.3f,
    HideHud        = true,
    Letterbox      = true,
    LockInput      = true,                       // InputContexts.Cutscene
    FreezeAiRadius = 30f,
    Actors         = new[] { Actors.Hero, Actors.Paimon, Actors.Npc("nh_priest") },
    Anchors        = "anchor_church_scene3",
    Preload        = new[] { "seq_gate_open", "fx_sparkle" }
});

await runner.RunAsync(root, context);
// 作用域退出 → 严格逆序还原，异常与取消路径同样执行
```

### 接管清单与还原顺序

| 顺序 | 进入 | 退出（逆序） |
|---|---|---|
| 1 | 淡出到黑 | 淡入 |
| 2 | 隐藏 HUD、打开黑边 | 恢复 HUD、关闭黑边 |
| 3 | 申请输入租约（`InputContexts.Cutscene`） | 释放输入租约 |
| 4 | 冻结半径内 AI 与怪物 | 解冻 |
| 5 | 挂起参演角色的战斗动画驱动（§6.5） | 恢复驱动并回到 Setup Pose |
| 6 | 解析并生成演出替身，摆位到 anchor | 销毁替身，还原原角色 Transform |
| 7 | 预加载并预热演出资源 | 释放资源句柄 |
| 8 | 创建演出片段播放器并完成轨道绑定 | 停止并销毁播放器 |
| 9 | 淡入 | 淡出到黑 |

`StageScope` 实现 `IAsyncDisposable`，内部维护一个 `Stack<IAsyncDisposable>`，`DisposeAsync` 依次弹栈。**忘记还原在语法上不可能**——这是相对 FilmSystem 手工管理两个 lease 的核心改进。

---

## 9. `ActorResolver`

```csharp
/// <summary>描述一个剧情角色的引用方式，解析结果可能是既有实体、临时替身或场景物体。</summary>
public readonly struct ActorRef
{
    /// <summary>获取角色引用类别。</summary>
    public ActorKind Kind { get; }

    /// <summary>获取该类别下的稳定标识。</summary>
    public string Id { get; }
}

/// <summary>定义剧情角色引用的类别。</summary>
public enum ActorKind
{
    /// <summary>当前上场的队伍成员。</summary>
    ActiveMember,

    /// <summary>按槽位指定的队伍成员。</summary>
    TeamSlot,

    /// <summary>常驻同伴。</summary>
    Companion,

    /// <summary>场景中的具名 NPC。</summary>
    Npc,

    /// <summary>场景中的具名物体。</summary>
    SceneProp,

    /// <summary>仅本次演出存在的临时替身。</summary>
    Stunt
}
```

解析规则：

- `ActiveMember` → `Core.Gameplay.GetSystem<ITeamSystem>().ActiveMember`
- `Npc` → 先在 `IEntitySystem` 中按 `NpcId` 查活动实体；查不到则按 `NpcDefinition` 生成 `Stunt` 替身，退出舞台时销毁。
- 解析结果 `ActorHandle` 暴露 `Transform`、`SkeletonAnimation`、`Animator`（均可空），叶子动作按需取用。
- **解析失败必须在 `Stage.EnterAsync` 阶段抛出**，不允许演到一半才发现角色不存在。

---

## 10. `TextMap` 本地化

项目当前无任何本地化设施，需先建。

```csharp
/// <summary>表示一条本地化文本的稳定键。</summary>
public readonly struct TextKey
{
    /// <summary>获取文本键字符串。</summary>
    public string Value { get; }
}

/// <summary>定义按当前语言解析文本键的查询入口。</summary>
public interface ITextMap
{
    /// <summary>解析文本键；缺失时返回可见的占位串而非抛出，避免剧情因单条文案中断。</summary>
    string Get(TextKey key);

    /// <summary>切换当前语言并通知已打开的 UI 刷新。</summary>
    void SetLanguage(string languageCode);
}
```

设计取舍：

- **用字符串 key，不用 hash。** 原神用 `textMapHash` 是为极端体量下的内存与查表性能；本项目体量下可读性收益远大于那点开销。`TextKey` 封成 struct，将来若要换 hash，改动限于该 struct 内部。
- 源数据为 CSV/Excel，导出为按语言分文件的资产，运行时按需加载单一语言，未使用语言不进内存。
- 配音事件与文本键约定同名映射（`TXT.Ch1_012` ↔ `VO_Ch1_012`），减少配置量。

---

## 11. 条件与变量

用**表达式**替代原神的数十种 `QUEST_COND_*` 枚举：

```
quest.mondstadt_1.state == Done && !flag.seen_paimon_intro && item.count("sigil") >= 3
```

- 求值器约 200 行：词法分析 → 递归下降 → 求值。支持 `&&`、`||`、`!`、`==`、`!=`、`<`、`<=`、`>`、`>=`、括号、数字、字符串、标识符路径与少量内置函数。
- **求值器与变量存储为纯 C#，零 Unity 依赖**，可在 EditMode 中全量覆盖，也为将来搬到 `IServiceSystem` 服务端权威化留门。
- 变量命名空间：`flag.*`（布尔旗标）、`var.*`（剧情变量）、`quest.*`（任务状态只读投影）、`item.*`（背包只读投影）。
- 编辑期校验：资产导入时解析全部表达式，未定义标识符直接报错，不留到运行时。

---

## 12. `Settle`：跳过 / 断点续演 / 编辑器预览

三个功能共用一条代码路径，这是本方案最经济的一处设计。

### 跳过

```
玩家请求跳过
  → 当前节点 cancellationToken 取消
  → 从当前位置起，对剩余所有节点递归调用 Settle()
  → Stage 正常退出
```

因 R1 保证 Timeline 无副作用、R2 保证每个 Action 可幂等落终态，跳过后世界状态与完整演绎**完全一致**。

跳过权限与原神一致：**仅对已完整看过一次的剧情开放**，`seen` 集合进存档。

### 断点续演

存档记录当前节拍的 `StoryPath`（如 `ch1/scene3/beat_07`）。重进时：

```
构造同一棵 Action 树
  → 对 resumeAt 之前的所有节点调用 Settle()
  → 从 resumeAt 节点开始 PlayAsync
```

**粒度到节拍**，优于原神的「整个 SubQuest 重来」。前提是每个 Action 的 `Settle` 幂等——这是 R2 的强制要求，通过 §20 的对拍测试保证。

### 编辑器预览

从任意节拍开始播，前置节点自动 `Settle`。策划改第 30 句台词无需从头看到第 30 句。

`StoryPath` 由树结构静态生成，稳定且可寻址，是这三项功能的共同基础。

### 快进的决策规则

续演的快进由 `StoryContext.DecideResume` 统一裁决，组合子只需照做：

| 子节点与续演点的关系 | 处理 |
|---|---|
| 就是续演点本身 | 清除快进标记，从这里开始正常演绎 |
| 是续演点的祖先 | 向下递归，继续快进 |
| 其余（位于续演点之前） | 只调用 `Settle`，不产生任何表现 |

两处需要特别处理：

- **`Par`**：并发子节点之间没有先后关系，因此不含续演点的子节点一律落终态，只有包含续演点的那一个继续演绎。
- **`Choose`**：续演不重新向玩家提问，而是沿用存档中记录的选择结果；**缺少记录时立即报错**，因为凭空替玩家选一次会让存档与实际走向产生分歧。

快进走完整棵树仍未碰到续演点时，执行器返回 `Failed` 并报错——这通常意味着存档记录的分支与当前变量或选择结果已经不一致。

---

## 13. 三种作者入口

```
   C# Fluent DSL          StoryGraph 资产（可视化图）        Timeline（密集连续表现）
         │                          │                                │
         └──────────────┬───────────┴────────────────────────────────┘
                        ▼
              IStoryAction 组合树（唯一运行时表示）
                        ▼
   Camera │ ActorAnim │ PropAnim │ Vfx │ Audio │ Dialogue │ Flow │ Stage
                        ▼
   Stage(作用域接管) │ ActorResolver │ TextMap │ StoryContext │ AssetKit
```

- **C# DSL**：程序侧首选，静态类型、可重构、可单测。**一期只做这个。**
- **StoryGraph 资产**：策划侧，反序列化为同一棵树。第 5 期补。
- **Timeline**：仅用于密集连续表现，作为 `Cinematic` 叶子被引用。

三者产出**同一个运行时表示**，不存在三套执行逻辑。

---

## 14. 存档格式

```csharp
/// <summary>剧情系统的可持久化状态；不包含任何演出播放进度。</summary>
[Serializable]
public sealed class NarrativeSnapshot
{
    /// <summary>获取全局布尔旗标。</summary>
    public List<string> Flags;

    /// <summary>获取剧情变量键值对。</summary>
    public List<StoryVariableEntry> Variables;

    /// <summary>获取已完整播放过、因而允许跳过的剧情标识。</summary>
    public List<string> SeenStories;

    /// <summary>获取已选择过的一次性选项标识。</summary>
    public List<string> ChosenOptions;

    /// <summary>获取中断时所处的节拍路径；为空表示当前无进行中的剧情。</summary>
    public string ResumePath;
}
```

**明确不存**：Timeline 时间、`PlayableDirector` 状态、镜头位置、特效句柄。这些全部可由 `Settle` 重建。

> **实现修正**：旗标与剧情变量共用同一份存储（旗标即 `flag.*` 前缀的布尔变量），
> 因此实际的 `NarrativeSnapshot` 只有一个 `variables` 列表，不再单列 `Flags`，避免两份数据产生分歧。
> 另外新增了 `choices`（各选项节点上已产生的选择结果），续演与跳过都依赖它才能不替玩家做决定。

FilmSystem 的 `FilmPlaybackSnapshot` 与 `IFilmSystem.SnapshotCaptured` 整体废弃。

---

## 15. 与现有系统的集成

| 系统 | 集成方式 |
|---|---|
| `IInputSystem` | `StageScope` 申请 `InputContexts.Cutscene` 租约；对话确认输入通过独立 `IInputReceiver` 上报。不再手写 `HasPressed` 长表达式，改为在 `InputFrame` 上加索引器 |
| `ICameraSystem` | 复用 `AcquireCutsceneCamera` 与 `CutsceneCameraLease`，租约挂 `StageScope` |
| `IEntitySystem` | `ActorResolver` 查活动实体 |
| `ITeamSystem` | 解析 `ActiveMember` 与 `TeamSlot` |
| `INpcSystem` | 订阅 `NpcSystem.InteractionRequested`，由叙事适配器请求剧情并在结束时回调 `CompleteInteraction` |
| `IQuestSystem` | 剧情通过 `Emit(...)` 发布任务事件；任务通过 `INarrativeSystem` 启动剧情。两者只经事件与接口交互，互不引用内部类型 |
| `EffectSystem` | `Vfx` 叶子的底层 |
| FMOD | `FmodAudioRuntime.PlayOneShot` 用于一次性事件；持续事件需补 `EventInstance` 句柄层 |
| `IUIKit` | `DialoguePanel` 走 `OpenPanel<T>()` 与 `ClosePanel<T>()` |
| `IAssetKit` | 全部剧情资源按 location 异步加载，禁止在定义资产里硬引用大资源 |
| `IServiceSystem` | 预留：条件求值与变量写入为纯函数，将来可整体搬到服务端 |

`NarrativeSystem` 继承 `XSystem`，以 `INarrativeSystem` 契约由 `GameplayKit` 注册，遵循项目既有系统边界约定。

---

## 16. 资源与内存

- `StoryDefinition` **不硬引用** `TimelineAsset`、`AnimationClip` 或预制体，只存 YooAsset location 字符串。
- `Stage.EnterAsync` 的 `Preload` 列表在淡黑期间批量加载并预热，避免演出中途卡顿。
- 资源句柄登记在 `StageScope`，退出时统一释放。
- 一章剧情按 scene 粒度分段加载，不整章常驻。

这一条直接修正 §1.1 表中的 #7。

---

## 17. 网络与服务端权威

一期纯本地。为将来权威化预留的约束**现在就要遵守**，否则后期搬不动：

1. 表达式求值器、变量存储、`StoryPath` 计算——**零 Unity 依赖**，独立于表现层。
2. 状态变更走单一入口（`SetVar` 与 `Emit`），不允许叶子直接改任务状态。
3. `NarrativeSnapshot` 可 JSON 序列化，与 `QuestSystem.CaptureSnapshot` 的风格保持一致。

---

## 18. 从 FilmSystem 迁移

| 现有文件 | 处置 |
|---|---|
| `FilmSystem.cs` / `IFilmSystem.cs` | 改造为 `NarrativeSystem` / `INarrativeSystem`，保留系统注册与生命周期骨架 |
| `FilmInstance.cs` | 拆解：Director 管理迁入 `Cinematic` 叶子；lease 逻辑升级为 `StageScope`；marker 轮询整体删除 |
| `FilmDefinition.cs` | 改造为 `StoryDefinition`，`TimelineAsset` 硬引用改 location |
| `FilmBindingContext.cs` | 由 `ActorResolver` 与 `SequenceDefinition` 绑定表取代 |
| `FilmHandle.cs` | 改造为 `StoryHandle`，去掉 `CaptureSnapshot` |
| `FilmFlowMarkers.cs` | **删除**（4 个流程 marker 全部由 Action 树取代） |
| `FilmInteractionMarker.cs` | 拆为 `BeatMarker`（Timeline 内嵌对话）与 `QteMarker`（失败走分支而非中止） |
| `FilmContracts.cs` | `FilmPlaybackSnapshot` **删除**；状态枚举保留精简版 |
| `ManualFilmInteractionService.cs` | **删除**，由 `DialogueSystem` 与测试用 `FakeDialogueView` 取代 |
| `Tests/Editor/FilmSystemTests.cs` | 重写为 `NarrativeSystemTests` |

`NpcDefinition.InteractionFilm` 改为 `InteractionStory`，`NpcInteractionCoordinator` 相应改写。

---

## 19. 分期路线

| 期 | 内容 | 完成标志 |
|---|---|---|
| **0** ✅ | `TextMap`、`StoryContext`、表达式求值器 | EditMode 全覆盖，零 Unity 依赖 |
| **1** ✅ | `IStoryAction`、`Seq`/`Par`/`If`、`StoryRunner`、`Beat`、对话视图 | 能用 C# DSL 写一段完整对话并跑起来 |
| **2** ✅ | `Stage`、`ActorResolver`、Camera/Vfx/Audio 叶子 | 一段带镜头、特效、音效的对话演出 |
| **3** ✅ | `Cinematic`、`StorySpineTrack`、`PropAnim` | Timeline 密集编排接入 |
| **4** | `Settle` 三用（跳过 / 续演 / 预览）与存档 | 跳过后世界状态与完整演绎一致 |
| **5** ✅ | `StoryGraph` 资产与编辑器 | 策划可脱离程序产出剧情 |
| **6** | `QuestSystem` 重构对接与世界状态联动 | 剧情推进改变世界 |

**建议顺序不可颠倒**：第 1 期做完就有可玩的对话，能立刻验证节拍模型是否成立；把 Timeline 放到第 3 期，是因为在没有 Action 树的情况下接 Timeline 会重演 FilmSystem 的错误。

---

## 20. 测试策略

`IStoryAction` 的纯逻辑本质使绝大部分内容可在 EditMode 覆盖。

| 层 | 测试方式 |
|---|---|
| 表达式求值 | 纯单测，覆盖优先级、短路、类型错误 |
| `Seq` / `Par` / `If` | 用 `FakeAction` 记录调用序列，断言执行顺序与取消传播 |
| `Beat` | 用 `FakeDialogueView` 驱动，断言打字机、二段点击、`Blocking` 吞输入 |
| **`Settle` 幂等性** | **关键**：对同一棵树分别执行「完整演绎」与「全部 Settle」，断言 `StoryContext` 终态一致 |
| **断点续演** | 对每个节拍位置各跑一次续演，断言终态与完整演绎一致 |
| `Stage` 还原 | 断言正常、异常、取消三条路径下接管清单全部还原 |
| `Cinematic` | PlayMode：断言 `Settle` 后 Director 时间为 `duration` 且世界状态正确 |

「Settle 幂等性」与「断点续演」两项应做成**参数化测试自动遍历每个剧情资产的每个节拍**，这是保证 R2 不被破坏的唯一可行手段。

---

## 21. 目录与命名

```
Assets/Prometheus/Gameplay/NarrativeSystem/
├── NarrativeSystemDesign.md          本文档
├── README.md
├── Core/                             IStoryAction / Seq / Par / StoryRunner / StoryPath / StoryContext
├── Dialogue/                         Beat / Choose / AdvancePolicy / IDialogueView
├── Actions/                          Camera / ActorAnim / PropAnim / Vfx / Audio / Flow 叶子
├── Cinematic/                        Cinematic 叶子 / StorySpineTrack / StoryActionClip / BeatMarker
├── Stage/                            Stage / StageScope / StageSpec / ActorAnimationHandover
├── Actors/                           ActorRef / IActorResolver / ActorHandle
├── Text/                             TextKey / ITextMap / TextMapAsset
├── Expression/                       词法 / 语法 / 求值器（零 Unity 依赖）
├── Definitions/                      StoryDefinition / SequenceDefinition / StoryGraph
├── Editor/                           图编辑器 / 资产校验 / 预览工具
└── Tests/Editor/                     单测
```

命名空间统一 `Xuan.Prometheus.Narrative.*`，与目录一一对应。系统实现类 `internal`，仅以 `INarrativeSystem` 对外。

---

## 22. 明确不做 / 已知代价

### 不做

- **不引入脚本 VM**（Lua 或 C# Script）。组合树加表达式已覆盖需求，引入 VM 会让逻辑离开静态类型系统。
- **不做通用行为树**。剧情的分支是数据分支而非 AI 决策，`If` 与 `Choose` 足够。
- **不存演出进度**。见 §14。
- **不复用 `AnimationLine`**。见 §6.4。
- **不为自动播放单独配置**。见 §4.2。

### 已知代价

1. ~~**图编辑器是最大的一次性投入。**~~ 已于第 5 期完成。
   最终没有做节点画布，而是做了**大纲树编辑器**：剧情树是一棵**有序**树，顺序本身就是语义（Seq 的先后、选项的排列），
   节点画布对顺序没有自然表达，只能靠序号角标或按坐标排序；对话内容又是线性阅读的，写手需要一份能从上往下扫的剧本。
   大纲树在缩进、折叠、重排上都直接对应这套模型，实现量也小一个数量级。
2. **`Settle` 幂等是全系统的隐性契约。** 一个叶子写错会导致跳过后世界状态错乱，且不易察觉。
   **缓解**：§20 的参数化对拍测试必须在第 4 期前建立，不能后补。
3. **`StorySpineTrack` 需自研。** spine-timeline 扩展包未安装，且直接采样的实现方式与官方轨道不同。
   **缓解**：实现只依赖 `Animation.Apply()` 一个 API，范围可控（预计 200~300 行），且将来换 Animator 时整体可弃。
4. **表达式求值器失去编译期检查。**
   **缓解**：资产导入时解析全部表达式并对未定义标识符报错。

---

## 附录 A：完整示例

```csharp
/// <summary>第一章第三场：教堂初遇。</summary>
public static IStoryAction Build()
{
    ActorRef hero   = ActorRef.ActiveMember();
    ActorRef paimon = ActorRef.Companion("paimon");
    ActorRef priest = ActorRef.Npc("nh_chillywinds_priest");

    return Seq(
        Stage.FadeOut(0.4f),
        Cinematic("seq_church_enter"),                          // 纯表现：推门、镜头入场

        Say(paimon, TXT.Ch1_S3_001)
            .Voice(FmodAudioEvent.VO_Paimon_Ch1_S3_001)
            .With(ActorAnim(paimon, "float_idle", loop: true))
            .With(Camera.BlendTo("cam_paimon_cu", 0.6f))
            .AdvanceOn(Advance.Click),

        Say(hero, TXT.Ch1_S3_002)
            .With(Camera.BlendTo("cam_hero_ots", 0.6f))
            .Await(ActorAnim(hero, "turn_around"))              // 先转身，转完才允许推进
            .AdvanceOn(Advance.Click),

        Par(ParallelMode.WhenAll,
            ActorMoveTo(priest, Anchor.Named("altar"), 1.5f),
            Camera.PushIn("cam_altar", 1.5f),
            Sfx(FmodAudioEvent.SFX_Footsteps_Stone)),

        Say(priest, TXT.Ch1_S3_003)
            .Voice(FmodAudioEvent.VO_Priest_Ch1_S3_003)
            .With(Vfx("fx_candle_flare", at: Anchor.Named("altar")))
            .AdvanceOn(Advance.VoiceEnd),

        Choose(
            Option(TXT.Ch1_S3_opt_ask)
                .Then(Seq(
                    Say(hero,   TXT.Ch1_S3_010),
                    Say(priest, TXT.Ch1_S3_011),
                    SetVar("flag.asked_about_wind", true))),

            Option(TXT.Ch1_S3_opt_leave)
                .Then(Say(priest, TXT.Ch1_S3_020))),

        If("flag.asked_about_wind",
            then: Say(paimon, TXT.Ch1_S3_030)),

        Stage.FadeOut(0.4f),
        Emit(QuestEventType.FilmCompleted, "ch1_s3"),
        Stage.FadeIn(0.4f));
}
```

调用侧：

```csharp
await using StageScope stage = await Stage.EnterAsync(new StageSpec
{
    HideHud   = true,
    Letterbox = true,
    LockInput = true,
    Actors    = new[] { hero, paimon, priest },
    Anchors   = "anchor_church_s3",
    Preload   = new[] { "seq_church_enter", "fx_candle_flare" }
});

StoryResult result = await narrative.RunAsync(Ch1Scene3.Build(), stage);
```
