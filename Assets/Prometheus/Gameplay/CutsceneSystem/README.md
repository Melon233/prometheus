# CutsceneSystem（演出系统）

## 目标

按 ScriptableObject 配置播放线性演出：一段演出由若干**步骤**组成，每步同时开始一组 **Action**（镜头、演员、音效、特效、对话）。
同一时间至多播放一段演出。演出系统**不认识任务系统**，任务通过 `CutsceneFinishedEvent` 得知演出结束。

## 数据结构

```
CutsceneDefinition (SO，Assets/BundleResources/Narrative/)
├─ anchorId                  作为坐标原点的 WorldAnchor id；所有坐标都相对它
├─ skippable                 是否允许取消键跳过
├─ actors: List<CutsceneActorDef>
│    ├─ key                  演出内唯一，Action 用它引用演员
│    └─ source               Player | Scene(anchorId) | Spawn(spawnPrefab, spawnPosition, spawnFaceRight)
└─ steps: List<CutsceneStep>
     ├─ duration             非对话步骤的持续秒数（必须 > 0）
     └─ actions              [SerializeReference] List<CutsceneAction>
```

文本（说话人、台词、选项）全部是 Luban 表 `TbText` 的整数 id，SO 中不保存任何字符串文案（见 `NarrativeText`）。

### 步骤推进规则

- 含 `DialogAction` 的步骤等待玩家**点击**推进；台词带选项时改为等待**选择**，此时点击被忽略。每步至多一句对话。
- 不含对话的步骤在 `duration` 秒后自动进入下一步。
- **Action 的效果跨步骤延续**：步骤时长只决定何时开始下一步，不截断尚未播完的镜头、移动、音效或特效。
- 同一**通道**（`Channel`）上的新 Action 开始时，旧的运行体先被 `Finish`（例如两个镜头 Action 不会同时驱动镜头）。当前通道：`camera`、`actor:{key}:move`。
- 选项只是让玩家表态，**不影响流程**：任何选择都进入下一步，NPC 的回应就是下一步。

### Action 一览

| Action | 持续型 | 跳过时的最终状态 | 说明 |
| --- | --- | --- | --- |
| `CameraAction` | 是（时长 0 为切镜） | 镜头落到目标位姿 | 起点默认取镜头当前位置；可配置注视演员 |
| `ActorMoveAction` | 是（时长 0 为瞬移） | 演员落到目标点并切到到达动画 | 玩家只允许瞬移；非玩家可配移动/到达动画，自动朝向移动方向 |
| `ActorFaceAction` | 否 | 同正常 | 左右朝向 |
| `ActorVisibleAction` | 否 | 同正常 | 显隐；玩家只关骨骼渲染，演出结束自动恢复可见 |
| `ActorAnimationAction` | 否 | 循环动画或其接续的循环动画 | Spine 动画名；不支持玩家 |
| `AudioAction` | 是（播完即止） | 不补播 | FMOD 事件，可跟随演员；演出结束时淡出停止 |
| `VfxAction` | 是（lifetime，0 = 到演出结束） | 不生成 | 特效 prefab，挂演员或锚点 |
| `DialogAction` | 否 | 不显示 | 说话人（0 = 旁白）、台词、可选选项 |

每个 Action 必须实现三件事：`Begin`（正常开始，持续型返回运行体）、`ApplyFinalState`（跳过时只改结果、不产生过程）、`Validate`（资产校验）。
Action 是资产里的只读数据，会被每次播放共享，**运行时状态只能放在 `Begin` 返回的 `CutsceneActionRun` 中**。

### 演员来源

| 来源 | 绑定 | 能力 | 演出结束后 |
| --- | --- | --- | --- |
| `Player` | 当前上场角色（`ITeamSystem.ActiveMember`） | 瞬移、朝向、显隐 | 保留位置，恢复可见 |
| `Scene` | 场景中同 id 的 `WorldAnchor` 对象，骨骼取其子节点的 `SkeletonAnimation` | 全部 | 保持最终状态（位置、动画） |
| `Spawn` | 在临时对象根下实例化 prefab | 全部（无骨骼的道具只能移动/显隐） | 销毁 |

玩家不支持自定义动画：玩家动画受 `SpineComponent` 优先级仲裁，且只接受 `AnimationSemantic`。

## 运行链路

