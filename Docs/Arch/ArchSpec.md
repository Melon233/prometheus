# Prometheus 架构硬约束（ArchSpec）

> 状态：生效
> 适用范围：`Assets/Prometheus` 下的运行时 Framework、Gameplay、UI 代码及其组合根；第三方插件、生成代码和独立渲染插件边界不在本文重构范围内。
> 规范词：**必须**表示不可绕过的要求，**禁止**表示不得新增或依赖的行为，**允许**表示满足所列边界时可采用的实现。

## 0. 元规则：特别的写法是规则错误的证据

- `ARCH-META-001`：新增或修改逻辑时，如果因为某条既有规则而不得不采取**特别的写法**，必须先评估该规则是否应当修改，并记录结论；不得默默用特别写法绕过。
- `ARCH-META-002`：**按整数下标序列化进资产的枚举，必须被黄金名单测试钉住**。这类枚举被插入或重排时，已有资产不报错、不崩溃，只是静默读到相邻成员——没有自动化执行手段的话，这条规则等于不存在。当前受钉枚举见 [SerializedEnumLayoutTests](../../Assets/Prometheus/Tests/Architecture/SerializedEnumLayoutTests.cs)；新增此类枚举必须同时加入该测试。

「特别的写法」指下列这类东西：额外的重入或空值守卫、只为让别人读到而提升的可见性、只为绕开规则而存在的中间层或静态属性、一段解释「为什么必须按这个顺序写」的注释、一套用来校验本可由编译器保证的事情的测试设施。

这条规则有四个当场验证的样本，见 `Docs/Arch/GameFlow.md` 第 10 节。四个样本里没有一个是「代码写得不好」，全部是规则或模型的问题。

**执行手段**：代码评审与 `Docs/Arch/` 下的决策记录。这是本文唯一一条不由自动化执行的约束——它管的正是「自动化该不该换个方向」。

## 1. 本规范如何被执行

**未被执行的规范只是注释。** 本文每一条约束都必须对应一种自动化执行手段，否则它就不该出现在这里：

| 执行手段 | 覆盖范围 |
| --- | --- |
| **asmdef 装配划分** | 分层依赖方向。编译器直接拒绝反向引用，无法绕过 |
| **`InternalsVisibleTo`** | 实现类型的可见性。只有唯一组合根和测试装配能看到 `internal` 实现 |
| **架构测试** `Assets/Prometheus/Tests/Architecture/ArchitectureConstraintTests.cs` | 编译器表达不了的部分：实现类型的可见性与唯一性、若干源码级禁令 |
| **显式接口实现** | 特权操作（如驱动实体生命周期）必须写出显式转换，无法被随手调用 |

新增一条约束时，必须同时给出它的执行手段。

## 2. 分层

运行时代码划分为四个装配，依赖方向由 asmdef 强制：

```text
Runtime（Bootstrap，唯一组合根）
  └─ Prometheus.UI
       └─ Prometheus.Gameplay
            └─ Prometheus.Framework
```

- `ARCH-LAYER-001`：下层装配禁止引用上层装配。由 asmdef 强制，并由架构测试 `Layer_DoesNotReferenceUpperLayers` 复核（防止有人放宽 asmdef）。
- `ARCH-LAYER-002`：`Prometheus.Framework` 禁止出现任何玩法领域概念。框架需要与玩法协作时，必须声明端口由玩法层实现，而不是引用玩法类型。
- `ARCH-LAYER-005`（生成配表例外，2026-09 确立）：**`Prometheus.Config.Generated` 不受 `ARCH-LAYER-002` 约束**，框架层允许直接引用并持有它。
  - 理由：该装配是导表工具的产物，只含只读数据类型，没有行为、没有依赖、不表达任何玩法规则。为它引入端口与类型参数，换来的只是 `Core.Config.Get<Tables>()` 这种每个调用点都要重复类型参数的写法，以及一层没有第二个实现的抽象——按 `ARCH-META-001`，这正是「特别的写法」。
  - 边界：例外只覆盖**导表生成的装配**。框架层引用任何手写的玩法装配仍然禁止。
  - 执行手段：asmdef 引用白名单与代码评审。
