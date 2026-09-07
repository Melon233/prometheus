# Prometheus 架构硬约束（ArchSpec）

> 状态：生效
> 适用范围：`Assets/Prometheus` 下的运行时 Framework、Gameplay、UI 代码及其组合根；第三方插件、生成代码和独立渲染插件边界不在本文重构范围内。
> 规范词：**必须**表示不可绕过的要求，**禁止**表示不得新增或依赖的行为，**允许**表示满足所列边界时可采用的实现。

## 0. 本规范如何被执行

**未被执行的规范只是注释。** 本文每一条约束都必须对应一种自动化执行手段，否则它就不该出现在这里：

| 执行手段 | 覆盖范围 |
| --- | --- |
| **asmdef 装配划分** | 分层依赖方向。编译器直接拒绝反向引用，无法绕过 |
| **`InternalsVisibleTo`** | 实现类型的可见性。只有唯一组合根和测试装配能看到 `internal` 实现 |
| **架构测试** `Assets/Prometheus/Tests/Architecture/ArchitectureConstraintTests.cs` | 编译器表达不了的部分：实现类型的可见性与唯一性、若干源码级禁令 |
| **显式接口实现** | 特权操作（如驱动实体生命周期）必须写出显式转换，无法被随手调用 |

新增一条约束时，必须同时给出它的执行手段。

## 1. 分层

运行时代码划分为四个装配，依赖方向由 asmdef 强制：

```text
Runtime（Bootstrap，唯一组合根）
  └─ Prometheus.UI
       └─ Prometheus.Gameplay
            └─ Prometheus.Framework
```

- `ARCH-LAYER-001`：下层装配禁止引用上层装配。由 asmdef 强制，并由架构测试 `Layer_DoesNotReferenceUpperLayers` 复核（防止有人放宽 asmdef）。
- `ARCH-LAYER-002`：`Prometheus.Framework` 禁止出现任何玩法领域概念。框架需要与玩法协作时，必须声明端口由玩法层实现，而不是引用玩法类型。
- `ARCH-LAYER-003`：`Runtime`（`Assets/Prometheus/Bootstrap`）是唯一被允许认识具体实现类型的装配。三个下层装配各自通过 `AssemblyInfo.cs` 中的 `InternalsVisibleTo("Runtime")` 只向它开放实现。
- `ARCH-LAYER-004`：`Prometheus.Rendering.*` 与 `Prometheus.NetworkKit` 是独立边界装配，不参与上述四层顺序。

## 2. 架构角色

| 角色 | 当前职责 | 所有者 |
| --- | --- | --- |
| `Core` | 创建、注册、更新和逆序释放基础 Kit，提供唯一跨模块入口 | `Entry` |
| Kit | 提供资源、事件、UI、玩法容器等跨模块基础能力 | `Core` |
| `IGameplaySystemInstaller` | 回答"这一局由哪些 System 组成、初始世界里有什么" | `Entry` |
| System | 管理单局内一个明确玩法领域的状态和行为 | `GameplayKit` |
| `ServiceSystem` | 单局唯一网络会话与业务无关的通用请求通道；不认识任何具体业务 | `GameplayKit` |
| `*Gateway` | 某一领域的网络适配器：组包、解包、Push 分类；不保存领域状态 | `GameplayKit` |
| Entity / Logic / Component | 表达运行时对象身份、行为和数据组合 | `EntitySystem` 管理 Entity 生命周期 |
| Event | 描述已经发生的全局玩法事实 | `IEventKit` |
| UI | 读取 System 接口并把用户命令转交给玩法层，不拥有权威玩法状态 | `IUIKit` 与对应面板 |

## 3. Kit 约束

- `ARCH-KIT-001`：可被 `Core` 注册的 Kit 接口必须继承 `IKitContract`。
- `ARCH-KIT-002`：正式 Kit 的具体实现必须保持 `internal sealed`；由架构测试 `KitImplementations_AreInternalSealed` 执行。
- `ARCH-KIT-003`：业务代码必须通过 `Core.Asset`、`Core.Event`、`Core.UI`、`Core.Gameplay` 使用正式 Kit；`Core.GetKit<T>()` 仅供 Core 生命周期管理和诊断。由架构测试 `Sources_DoNotResolveKitsByGetKit` 执行。
- `ARCH-KIT-004`：Kit 的创建、初始化、更新和释放顺序只允许由 `Core` 控制；释放顺序必须与注册顺序相反。
- `ARCH-KIT-005`：Kit 的构造函数禁止产生全局副作用。四个静态入口只允许在 `Core.RegisterKit`（`Core.PublishStaticEntry`）中写入、在 `Core.Dispose` 中清空。`new` 一个 Kit 与把它接入当前 Core 必须是两件可以分别发生的事。

## 4. 组合根约束

