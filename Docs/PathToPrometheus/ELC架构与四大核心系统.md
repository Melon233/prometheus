# Path to Prometheus：Entity-Logic-Component 架构与四大核心系统

> 本文基于 Prometheus 当前代码实现，说明游戏运行时采用的 Entity-Logic-Component（ELC）架构，以及 Entity、Animation、Effect、AI 四个核心系统的职责、实现与演进方向。

## 1. 为什么选择 ELC

Prometheus 的 ELC 不是传统 ECS，也不是以 `MonoBehaviour` 为中心的脚本集合。它将一个玩法对象拆成三类角色：

- **Entity**：对象的组合根，表示“这是同一个玩法对象”，负责组织 Component 与 Logic，并承载统一生命周期。
- **Component**：数据和外部引用，回答“对象拥有什么”，例如属性、运动状态、Spine、碰撞盒和效果接入状态。
- **Logic**：行为和规则，回答“对象能做什么”，例如移动、攻击、受击、死亡、效果接入和敌人决策。
- **System**：单局范围的公共服务与调度器，处理不应归属于某一个 Entity 的能力，例如实体注册、效果结算、输入、相机和世界管理。

一句话概括 ELC：**以 Entity 作为生命周期边界，以 Component 保存数据，以 Logic 组合行为，再由单局 System 协调跨实体能力。**

这种拆分兼顾了 Unity 项目的工程现实：场景对象和第三方组件仍可保留在 GameObject 上，同时玩法流程不会散落在大量 `MonoBehaviour.Update` 中。它也与纯 ECS 有明确区别：当前架构强调面向对象的聚合、稳定的 Logic 顺序和显式生命周期，而不是 Archetype、Chunk 与批量数据计算。

## 2. 总体实现

### 2.1 对象关系

```text
Core
└── GameplayKit（单局玩法世界）
    ├── EntitySystem（Entity 注册、更新、回收、字段监听）
    ├── EffectSystem（全局效果运行时）
    ├── Input / Camera / World / Team / ... System
    └── Entity
        ├── Component（数据、Unity 引用、运行态）
        └── Logic（行为、状态、系统适配）
```

`GameplayKit` 是一局游戏的组合根。它在初始化阶段注册公共 `XSystem`，创建初始 Entity，并提供 `GetSystem<T>()` 让 Entity 内部的 Logic 显式取得本局服务。系统不依赖进程级单例，因此理论上可以同时存在多个隔离的战斗上下文。

需要特别说明：本文沿用“Animation System”和“AI System”作为功能子系统名称，但它们当前并不是注册到 `GameplayKit` 的同名 `XSystem`。动画以 `SpineComponent` 为单 Entity 仲裁中心，AI 以 `EnemyAiBrain` 为单敌人运行时；这是当前架构的重要边界。

### 2.2 Entity 生命周期

Entity 使用显式状态机管理生命周期：

```text
Created -> Registered -> Active -> DespawnRequested -> Disposed
```

1. 构造 Entity，并通过 `AddComp`、`AddLogic` 完成静态组合。
2. `EntitySystem.AddEntity` 分配单局唯一 `EntityId`，绑定所属 `GameplayKit`。
3. `Entity.AfterNew` 按 `OrderTag + Logic 注册顺序` 排序并初始化 Logic。
4. Active 阶段由 `EntitySystem` 每帧依次调用 Entity，再由 Entity 调用满足门禁条件的 Logic。
5. `RequestDispose` 立即进入 `DespawnRequested`，停止剩余行为，并在安全边界完成反向清理。

Logic 的默认阶段顺序从 Input、Talent、Buff、Gameplay 一直延伸到 AfterGameplay。同阶段使用注册序号保证稳定性。除此之外，`LogicControlRequirement` 将行动、移动和主动技能权限统一映射到 `PropertyComponent`，使眩晕、定身、沉默等控制效果不需要逐个操作 Logic。

### 2.3 单帧调度

`GameplayKit.OnUpdate` 的核心顺序为：