1. `AfterNew`：在 `PersistentRoot` 下创建停用的演出镜头（`CinemachineCamera`）。场景锚点由先注册的 `WorldAnchorSystem` 索引。
2. `Play(definition)`：
   1. 正在播放时抛出（调用方逻辑错误）；`definition.Validate` 失败或引用的锚点在场景中不存在时抛出。
   2. 建立演员句柄，Spawn 来源实例化到临时对象根下。
   3. 演出镜头对齐当前输出镜头位姿并继承跟随镜头的 Lens，激活后申请 `ICameraSystem.AcquireCutsceneCamera` 租约（优先级 1000）。
   4. 以 `InputContexts.Cutscene` 独占全部输入动作——玩家角色因此原地停下。确认键推进台词，取消键跳过。
   5. 发布 `CutsceneStartedEvent`，进入第一步。
3. `OnUpdate`：处理本帧的推进/跳过请求，驱动 `CutscenePlayback`；走完最后一步即收尾。
4. 收尾（播完或跳过）：`Finish` 全部运行中的 Action → 恢复玩家可见 → 销毁临时对象根 → 归还镜头与输入租约 → 停用演出镜头 → 清空台词 → 发布 `CutsceneFinishedEvent(definition, skipped)`。
5. 跳过：运行中的 Action 立即 `Finish`，剩余步骤的 Action 依次 `ApplyFinalState`，然后走同一收尾。
6. `Dispose`（会话销毁时仍在播放）：只归还租约并销毁系统根，不做 Action 落位、不发事件——进程退出时场景对象可能已被销毁（ARCH-LIFE-004）。

### 对外状态与事件

| 名称 | 说明 |
| --- | --- |
| `ICutsceneSystem.IsPlaying` / `Current` | 播放状态 |
| `ICutsceneSystem.CurrentLine` | 当前台词快照（文本已解析）；为空表示没有台词 |
| `Advance()` / `Choose(i)` / `Skip()` | UI 与输入提交的玩家操作 |
| `CutsceneStartedEvent` | 演出开始 |
| `CutsceneLineChangedEvent` | 台词变化 |
| `CutsceneFinishedEvent` | 演出结束（含是否跳过）；跳过同样算看完 |

## 与其他系统

```
QuestSystem ──Play()──▶ CutsceneSystem ──租约──▶ CameraSystem
     ▲                        │        ──租约──▶ InputSystem
     └── CutsceneFinishedEvent┘        ──读取──▶ TeamSystem（玩家演员）
                                       ──查询──▶ WorldAnchorSystem（坐标原点、场景演员）

CutsceneUISystem（UI 层）── 监听开始/结束 ──▶ 收起/恢复 HudPanel，打开/关闭 DialoguePanel
DialoguePanel ── 读 CurrentLine、监听 CutsceneLineChangedEvent ── 点击 Advance / 选项 Choose
```

## 场景摆放

- 坐标原点：空物体挂 `WorldAnchor`，填 `anchorId`。旋转会参与坐标换算。
- 场景演员：演员根节点挂 `WorldAnchor`，Spine 骨骼作为其子节点（需要设置初始皮肤，否则不渲染）。同一个 NPC 的锚点同时供任务挂交互点使用。
- 编辑期可用菜单 `Prometheus/Narrative/校验场景锚点` 核对演出与任务引用的锚点是否都在当前场景中。

## 新增一种 Action

1. 在 `Actions/` 下新增 `[Serializable]` 的 `CutsceneAction` 子类（需要无参构造），实现 `Begin` / `ApplyFinalState` / `Validate`，持续型同时写一个 `CutsceneActionRun`。
2. 需要互斥时重写 `Channel`。
3. Inspector 的类型下拉（`SerializeReferencePicker`）会自动列出新类型，无需改编辑器代码。
4. 已有资产按类名记录 `[SerializeReference]` 类型，**改类名、命名空间或跨程序集移动**需要同步迁移资产（ARCH-UNITY-002）。

## 校验

- 运行时：`Play` 前调用 `CutsceneDefinition.Validate`，失败直接抛出。
- 编辑期：菜单 `Prometheus/Narrative/校验全部演出与任务`。
- `CutsceneActorSource` 按下标序列化，已加入 `SerializedEnumLayoutTests` 黄金名单，只允许末尾追加。

## 测试场景

`Assets/BundleResources/Scene/CutsceneTest.unity`（世界 id `w_cutscene_test`，当前为世界目录中的 `MainWorld`）配合任务 `Quest_Prologue` 覆盖了全部 Action、三种演员来源、三种任务触发器、选项与跳过。

## 已知限制

- 没有打字机效果，台词整句显示。
- 没有自动播放模式；对话步骤只能点击推进。
- 演出期间敌人 AI 不暂停，策划需保证触发演出的地点是安全区。
- 镜头只做位姿插值，不使用 Cinemachine 的 Body/Aim 与 Blend；需要复杂运镜时建议后续增加 `PlayTimelineAction`。