- `ARCH-LAYER-003`：`Runtime`（`Assets/Prometheus/Bootstrap`）是唯一被允许认识具体实现类型的装配。三个下层装配各自通过 `AssemblyInfo.cs` 中的 `InternalsVisibleTo("Runtime")` 只向它开放实现。
- `ARCH-LAYER-004`：`Prometheus.Rendering.*` 与 `Prometheus.NetworkKit` 是独立边界装配，不参与上述四层顺序。

## 3. 架构角色

| 角色 | 当前职责 | 所有者 |
| --- | --- | --- |
| `Core` | 创建、注册、更新和逆序释放基础 Kit，提供唯一跨模块入口 | `Entry` |
| `GameplayKit` | **会话工厂**：按组合根的声明建立/销毁玩法会话，并驱动世界相位 | `Core` |
| `WorldSession` | 世界切换的编排：退出旧世界 → 加载场景 → 进入新世界 | `Entry` |
| Kit | 提供资源、配表、事件、UI、玩法容器等跨模块基础能力 | `Core` |
| `IGameplaySystemInstaller` | 回答"这一次会话由哪些 System 组成"；同步、纯构造 | `GameplayKit` |
| System | 管理单局内一个明确玩法领域的状态和行为 | `GameplayKit` |
| `ServiceSystem` | 单局唯一网络会话与业务无关的通用请求通道；不认识任何具体业务 | `GameplayKit` |
| `*Gateway` | 某一领域的网络适配器：组包、解包、Push 分类；不保存领域状态 | `GameplayKit` |
| Entity / Logic / Component | 表达运行时对象身份、行为和数据组合 | `EntitySystem` 管理 Entity 生命周期 |
| Event | 描述已经发生的全局玩法事实 | `IEventKit` |
| UI | 读取 System 接口并把用户命令转交给玩法层，不拥有权威玩法状态 | `IUIKit` 与对应面板 |

## 4. Kit 约束

- `ARCH-KIT-001`：可被 `Core` 注册的 Kit 接口必须继承 `IKitContract`。
- `ARCH-KIT-002`：正式 Kit 的具体实现必须保持 `internal sealed`；由架构测试 `KitImplementations_AreInternalSealed` 执行。
- `ARCH-KIT-003`：业务代码必须通过 `Core.Asset`、`Core.Event`、`Core.UI`、`Core.Gameplay` 使用正式 Kit；`Core.GetKit<T>()` 仅供 Core 生命周期管理和诊断。由架构测试 `Sources_DoNotResolveKitsByGetKit` 执行。
- `ARCH-KIT-004`：Kit 的创建、初始化、更新和释放顺序只允许由 `Core` 控制；释放顺序必须与注册顺序相反。
- `ARCH-KIT-006`（配表所有权）：`ConfigKit` **直接持有**生成的 `Tables`，并以 `IConfigKit.Tables` 公开。读表统一写作 `Core.Config.Tables.Tb*`。禁止为配表再建第二份加载入口或缓存：一个进程只有一份表数据，它的所有者是 `ConfigKit`。依据见 `ARCH-LAYER-005`。
- `ARCH-KIT-005`：Kit 的构造函数禁止产生全局副作用。四个静态入口只允许在 `Core.RegisterKit`（`Core.PublishStaticEntry`）中写入、在 `Core.Dispose` 中清空。`new` 一个 Kit 与把它接入当前 Core 必须是两件可以分别发生的事。

## 5. 组合根约束