```text
处理待回收 Entity
-> 所有 System.BeforeEntityUpdate
-> EntitySystem.UpdateEntities
-> 所有 System.OnUpdate
-> 再次处理待回收 Entity
```

这使输入等前置数据能够先准备，Entity Logic 在中间执行玩法行为，Effect 等公共运行时随后推进持续时间和周期结算。Entity 在更新中请求销毁时不会修改正在遍历的容器，并且会立即停止自身剩余 Logic。

---

## 3. Entity System

### 一句话概括

**Entity System 是单局内所有 Entity 的权威注册表、稳定调度器、生命周期管理器和可观察字段入口。**

### 实现思路

`EntitySystem` 在 `GameplayKit` 构造时作为首个内建系统创建。它通过 `EntityId -> Entity` 的稳定容器保存实体，负责注册、查询、逐帧更新和回收，不让 Entity 自己操作全局集合。

Entity 只负责自身 Component 与 Logic 的组合；跨实体定位、单局归属、更新边界和最终销毁都交给 Entity System。UI 等观察者也不直接长期抓取组件回调，而是通过 `EntitySystem.Listen` 订阅指定 Entity 的 `ModifiableProperty`。

### 技术细节

**稳定标识与归属**

- `EntityId` 从 1 开始按单局递增，0 永远表示无效对象。
- `AddEntity` 同时绑定 `GameplayKit`，Entity 由此显式解析 Effect、Team 等协作系统。
- ID 是运行时句柄，不是存档 ID 或网络全局 ID；跨局持久化不能直接复用。

**更新与顺序**

- `XMap<int, Entity>` 同时承担查询和稳定遍历。
- Entity 内 Logic 按 `OrderTag` 排序，同阶段再按注册顺序执行。
- 每次 Logic 执行前检查启停条件、阻塞计数与控制权限。
- Entity 一旦请求回收，本帧剩余 Logic 立即停止，避免死亡后继续攻击或移动。

**安全回收**

- 遍历期间直接删除会转为 `RequestRemoveEntity`。
- 待回收对象进入 `pendingEntityRemovals`，在帧前、Entity 更新结束和整帧末尾排水。
- 最终清理按 Logic 初始化顺序的逆序执行 `OnDisable` 和 `OnDispose`，再解绑 Component、Logic 与 GameObject。
- Entity 回收时一并注销其字段监听和 Team 关系，避免 UI 回调引用已经失效的组件。

**字段监听**

- `Listen<TComponent>` 通过字段选择器绑定 `ModifiableProperty`。
- 默认注册后立即同步一次，之后只在最终值实际变化时回调。
- 返回幂等 `ListenHandle`，支持调用方精确退订。
- 监听按 EntityId 归组，Entity 或整个系统释放时自动清理。

### 后续发展

1. **创建入口解耦**：当前初始小队与敌人的实例化仍在 `EntitySystem` 内，后续可交给 Spawn/Factory System，使 Entity System 聚焦生命周期与索引。
2. **多索引查询**：增加按类型、阵营、标签或空间分区的只读索引，减少业务系统遍历或依赖 Unity 场景查询。
3. **持久化标识分层**：明确 Runtime EntityId、配置 ID、存档 ID 和网络 ID 的转换边界。
4. **结构变更命令缓冲**：允许更新过程中排队创建 Entity，而不仅是排队删除，以支持召唤物和复杂生成链路。
5. **调试可观测性**：提供 Entity、Component、Logic、生命周期与字段监听的运行时检查面板。

### 补充：应坚持的边界

Entity System 不应演变成容纳所有玩法规则的“总管”。它管理 Entity，但不决定攻击公式、动画选择或 AI 状态。新增能力应优先落在 Component、Logic 或独立 System，再通过显式接口协作。

---

## 4. Animation System

### 一句话概括

**Animation System 将稳定的玩法语义解析为角色专属 Spine 动画，并通过所有权、优先级和播放会话统一仲裁动画与时间轴事件。**

### 实现思路

动画子系统以“语义与资源解耦”为核心。Logic 只请求 `AnimationSemantic.Idle`、Attack 等稳定语义，不持有具体 Spine 资源；角色自己的 `AnimationLibrary` 将语义映射到 `AnimationLine`。