- `ARCH-COMP-001`：`Entry` 是 Player Build 唯一直接入口；`Core` 是 Kit 唯一组合根；`IGameplaySystemInstaller` 的实现是 System 与初始世界内容的唯一组合根。
- `ARCH-COMP-002`：`Core.Configure` 只接受一个安装器。禁止让 `Core` 或 `GameplayKit` 认识任何玩法参数类型。
- `ARCH-COMP-003`：新增 System 必须先定义最小接口，再在 `PrometheusSystemInstaller.RegisterSystems` 中完成创建和注册；注册顺序即初始化顺序，`GameplayKit` 负责逆序释放。
- `ARCH-COMP-004`：启动链路禁止传递参数。资源包名由 `AssetKit.DefaultPackageName` 固定；每个资源地址由**使用它的系统**以常量持有（`EffectSystem.DefaultLibraryAddress`、`TeamSystem.MemberAddresses`、`EntitySystem` 的敌人地址、组合根的玩法场景地址）；跨场景运行时对象统一挂在 `PersistentRoot.Shared` 下，不再逐层传递根节点。禁止在 `Entry` 或任何 MonoBehaviour 上序列化玩法参数，也禁止重新引入集中式启动配置对象。
- `ARCH-COMP-005`：手工搭建 Core 的测试必须提供与正式链路一致的入口集合（至少 `Core.Asset`、`Core.Event`、`Core.Gameplay`）。生产代码不为"入口缺失"增加空值兜底——那是时序错误，不是可恢复状态。

## 5. System 约束

- `ARCH-SYS-001`：可被 `GameplayKit` 注册的 System 接口必须继承 `ISystemContract`。
- `ARCH-SYS-002`：System 具体实现必须保持 `internal sealed`；外部只允许持有 `I*System`。由架构测试 `SystemImplementations_AreInternalSealed` 执行。
- `ARCH-SYS-003`：System 必须以接口作为注册键；禁止 `GetSystem<ConcreteSystem>()`。由架构测试 `Sources_DoNotQuerySystemsByConcreteType` 执行。
- `ARCH-SYS-004`：跨 System 调用必须依赖对方接口，禁止读取对方私有缓存、网络客户端、会话或子服务。
- `ARCH-SYS-005`：公共 System 之间禁止构造注入或长期保存另一 System 实例；必须在使用点通过 `Core.Gameplay.GetSystem<IContract>()` 或 `TryGetSystem<IContract>()` 获取接口。构造参数只用于该 System 独占的内部实现与配置。
- `ARCH-SYS-006`：Entity 的注册、查询、监听和回收必须经 `IEntitySystem`；单局至多存在一个 `IEntityDriver` 实现。由架构测试 `EntityDriver_IsUnique` 执行。
- `ARCH-SYS-007`：`IGameplayKit` 禁止公开任何玩法领域概念。"当前上场角色"之类的状态由对应 System 自己发布（`ITeamSystem.ActiveMember`），不由框架层容器代理。
- `ARCH-SYS-008`：System 允许由 UI 层拥有。它仍由组合根统一注册，并只能向下依赖 Gameplay 与 Framework。当前 UI 层没有注册任何 System。

## 6. 事件约束

- `ARCH-EVT-001`：跨 System、Entity 与 UI 广播的全局玩法事实必须通过唯一的 `IEventKit`，发布和订阅统一使用 `Core.Event`。
- `ARCH-EVT-002`：禁止新增静态事件总线或其他与 `IEventKit` 平行的全局事件通道。由架构测试 `Sources_DoNotDeclareStaticEventBuses` 执行。
- `ARCH-EVT-003`：全局事件载荷必须实现 `IEvent`，对外只提供只读属性，并在构造时形成完整快照；禁止发布后修改载荷。
- `ARCH-EVT-004`：事件载荷的**具体类型**是唯一路由键。载荷类型必须定义在它所属的领域目录中，禁止在框架层新增集中式事件枚举或事件注册表。新增一个全局事件不应当需要修改 EventKit。
- `ARCH-EVT-005`：订阅者必须在自身释放或失活边界，用同一个委托实例对称退订。`EventKit.Dispose` 只负责最终清空，不能替代订阅者的生命周期管理。由架构测试 `Sources_UnsubscribeEveryGlobalEventTheySubscribe` 执行：按文件比较 `AddListener<T>` 与 `RemoveListener<T>` 的事件类型集合。
- `ARCH-EVT-006`：对象内部或明确端口上的点对点回调允许使用 C# `event`，例如网络推送、叙事会话回调和 Component 变更通知；它们必须由持有者管理订阅关系，且禁止承担全局广播职责。
- `ARCH-EVT-007`：发布一个当前没有监听者的事件是合法的空操作，不构成"死代码"。

## 7. 网络边界约束

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

## 8. Entity 约束