- `ARCH-COMP-001`：`Entry` 是 Player Build 唯一直接入口；`Core` 是 Kit 唯一组合根；`IGameplaySystemInstaller` 的实现是 System 与初始世界内容的唯一组合根。
- `ARCH-COMP-002`：`GameplayKit.CreateSessionAsync` 只接受一个安装器。禁止让 `Core` 或 `GameplayKit` 认识任何玩法参数类型。
- `ARCH-COMP-003`：新增 System 必须先定义最小接口，再在 `PrometheusSystemInstaller.Install` 中完成创建和注册。注册顺序即初始化顺序，`GameplayKit` 负责逆序释放；该顺序**由构造依赖强制**（要用谁就得先拿到谁的返回值），不再是需要人工维护的约定。
- `ARCH-COMP-004`：启动链路禁止传递参数。资源包名由 `AssetKit.DefaultPackageName` 固定；跨场景运行时对象统一挂在 `PersistentRoot.Shared` 下，不再逐层传递根节点。禁止在 `Entry` 或任何 MonoBehaviour 上序列化玩法参数，也禁止重新引入集中式启动配置对象。
- `ARCH-COMP-006`（资源自持）：每个资源地址必须由**使用它的系统**以 `private` 常量持有，并由该系统在自己的 `AfterNewAsync` 中加载。禁止任何一方替另一方加载资源，禁止为了让别人代加载而提升地址常量的可见性。组合根不接触任何资源地址。
- `ARCH-COMP-005`：手工搭建 Core 的测试必须提供与正式链路一致的入口集合（至少 `Core.Asset`、`Core.Event`、`Core.Gameplay`）。生产代码不为"入口缺失"增加空值兜底——那是时序错误，不是可恢复状态。

## 6. System 约束

- `ARCH-SYS-001`：可被 `GameplayKit` 注册的 System 接口必须继承 `ISystemContract`。
- `ARCH-SYS-002`：System 具体实现必须保持 `internal sealed`；外部只允许持有 `I*System`。由架构测试 `SystemImplementations_AreInternalSealed` 执行。
- `ARCH-SYS-003`：System 必须以接口作为注册键；禁止 `GetSystem<ConcreteSystem>()`。由架构测试 `Sources_DoNotQuerySystemsByConcreteType` 执行。
- `ARCH-SYS-004`：跨 System 调用必须依赖对方接口，禁止读取对方私有缓存、网络客户端、会话或子服务。
- `ARCH-SYS-005`（**已于 2026-09 反转**）：公共 System 之间的依赖**必须**通过构造函数注入 `I*System` 契约建立，**禁止**在 System 实现内出现 `Core.Gameplay`。
  - 依赖因此写在构造函数签名上，编译器可见；依赖成环从「能构造但运行时死循环」变成「编译不过」；注册顺序由 C# 强制。
  - **服务定位保留给 Unity 创建的对象**：MonoBehaviour、`UIPanel`、Entity 的 Logic/Component、`Ports/Runtime/` 下的端口适配器——它们由 Unity 或运行时实例化，没有可注入的构造时机。
  - **Kit 不在此列**：`Core.Asset`、`Core.Event`、`Core.UI` 允许在任何位置直接引用。Kit 是进程级基础能力而非会话级玩法依赖，注入它们只会增加噪声。
  - 会话级共享状态（非 System 的普通对象）由组合根创建并构造注入，与注入 System 契约形式一致。
  - 旧规则的代价见 `Docs/Arch/GameFlow.md` 第 4.2 节。
- `ARCH-SYS-006`：Entity 的注册、查询、监听和回收必须经 `IEntitySystem`；单局至多存在一个 `IEntityDriver` 实现。由架构测试 `EntityDriver_IsUnique` 执行。
- `ARCH-SYS-007`：`IGameplayKit` 禁止公开任何玩法领域概念。"当前上场角色"之类的状态由对应 System 自己发布（`ITeamSystem.ActiveMember`），不由框架层容器代理。会话与世界的生命周期（`CreateSessionAsync` / `DestroySession` / `EnterWorldAsync` / `ExitWorld`）是框架级组合概念而非玩法领域概念，允许公开。
- `ARCH-SYS-008`：System 允许由 UI 层拥有。它仍由组合根统一注册，并只能向下依赖 Gameplay 与 Framework。当前 UI 层没有注册任何 System。

## 7. 事件约束