`SpineComponent` 是单 Entity 的唯一播放仲裁者。它判断新请求是否能抢占主轨，创建 `AnimationPlayback` 会话，并保证某个 Logic 只能停止自己拥有的动画。玩法 Logic 订阅会话事件来开启碰撞盒、生成特效或处理结束，而不是直接订阅裸 `TrackEntry`。

### 技术细节

**配置层**

- `AnimationSemantic` 是跨角色稳定 API。
- `AnimationLibrary` 是角色共享的只读 `ScriptableObject`，建立 `Semantic -> AnimationLine` 唯一索引。
- 同一语义出现多个 AnimationLine 时标记冲突并拒绝播放，而不是随机选择。
- `AnimationMixDurationMatrix` 保存有向过渡混合时长，未覆盖项使用统一默认值。

**AnimationLine**

- 包装 `AnimationReferenceAsset`，保留导入的 Spine 事件。
- 将 Unity 中配置的事件、强类型 Gameplay Command 和 FMOD 音频标记合并到克隆的 EventTimeline，不修改导入源动画。
- 当前强类型命令包括 `EnableHitbox` 与 `DisableHitbox`，正常命中窗口由每条实际攻击动画独立配置。
- 运行时动画使用非序列化缓存，资源校验或编辑后失效重建。

**播放仲裁**

- 主轨优先级从 Idle、Locomotion、Attack、Dodge、Skill 到 Death 递增。
- 低优先级请求无法打断高优先级会话；同级允许状态切换；高优先级可抢占。
- `AnimationOwner` 标记 Idle、移动、敌人攻击、受击、死亡等所有者，避免一个 Logic 停止另一个 Logic 的动画。
- 单段或序列播放都封装为 `AnimationPlayback`。

**会话生命周期**

- 会话转发普通 `EventReceived`、强类型 `CommandReceived` 和唯一一次 `Finished`。
- 结束原因区分 `Completed`、`Interrupted`、`Stopped`、`Disposed`。
- 会话结束时对称解绑全部 TrackEntry 回调，防止迟到事件访问已释放 Logic。
- FMOD 动画标记先由音频运行时消费，其余命令和事件才交给玩法订阅者。

### 后续发展

1. **扩大强类型时间轴命令**：为位移、霸体、镜头、武器挂点、投射物等建立带类型载荷的命令，继续减少字符串事件协议。
2. **分层与遮罩**：在主轨之外增加上半身、武器和附加表现轨道，并明确跨轨所有权与混合规则。
3. **配置预检**：在构建前扫描语义缺失、冲突、命中窗口不闭合、音频引用和矩阵覆盖问题。
4. **网络与回放**：将“语义 + 播放参数 + 起始 Tick”定义为可记录命令，表现侧按确定性时间重建。
5. **可视化调试**：实时显示当前 Owner、Priority、Semantic、会话版本、结束原因和事件流。

### 补充：动画不是玩法状态机

动画系统负责表现仲裁和时间轴通知，不应成为角色玩法状态的唯一真相。能否移动、攻击和释放技能应由属性、Logic 与 Effect 决定；动画被打断时，Logic 必须依据会话结束原因做对称清理。

---

## 5. Effect System

### 一句话概括

**Effect System 把战斗事实转换为可配置、可堆叠、可回滚且具备因果链保护的统一效果结算。**

### 实现思路

效果链路为：

```text
EffectSignal
-> EffectTriggerDefinition
-> EffectRequest
-> EffectInstance
-> Operation / Result Signal
```

攻击 Logic 只发布“命中确认”等事实信号，不直接串联伤害、燃烧、眩晕和战意。Trigger 根据条件把事实转换为 EffectRequest，统一队列创建或更新 EffectInstance，再由 Operation 执行伤害、属性修改、控制状态、二次效果或派生信号。

每个 `GameplayKit` 持有唯一 `EffectSystem` 和 `EffectRuntime`。共享的 EffectDefinition、TriggerSet 与 EffectLibrary 只保存配置；层数、计时、随机状态和资源句柄全部留在单局 Runtime 与单个 EffectInstance 中。