- `ARCH-ENT-001`：`Entity` 位于框架层，禁止认识任何具体 Component 类型。调度与清理必须经由 `IControlStateProvider`、`IListenerHost` 等组件端口。
- `ARCH-ENT-002`：驱动实体生命周期跃迁（绑定编号、标记回收、立即释放）必须通过显式接口 `IEntityLifecycleController`。禁止把这类特权操作放宽为普通公开方法。
- `ARCH-ENT-003`：`OrderTag` 只允许描述与玩法领域无关的通用相位。"背包""任务""编队"之类的领域概念必须通过同一相位内的注册顺序表达，禁止在框架层新增领域枚举值。
- `ARCH-ENT-004`：`Entity.bindGo` 对外只读。实体子类可在构造阶段绑定既有场景对象，运行期的接管与解绑一律经由 `Entity.BindGameObject`。
- `ARCH-ENT-005`：ELC 适用于**由运行时创建并拥有表现对象、具备逐帧行为**的对象。场景摆放、数量固定、状态由服务器权威、且没有逐帧逻辑的对象（如 POI）不得使用 ELC——为它们建立实体只会带来生命周期、组件组合与帧驱动三重净负担，应直接以 MonoBehaviour 承载配置、状态与交互入口。

## 9. 当前允许的主要依赖

| 调用方 | 允许依赖 | 用途 |
| --- | --- | --- |
| Gameplay/UI 任意模块 | `IAssetKit`、`IEventKit`、`IUIKit`、`IGameplayKit` | 基础模块入口 |
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

## 10. 生命周期与数据约束

- `ARCH-LIFE-001`：System 初始化顺序必须满足依赖方晚于被依赖方；释放必须逆序执行。Entity 必须先于依赖其数据的其他 System 完成释放。
- `ARCH-DATA-001`：UI 不得成为权威玩法数据源；UI 只读取接口暴露的只读状态并提交命令。
- `ARCH-DATA-002`：配置 SO、只读 DTO、事件载荷和生命周期句柄可以跨接口传递，但不得借此泄漏可替换服务实现。

## 11. 工程与文档约束

- `ARCH-DOC-001`：修改成型系统的链路、职责、所有权或依赖方向时，必须同步更新对应中文 Markdown 文档和本 ArchSpec。
- `ARCH-UNITY-001`：Unity 资产新增、移动或删除时必须同步维护 `.meta`，移动时必须保留原 GUID。
- `ARCH-UNITY-002`：跨装配移动使用 `[SerializeReference]` 的类型时，必须同步迁移已有资产中记录的程序集名（`asm: <Assembly>`）。该数据不由 GUID 保护，遗漏会导致多态引用整体丢失。
- `ARCH-EDITOR-001`：配表相关编辑器代码、配置资产、JSON 工具和文档必须位于项目级 `Assets/Editor`；系统专属 Editor 代码可保留在系统目录的 `Editor` 子目录。
- `ARCH-EDITOR-002`：配表工具菜单必须位于 `Prometheus/...`，禁止新增平行根菜单。
- `ARCH-CODE-001`：新增代码必须带有解释意图、关键参数和边界情况的清晰注释；代码优先保持紧凑，但不得以单行为由牺牲可读性。
- `ARCH-NAME-001`：`Gameplay` 下的模块目录命名遵循两条：含 System 的目录名必须与该 System 的类名**完全相同**（例如 `CombatAudioPresentationSystem/`）；只包含库、Logic、Component 或配置而不含 System 的目录不得使用 `System` 后缀（例如 `Ai/`、`Animation/`、`Audio/`）。被多个模块共用的库必须独立成目录，不得寄居在某个 System 的目录下。
- `ARCH-NAME-002`：`Assets/Prometheus/UI` 下每个界面独占一个以面板类型命名的目录，界面的业务脚本、生成基类和该界面专属 Mono 全部放在其中；跨界面共用的表现脚本单独成目录（如 `WorldMap`、`FloatingDmgText`）。UIKit 代码生成器按面板名输出到对应目录。

## 12. 禁止示例

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

## 13. 例外规则

第三方代码、生成代码、测试友元访问和独立工具不自动成为生产架构先例。确需违反本规范时，必须先在评审中记录原因、影响范围、退出条件和负责人，并在本文件增加带编号的限时例外；没有文档记录的实现视为违规。

## 14. 验证

```powershell
# 架构约束（含分层复核、可见性、唯一性、源码级禁令）
# Unity Test Runner → EditMode → Prometheus.Architecture.EditorTests

# 全量编译
dotnet build prometheus.slnx --no-restore

git diff --check
```

分层依赖不再需要 `rg` 巡检：`Prometheus.Framework` 无法引用 `Prometheus.Gameplay`，因为 asmdef 里没有这条引用，编译器直接拒绝。

## 15. 当前非目标与已知待决项

- 当前不按每个 System 拆分独立 asmdef。四层装配已经把最重要的方向性约束交给了编译器；更细的拆分收益递减。
- 网络已按领域拆分为 `IPoiGateway` / `IBagGateway`，共享 `IServiceSystem` 提供的同一条连接。新增领域时应新增对应 Gateway，而不是扩大既有接口。
- `ToolKit` 下的 `MonoSingleton`、UI 辅助类和纯工具不是正式 Kit，不得注册为 `IKitContract` 实现。它们共用 `Kit` 后缀会持续制造误解，建议后续改名。