- `ARCH-EVT-001`：跨 System、Entity 与 UI 广播的全局玩法事实必须通过唯一的 `IEventKit`，发布和订阅统一使用 `Core.Event`。
- `ARCH-EVT-002`：禁止新增静态事件总线或其他与 `IEventKit` 平行的全局事件通道。由架构测试 `Sources_DoNotDeclareStaticEventBuses` 执行。
- `ARCH-EVT-003`：全局事件载荷必须实现 `IEvent`，对外只提供只读属性，并在构造时形成完整快照；禁止发布后修改载荷。
- `ARCH-EVT-004`：事件载荷的**具体类型**是唯一路由键。载荷类型必须定义在它所属的领域目录中，禁止在框架层新增集中式事件枚举或事件注册表。新增一个全局事件不应当需要修改 EventKit。
- `ARCH-EVT-005`：订阅者必须在自身释放或失活边界，用同一个委托实例对称退订。`EventKit.Dispose` 只负责最终清空，不能替代订阅者的生命周期管理。由架构测试 `Sources_UnsubscribeEveryGlobalEventTheySubscribe` 执行：按文件比较 `AddListener<T>` 与 `RemoveListener<T>` 的事件类型集合。
- `ARCH-EVT-006`：对象内部或明确端口上的点对点回调允许使用 C# `event`，例如网络推送、叙事会话回调和 Component 变更通知；它们必须由持有者管理订阅关系，且禁止承担全局广播职责。
- `ARCH-EVT-007`：发布一个当前没有监听者的事件是合法的空操作，不构成"死代码"。

## 8. 网络边界约束

- `ARCH-NET-001`：`INetworkClient` 只允许公开连接、主动断连、重连、通用 Packet 请求关联、通用 Packet Push 和泵送等业务无关能力；禁止增加房间、POI、背包、抽卡、位置或其他具体游戏业务接口。
- `ARCH-NET-002`：`NetworkClient`、`NetworkSession` 和协议编解码器必须保持内部实现。公开的 `IByteTransport` 仅作为基础设施替换扩展点，Gameplay 业务不得直接依赖。
- `ARCH-NET-003`：网络职责按两层划分。`IServiceSystem` 只提供业务无关的会话与通用请求通道（`EnterWorldAsync` / `RequestAsync` / `PushReceived`）；每个领域的组包、解包与 Push 分类由该领域自己的 `I*Gateway` 承担。底层客户端只允许由 NetworkKit 自身声明、由 `ServiceSystem` 独占持有（架构测试 `Sources_DoNotUseNetworkClientOutsideServiceSystem`）；`RequestAsync` 只允许由 `*Gateway.cs` 调用（架构测试 `Sources_DoNotUseRequestChannelOutsideGateways`）。
- `ARCH-NET-003a`：Gateway 必须与它服务的领域同目录（例如 `Gameplay/PoiSystem/PoiGateway.cs`）。新增一个领域网络请求应当只改动该领域自己的文件。
- `ARCH-NET-003b`：Gateway 禁止持有连接状态、重试策略或并发原语。会话、并发与断线语义由 `ServiceSystem` 独占；Gateway 只做翻译。
- `ARCH-NET-004`：注册顺序必须为 `ServiceSystem` → 各 `*Gateway` → 领域消费者，释放按其逆序，保证单局只有一个网络客户端和一条 Push 泵送链，且退订时通道仍然可用。由架构测试 `SystemRegistration_OrdersChannelBeforeGatewaysBeforeConsumers` 执行；消费关系由源码引用推导，不在测试中写死。
- `ARCH-NET-005`：连接、断连、重连和 `PumpEvents` 属于 NetworkKit 生命周期能力，禁止出现在 `IServiceSystem`。
- `ARCH-NET-006`：所有服务器 Push 必须先以通用 Packet 进入 ServiceSystem；ServiceSystem 必须在自身 `OnUpdate` 中调用 `PumpEvents`，并把 Packet 原样转发到 `PushReceived`。按业务类型分类是各领域 Gateway 的职责，分类结果以强类型事件在 Unity 主线程分发。
- `ARCH-NET-007`：进入世界的首次连接探测由 `ServiceSystem.EnterWorldAsync` 内部执行且本局最多访问服务器一次；失败后禁止其他 System 绕过服务层重试。
- `ARCH-NET-008`：ServiceSystem 与 Gateway 都不得持有或修改 POI、背包、任务等领域状态；请求结果必须由对应 System 解释和缓存。
- `ARCH-NET-009`：`IServiceSystem` 与全部 `I*Gateway` 的异步接口必须接受 `CancellationToken`；调用方必须传入自身生命周期令牌。
- `ARCH-NET-010`：NetworkKit 必须通过业务无关的断线通知报告接收或发送失败；ServiceSystem 必须将其转换为世界不可用状态。当前不自动重连。
- `ARCH-NET-011`：ServiceSystem 释放时必须先取消生命周期令牌，并等待活动异步调用退出后再释放客户端和同步原语。