### 技术细节

**生命周期与隔离**

- `EffectSystem` 是普通 C# `XSystem`，不依赖 MonoBehaviour 或进程级单例。
- `AfterNew` 在 Entity 初始化前创建 Runtime，使 `EffectLogic` 可以注册触发规则。
- `OnUpdate` 在 Entity 行为发布完当帧信号后执行 `Runtime.Tick(dt)`。
- `Dispose` 统一移除效果、触发注册和运行时资源，配置资产仍由 Unity 管理。

**Entity 接入**

- `EffectLogic` 从 `Entity.GameplayKit` 取得本局 Effect System。
- `EffectComponent` 保存 Runtime 引用、Entity 所有者和触发注册句柄。
- Entity 销毁时自动注销规则、移除持续效果并回滚属性或控制句柄。
- Buff 列表通过版本化可观察字段暴露给 HUD，而不是让 UI 修改 Runtime。

**效果模型**

- 即时效果只执行 OnApply；持续效果还可包含 Duration、Tick、Stack 和 Refresh 行为。
- 堆叠策略明确区分加层与刷新持续时间，周期计时不会因刷新而被意外重置。
- 持续属性与控制修改持有独立资源句柄，EffectInstance 移除时精确回滚自身贡献。
- `Caster` 表示直接行为释放者，`Source` 表示整条因果链的实际源头。

**递归与可观测性**

- Trigger 只能生成请求，不能直接递归执行效果。
- 子信号继承 `SignalChainId` 并增加深度，用于 OncePerSignalChain 和递归上限。
- `EffectRuntime` 使用显式随机种子，便于复现。
- SignalProcessed 观察者彼此隔离；表现层异常不能中断战斗结算。
- Trace 可记录效果链，结果信号则供 VFX、音效、飘字等表现系统消费。

### 后续发展

1. **结算上下文快照**：明确哪些公式读取施放时快照，哪些读取命中或 Tick 时实时属性。
2. **预测与回滚**：让 EffectRequest、随机采样和结果信号具备序列号，为联机预测和战斗回放准备稳定协议。
3. **效果查询 API**：提供按标签、来源、能力 ID 和控制类型的高效查询，避免业务直接遍历实例。
4. **配置编译**：把 ScriptableObject 图预编译为紧凑运行时数据，并在构建阶段完成引用与循环校验。
5. **性能预算**：针对大量单位的 Trigger 路由、公式求值、Tick 和信号观察者建立采样指标。
6. **调试工具**：展示活动效果、剩余时间、层数、来源、Modifier 贡献与完整信号链。

### 补充：事实与表现分离

Effect System 产出的是可验证的玩法结果。命中音效、受击动画和飘字可以观察 `DamageApplied` 等结果信号，但不应反向决定是否造成伤害。这样即使某个表现模块失效，结算仍保持完整。

---

## 6. AI System

### 一句话概括

**AI System 用共享只读定义描述状态图，用每敌人独立 Brain 保存决策状态，再由 Entity Logic 适配感知、移动、动画和战斗能力。**

### 实现思路

AI 子系统分为三层：

- `EnemyAiDefinition`：共享只读资产，声明状态、进入/逐帧/退出动作、转移条件和感知移动参数。
- `EnemyAiBrain`：单敌人纯 C# 运行时，解释定义并保存黑板、计时器、目标、随机源和当前状态。
- `EnemyAiLogic`：ELC 适配层，实现 `IEnemyAiAgent`，把 Brain 的抽象动作连接到 Physics、MotionComponent、SpineComponent 和 EffectRuntime。

这种结构让决策逻辑不直接依赖史莱姆预制体，也不会把可变数据写回共享 ScriptableObject。Brain 可在没有完整场景表现的情况下独立验证，Logic 则处理 Unity 与具体战斗链路。

### 技术细节

**资产化状态机**