## 9. Entity 约束

- `ARCH-ENT-001`：`Entity` 位于框架层，禁止认识任何具体 Component 类型。调度与清理必须经由 `IControlStateProvider`、`IListenerHost` 等组件端口。
- `ARCH-ENT-002`：驱动实体生命周期跃迁（绑定编号、标记回收、立即释放）必须通过显式接口 `IEntityLifecycleController`。禁止把这类特权操作放宽为普通公开方法。
- `ARCH-ENT-003`：`OrderTag` 只允许描述与玩法领域无关的通用相位。"背包""任务""编队"之类的领域概念必须通过同一相位内的注册顺序表达，禁止在框架层新增领域枚举值。
- `ARCH-ENT-004`：`Entity.bindGo` 对外只读。实体子类可在构造阶段绑定既有场景对象，运行期的接管与解绑一律经由 `Entity.BindGameObject`。
- `ARCH-ENT-005`：ELC 适用于**由运行时创建并拥有表现对象、具备逐帧行为**的对象。场景摆放、数量固定、状态由服务器权威、且没有逐帧逻辑的对象（如 POI）不得使用 ELC——为它们建立实体只会带来生命周期、组件组合与帧驱动三重净负担，应直接以 MonoBehaviour 承载配置、状态与交互入口。

## 10. 当前允许的主要依赖

| 调用方 | 允许依赖 | 用途 |
| --- | --- | --- |
| Gameplay/UI 任意模块 | `IAssetKit`、`IConfigKit`、`IEventKit`、`IUIKit`、`IGameplayKit` | 基础模块入口 |
| `ConfigKit` | `IAssetKit`、`Prometheus.Config.Generated` | 按表名加载 `.bytes` 并构造唯一 `Tables` |
| `PoiSystem` | `IEntitySystem`、`IServiceSystem`、`IPoiGateway`、`ITeamSystem` | POI Entity 生命周期、会话可用性、世界网络请求、当前上场成员位置 |
| `WorldMapSystem` | 无 | 加载静态地图定义并提供世界坐标换算，不持有任何 POI 或界面状态 |
| `BagSystem` | `IBagGateway` | 库存请求与本地快照 |
| `PoiGateway` / `BagGateway` | `IServiceSystem` | 通用请求通道与 Push 流 |
| `NarrativeSystem` | `ICameraSystem`（规划中） | 叙事演出期间的镜头租约 |
| `NpcSystem` | 无 | 只发布 `InteractionRequested`，由叙事适配器接管演出 |
| `TeamSystem` | `IInputSystem` | 上场成员输入切换 |
| `CameraSystem` | `IEntitySystem` | 跟随目标解析 |
| `CombatAudioPresentationSystem` | `IEffectSystem` | 战斗效果信号到音频表现 |
| UI 面板 | 对应 `I*System` 和 `Core.Event` | 展示状态、提交命令、监听全局事实 |

未列出的跨 System 依赖必须先确认职责归属并更新本文；禁止为了复用一个方法临时扩大 System 接口。

## 11. 生命周期与数据约束

- `ARCH-LIFE-001`：System 初始化顺序必须满足依赖方晚于被依赖方；释放必须逆序执行。Entity 必须先于依赖其数据的其他 System 完成释放。世界相位同理：`OnWorldEnterAsync` 正序、`OnWorldExit` 逆序。
- `ARCH-LIFE-002`（相位隔离）：`XSystem.AfterNewAsync` 并行执行，其中**禁止调用其他 System**——此刻别的系统可能还没加载完自己的配置。需要协作的初始化一律放到同步的 `AfterNew` 及之后。
- `ARCH-LIFE-003`（三段生命周期）：运行时状态必须落在明确的一段上。
  - **App**：`Core` 与全部 Kit，进程启动时建立、退出时释放。
  - **Session**：全部玩法 System，登录后建立、登出时释放，**跨越多个世界**。跨场景存活的状态（背包、任务进度、小队编成、网络会话）属于这一段。
  - **World**：场景本身与场景内的 Entity / POI / NPC，每次进出场景建立与拆除。**任何绑定场景 GameObject 的状态都必须落在这一段**，否则场景以 `LoadSceneMode.Single` 卸载时会留下指向已销毁对象的引用。
- `ARCH-WORLD-001`（世界目录）：世界之间的跳转必须按 `WorldDefinition.WorldId` 引用，**禁止**按场景地址跳转——场景地址是实现细节。压栈行为由 `WorldKind` 推导，禁止为它单独配置开关。世界目录必须有且只有一个 `MainWorld`：它是世界栈的栈底，必须唯一确定。
- `ARCH-WORLD-002`（`WorldContext` 的边界）：`WorldContext` 只允许携带**由流程解析、System 自己查不到**的事实（当前是世界标识、场景与出生位姿）。其余世界级配置由关心它的 System 按 `WorldId` 自行加载，禁止塞进上下文——否则该类型会随场景种类不断膨胀，退化成集中式启动参数对象。
- `ARCH-BOOT-001`（启动层自持）：开屏与热更界面**禁止**使用 `UIPanel` 或任何经 `Core.Asset` 加载的资源——它们运行在资源包就绪之前。这一层只允许使用随包体直出的东西：启动场景内的对象、`Resources` 资源、代码创建的对象、系统字体。登录界面不在此列，它在资源包就绪之后，是正常的 `UIPanel`。
- `ARCH-BOOT-002`（热更是阶段而非步骤）：热更下载是资源包初始化**内部**的一段（清单就绪之后、可加载之前），因此界面只能订阅 `IAssetKit.BootProgressChanged` 显示进度，**禁止**自行驱动下载流程或另起一套进度来源。
- `ARCH-VAR-001`（变量所有权）：跨系统共享的变量必须存在唯一的 `VariableStore` 里，按路径**根段**路由到唯一的 `IVariableNamespace` 所有者。禁止为同一批变量建立第二份存储，也禁止让两个存储互相注册为投影——互投影在图上是一个环，需要重入守卫才不会无限递归。写权限由所有者的门面强制：一个命名空间只允许写自己的根段。存档按所有者切片导出，因此共享存储不影响各系统各存各的档。
- `ARCH-CONFIG-001`（配表加载时机）：配表必须在**资源包就绪之后、玩法会话建立之前**由 `Core.Config.Load()` 一次性加载，当前落在 `GameFlow.RunHotUpdateAsync` 的 `core.AfterNew()` 之后。它属于 App 段：跨越登录与全部世界，不随会话重建。System 禁止各自加载表数据，也禁止在 `AfterNewAsync` 阶段依赖表已就绪之外的任何时序假设。
- `ARCH-LIFE-004`（释放自足）：`XSystem.Dispose` **禁止**依赖 `OnWorldExit` 先执行过。`OnWorldExit` 的前提是「场景仍然存活」，只在世界切换时成立；进程结束时 Unity 已在销毁场景对象，`GameplayKit.DestroySession` 因此不驱动世界退出相位。`OnWorldExit` 负责**归还**，`Dispose` 负责**销毁**，两者互不替代。
- `ARCH-DATA-001`：UI 不得成为权威玩法数据源；UI 只读取接口暴露的只读状态并提交命令。
- `ARCH-DATA-002`：配置 SO、只读 DTO、事件载荷和生命周期句柄可以跨接口传递，但不得借此泄漏可替换服务实现。

## 12. 工程与文档约束