- 状态具有稳定 ID，以及有序的 Enter、Tick、Exit 原子动作。
- 一条 Transition 的多个条件按逻辑与组合，可使用 `Negate` 反转。
- 满足条件的转移按高优先级优先，同优先级保持资产列表顺序。
- 每次决策最多切换一次，防止同一 Tick 内连续穿透多个状态。
- `ValidateOrThrow` 在创建 Brain 时校验半径关系、状态 ID、引用、动作与条件列表。

**独立运行态**

- 每个 Brain 拥有 `EnemyAiBlackboard`，保存 Target、HomePosition、PatrolPoint、状态时间、待机和攻击冷却。
- 感知与决策使用独立间隔，不必每帧执行 Physics 查询与状态图扫描。
- 巡逻使用 Brain 自己的 `System.Random`，不污染 Unity 全局随机状态，并可通过种子复现。
- Suspend 保留状态、目标和冷却，但取消攻击与移动；Resume 重放当前状态进入动作恢复意图。

**ELC 适配**

- `EnemyAiLogic` 声明 Act 控制要求；眩晕、受击和死亡通过 Entity 统一门禁暂停 Brain。
- 感知使用 `Physics.OverlapSphereNonAlloc` 和固定缓冲区，按距离选择最近有效目标。
- AI 只写 MotionComponent 的水平速度，重力 Logic 保持竖直速度，最终由 MotionLogic 合并移动。
- 动画通过语义、Owner 与 Priority 请求，不直接操作 Spine Track。
- 攻击动画的强类型命令开启或关闭碰撞盒；命中后发布 `HitConfirmed` EffectSignal。
- 单次攻击以目标实例集合去重，动画自然结束才进入完整冷却，被打断则不消耗完整冷却。

### 后续发展

1. **感知服务集中化**：大量敌人时，将逐 Brain Physics 查询升级为空间索引或批处理 Perception System。
2. **决策调度分帧**：按预算分散不同敌人的感知与决策时刻，避免同一帧峰值。
3. **组合式节点扩展**：在保持强类型动作与条件的前提下，引入子状态机、效用评分或轻量行为树处理复杂 Boss。
4. **目标与威胁系统**：从“最近合法目标”演进为伤害、距离、职业和事件共同驱动的仇恨表。
5. **导航能力抽象**：让 `IEnemyAiAgent` 对接 NavMesh、局部避障、跳跃连接和动态阻挡，而不修改 Brain。
6. **运行时诊断**：展示当前状态、候选转移、失败条件、目标、冷却和最近状态切换历史。
7. **存档与联机**：序列化状态 ID、黑板与随机状态，并定义服务端权威决策和客户端表现边界。

### 补充：何时需要真正的 AiSystem

当前 AI 规模下，每只敌人的 Logic 驱动独立 Brain，边界简单且与 ELC 一致。当单位数量增加、感知查询成为热点、需要统一分帧预算或服务端无表现模拟时，再引入注册到 `GameplayKit` 的 `AiSystem` 才有明确收益。届时它应负责任务调度与共享查询，而不是吞并 Brain 的单体状态。

---

## 7. 四个系统如何协作

以敌人一次普通攻击为例：

```text
EntitySystem 驱动 EnemyAiLogic
-> EnemyAiLogic.Tick 推进 EnemyAiBrain
-> Brain 满足 Attack 转移并请求 StartAttack
-> EnemyAiLogic 向 SpineComponent 请求 Attack 语义动画
-> AnimationPlayback 收到 EnableHitbox 命令
-> ColliderProxy 命中目标
-> EnemyAiLogic 发布 HitConfirmed EffectSignal
-> EffectRuntime 根据 Trigger 生成并执行伤害 Effect
-> 发布 DamageApplied / Killed 等结果信号
-> 音频、VFX、飘字、受击与死亡表现消费结果
-> Entity 死亡后请求安全回收
-> EntitySystem 在安全边界反向释放 Logic、Effect 绑定与场景对象
```

这条链路体现了当前架构最重要的分工：

- Entity System 决定对象是否存在、何时更新、何时清理。
- AI System 决定敌人此刻尝试做什么。
- Animation System 决定动作如何表现、何时打开判定窗口。
- Effect System 决定命中之后实际发生什么。