- `ARCH-DOC-001`：修改成型系统的链路、职责、所有权或依赖方向时，必须同步更新对应中文 Markdown 文档和本 ArchSpec。
- `ARCH-UNITY-001`：Unity 资产新增、移动或删除时必须同步维护 `.meta`，移动时必须保留原 GUID。
- `ARCH-UNITY-002`：跨装配移动使用 `[SerializeReference]` 的类型时，必须同步迁移已有资产中记录的程序集名（`asm: <Assembly>`）。该数据不由 GUID 保护，遗漏会导致多态引用整体丢失。
- `ARCH-EDITOR-001`：配表相关编辑器代码、配置资产、JSON 工具和文档必须位于项目级 `Assets/Editor`；系统专属 Editor 代码可保留在系统目录的 `Editor` 子目录。
- `ARCH-EDITOR-002`：配表工具菜单必须位于 `Prometheus/...`，禁止新增平行根菜单。
- `ARCH-CODE-001`：新增代码必须带有解释意图、关键参数和边界情况的清晰注释；代码优先保持紧凑，但不得以单行为由牺牲可读性。
- `ARCH-NAME-001`：`Gameplay` 下的模块目录命名遵循两条：含 System 的目录名必须与该 System 的类名**完全相同**（例如 `CombatAudioPresentationSystem/`）；只包含库、Logic、Component 或配置而不含 System 的目录不得使用 `System` 后缀（例如 `Ai/`、`Animation/`、`Audio/`）。被多个模块共用的库必须独立成目录，不得寄居在某个 System 的目录下。
- `ARCH-NAME-002`：`Assets/Prometheus/UI` 下每个界面独占一个以面板类型命名的目录，界面的业务脚本、生成基类和该界面专属 Mono 全部放在其中；跨界面共用的表现脚本单独成目录（如 `WorldMap`、`FloatingDmgText`）。UIKit 代码生成器按面板名输出到对应目录。

## 13. 禁止示例

```csharp
// 禁止：按具体实现查询 System。
PoiSystem poi = Core.Gameplay.GetSystem<PoiSystem>();

// 禁止：ServiceSystem 之外的 Gameplay 代码直接创建底层网络客户端。
INetworkClient client = NetworkClientFactory.Create();

// 禁止：创建第二条全局静态事件通道。
public static event Action<PoiOpenedEvent> PoiOpened;

// 禁止：在框架层新增集中式事件键。
public enum Event { PoiOpened, EntityDied }

// 禁止：Kit 构造函数写入全局静态入口。
public UIKit() { Core.UI = this; }

// 禁止：Gateway 之外的代码直接组装业务 Packet。
Packet response = await Core.Gameplay.GetSystem<IServiceSystem>()
    .RequestAsync(new Packet { GetItems = new GetItemsRequest() });

// 禁止：框架层引用玩法层具体类型。
// Framework/GameplayKit/GameplayKit.cs
using Xuan.Prometheus.World;
```

## 14. 例外规则

第三方代码、生成代码、测试友元访问和独立工具不自动成为生产架构先例。确需违反本规范时，必须先在评审中记录原因、影响范围、退出条件和负责人，并在本文件增加带编号的限时例外；没有文档记录的实现视为违规。

## 15. 验证

```powershell
# 架构约束（含分层复核、可见性、唯一性、源码级禁令）
# Unity Test Runner → EditMode → Prometheus.Architecture.EditorTests

# 全量编译
dotnet build prometheus.slnx --no-restore

git diff --check
```

分层依赖不再需要 `rg` 巡检：`Prometheus.Framework` 无法引用 `Prometheus.Gameplay`，因为 asmdef 里没有这条引用，编译器直接拒绝。

System 注册顺序同样不再需要测试巡检：原先用正则扫源码推导依赖关系再校验注册顺序的 `SystemRegistration_OrdersChannelBeforeGatewaysBeforeConsumers` 已随 `ARCH-SYS-005` 的反转删除——构造注入让编译器接管了这件事。

## 16. 当前非目标与已知待决项

带**触发条件**的结构性待决项记录在 [LoadBearingSeams](LoadBearingSeams.md)：本节列的是「当前不做」的结论，那份台账列的是「什么时候必须改」。没有触发条件的重构意向不写进任何一份。

- 当前不按每个 System 拆分独立 asmdef。四层装配已经把最重要的方向性约束交给了编译器；更细的拆分收益递减。
- 网络已按领域拆分为 `IPoiGateway` / `IBagGateway`，共享 `IServiceSystem` 提供的同一条连接。新增领域时应新增对应 Gateway，而不是扩大既有接口。
- `ToolKit` 下的 `MonoSingleton`、UI 辅助类和纯工具不是正式 Kit，不得注册为 `IKitContract` 实现。它们共用 `Kit` 后缀会持续制造误解，建议后续改名。