任一层都通过稳定语义、接口、信号或生命周期句柄与下一层协作，而不是直接修改对方内部状态。

## 8. 当前优势与主要风险

### 当前优势

- 单局 `GameplayKit` 隔离公共系统，降低全局单例污染。
- Entity 生命周期和 Logic 顺序明确，回收不会破坏帧内遍历。
- Component、Logic 与 System 的职责边界已经贯穿动画、AI 和效果链路。
- Animation 与 Effect 使用共享只读配置、独立运行态，适合多实例复用。
- 强类型语义、命令与结果信号减少字符串协议。
- 句柄化监听与 Modifier 使注销、回滚和清理可以对称完成。

### 主要风险

- Animation/AI 的“System”命名与实际 `XSystem` 形态不同，团队沟通中必须区分功能子系统和全局调度系统。
- Entity 创建职责仍部分耦合在 Entity System，扩展动态生成时可能膨胀。
- AI 感知目前按敌人执行固定容量 Physics 查询，单位规模扩大后会出现精度和峰值问题。
- 动画时间轴命令类型较少，复杂技能仍可能回退到普通字符串事件。
- 四个系统虽有单元和回归测试，但缺少统一的运行时链路可视化，跨系统问题定位成本仍高。

## 9. 建议的演进顺序

1. **先补可观测性**：统一展示 Entity 生命周期、Logic 门禁、动画会话、AI 状态和 Effect 信号链。
2. **再做配置预检**：在进入运行时前发现动画语义冲突、AI 图引用错误和 Effect 循环依赖。
3. **然后做规模化调度**：根据 Profiler 数据决定是否引入集中感知、分帧 AI 和效果批处理。
4. **最后建立确定性协议**：统一随机种子、逻辑 Tick、输入命令、动画语义和 Effect 结果序列，为回放与联机服务。

演进时应保持一个原则：**共享资产只读、运行状态有明确所有者、跨系统协作使用稳定协议、所有注册都能对称释放。** 只要这四点不被破坏，ELC 可以在保留 Unity 开发效率的同时，逐步支撑更复杂的战斗、更多实体以及更严格的可测试性需求。

## 10. 关键代码索引

- `Assets/Prometheus/Framework/GameplayKit/Entity.cs`：Entity 生命周期、Logic 排序与控制门禁。
- `Assets/Prometheus/Framework/GameplayKit/Component.cs`：Component 基础契约。
- `Assets/Prometheus/Framework/GameplayKit/Logic.cs`：Logic 基础契约。
- `Assets/Prometheus/Gameplay/GameplayKit.cs`：单局 System 注册与帧循环。
- `Assets/Prometheus/Gameplay/EntitySystem.cs`：Entity 注册、更新、监听和安全回收。
- `Assets/Prometheus/Gameplay/Animation/README.md`：动画子系统详细约束。
- `Assets/Prometheus/Gameplay/Animation/AnimationLibrary.cs`：动画语义索引和混合矩阵。
- `Assets/Prometheus/Gameplay/Animation/AnimationLine/AnimationLine.cs`：动画资源包装与时间轴命令。
- `Assets/Prometheus/Gameplay/Animation/AnimationPlayback.cs`：播放会话协议。
- `Assets/Prometheus/Gameplay/Component/SpineComponent.cs`：单 Entity 动画仲裁中心。
- `Assets/Prometheus/Gameplay/Effect/README.md`：Effect 完整规则与接入约束。
- `Assets/Prometheus/Gameplay/Effect/Runtime/EffectSystem.cs`：单局效果系统生命周期。
- `Assets/Prometheus/Gameplay/Ai/EnemyAiDefinition.cs`：共享 AI 状态图定义。
- `Assets/Prometheus/Gameplay/Ai/EnemyAiBrain.cs`：单敌人决策运行时与黑板。
- `Assets/Prometheus/Gameplay/Ai/EnemyAiLogic.cs`：AI 与 ELC、动画、物理、Effect 的适配层。
