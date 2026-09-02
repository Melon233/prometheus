# Prometheus 架构硬约束（ArchSpec）

> 状态：生效
> 适用范围：`Assets/Prometheus` 下的运行时 Framework、Gameplay、UI 代码及其组合根；第三方插件、生成代码和独立渲染插件边界不在本文重构范围内。
> 规范词：**必须**表示不可绕过的要求，**禁止**表示不得新增或依赖的行为，**允许**表示满足所列边界时可采用的实现。

## 1. 架构角色

| 角色 | 当前职责 | 所有者 |
| --- | --- | --- |
| `Core` | 创建、注册、更新和逆序释放基础 Kit，提供唯一跨模块入口 | `Entry` |
| Kit | 提供资源、事件、UI、玩法容器等跨模块基础能力 | `Core` |
| System | 管理单局内一个明确玩法领域的状态和行为 | `GameplayKit` |
| `ServiceSystem` | 为其他 System 提供唯一网络请求和 Push 入口，不保存领域状态 | `GameplayKit` |
| Entity / Logic / Component | 表达运行时对象身份、行为和数据组合 | `EntitySystem` 管理 Entity 生命周期 |
| Event | 描述已经发生的全局玩法事实 | `IEventKit` |
| UI | 读取 System 接口并把用户命令转交给玩法层，不拥有权威玩法状态 | `IUIKit` 与对应面板 |

## 2. Kit 约束

- `ARCH-KIT-001`：可被 `Core` 注册的 Kit 接口必须继承 `IKitContract`。
- `ARCH-KIT-002`：正式 Kit 的具体实现必须保持 `internal sealed`，业务代码禁止实例化、向下转换或公开返回具体实现。
- `ARCH-KIT-003`：业务代码必须通过 `Core.Asset`、`Core.Event`、`Core.UI`、`Core.Gameplay` 等接口入口使用正式 Kit；`Core.GetKit<T>()` 仅供 Core 生命周期管理和诊断。
- `ARCH-KIT-004`：Kit 的创建、初始化、更新和释放顺序只允许由 `Core` 控制；释放顺序必须与注册顺序相反。

## 3. System 约束

- `ARCH-SYS-001`：可被 `GameplayKit` 注册的 System 接口必须继承 `ISystemContract`。
- `ARCH-SYS-002`：System 具体实现必须保持 `internal sealed`；外部只允许持有 `I*System`。
- `ARCH-SYS-003`：System 必须以接口作为注册键；禁止 `GetSystem<ConcreteSystem>()`、`TryGetSystem<ConcreteSystem>()` 和公开具体实现类型。
- `ARCH-SYS-004`：跨 System 调用必须依赖对方接口，禁止读取对方私有缓存、网络客户端、会话或子服务。
- `ARCH-SYS-005`：新增 System 必须先定义最小接口，再在 `GameplayKit.RegisterGameplaySystems` 中完成创建和注册；`GameplayKit` 负责逆序释放。
- `ARCH-SYS-006`：Entity 的注册、查询、监听和回收必须经 `IEntitySystem`；其他 System 禁止维护第二套 Entity 生命周期容器。
- `ARCH-SYS-007`：公共 System 之间禁止构造注入或长期保存另一 System 实例；必须在使用点通过 `Core.Gameplay.GetSystem<IContract>()` 或 `TryGetSystem<IContract>()` 获取接口，构造参数只用于该 System 独占的内部实现与配置。

## 4. 事件约束

- `ARCH-EVT-001`：跨 System、Entity 与 UI 广播的全局玩法事实必须通过唯一的 `IEventKit`，发布和订阅统一使用 `Core.Event`。
- `ARCH-EVT-002`：禁止新增静态事件总线、全局 `EventHandler<T>` 聚合器或其他与 `IEventKit` 平行的全局事件通道。
- `ARCH-EVT-003`：全局事件载荷必须实现 `IEvent`，对外只提供只读属性，并在构造时形成完整快照；禁止发布后修改载荷。
- `ARCH-EVT-004`：每个 `Event` 枚举值必须只对应一种载荷类型；发布者和订阅者必须使用相同的枚举值与泛型参数组合。
- `ARCH-EVT-005`：订阅者必须在自身释放或失活边界对称退订。`EventKit.Dispose` 只负责最终清空，不能替代订阅者的生命周期管理。
- `ARCH-EVT-006`：对象内部或明确端口上的点对点回调允许使用 C# `event`，例如网络推送、Film 会话回调和 Component 变更通知；它们必须由持有者管理订阅关系，且禁止承担全局广播职责。

## 5. 网络边界约束

- `ARCH-NET-001`：`INetworkClient` 只允许公开连接、主动断连、重连、通用 Packet 请求关联、通用 Packet Push 和泵送等业务无关能力；禁止增加房间、POI、背包、抽卡、位置或其他具体游戏业务接口。
- `ARCH-NET-002`：NetworkKit 面向 Gameplay 只允许由 `ServiceSystem` 持有 `INetworkClient` 和调用 `NetworkClientFactory`；`NetworkClient`、`NetworkSession` 和协议编解码器必须保持内部实现。公开的 `IByteTransport` 仅作为基础设施替换扩展点，Gameplay 业务不得直接依赖。
- `ARCH-NET-003`：所有 Gameplay 网络业务必须通过 `IServiceSystem`；ServiceSystem 负责业务 Packet 的组装、响应解析和 Push 分类，其他 System 禁止创建、持有或公开底层客户端、会话、Transport 或协议编解码器。
- `ARCH-NET-004`：ServiceSystem 必须早于所有网络消费者注册，并在消费者之后释放，保证单局只有一个网络客户端和一条 Push 泵送链。
- `ARCH-NET-005`：连接、断连、重连和 `PumpEvents` 属于 NetworkKit 生命周期能力，禁止出现在 `IServiceSystem`；ServiceSystem 只能通过 `EnterWorldAsync` 等纯游戏业务接口向其他 System 暴露能力。
- `ARCH-NET-006`：所有服务器 Push 必须先以通用 Packet 进入 ServiceSystem；ServiceSystem 必须在自身 `OnUpdate` 中调用 `PumpEvents`、按业务类型分类，再通过接口事件在 Unity 主线程分发。
- `ARCH-NET-007`：进入世界的首次连接探测由 `ServiceSystem.EnterWorldAsync` 内部执行且本局最多访问服务器一次；失败后禁止其他 System 绕过服务层重试。
- `ARCH-NET-008`：ServiceSystem 不得持有或修改 POI、背包、任务等领域状态；请求结果必须由对应 System 解释和缓存。
- `ARCH-NET-009`：`IServiceSystem` 的全部异步业务接口必须接受 `CancellationToken`；调用方必须传入自身生命周期令牌，并在释放后禁止异步 continuation 修改集合、Unity 对象或可用状态。
- `ARCH-NET-010`：NetworkKit 必须通过业务无关的断线通知报告接收或发送失败；ServiceSystem 必须将其转换为世界不可用状态。当前不自动重连，断线后本局禁止继续隐式请求。
- `ARCH-NET-011`：ServiceSystem 释放时必须先取消生命周期令牌，并等待活动异步调用退出后再释放客户端和同步原语；禁止让等待锁或网络响应的 continuation 访问已释放对象。

当前网络依赖方向：

```text
GameplayKit
└─ ServiceSystem（业务组包/解包，内部持有唯一 INetworkClient）
   ├─ Core.Gameplay.GetSystem<IServiceSystem>() ◄─ WorldSystem
   └─ Core.Gameplay.GetSystem<IServiceSystem>() ◄─ BagSystem
```

## 6. 当前允许的主要依赖

| 调用方 | 允许依赖 | 用途 |
| --- | --- | --- |
| Gameplay/UI 任意模块 | `IAssetKit`、`IEventKit`、`IUIKit`、`IGameplayKit` | 基础模块入口 |
| `WorldSystem` | `IEntitySystem`、`IServiceSystem` | POI Entity 生命周期与世界网络请求 |
| `BagSystem` | `IServiceSystem` | 库存请求与本地快照 |
| `FilmSystem` | `IInputSystem`、`ICameraSystem` | 演出期间输入和镜头租约 |
| `NpcSystem` | `IFilmSystem` | NPC 交互演出协调 |
| `TeamSystem` | `IInputSystem` | 上场成员输入切换 |
| `CombatAudioPresentationSystem` | `IEffectSystem` | 战斗效果信号到音频表现 |
| UI 面板 | 对应 `I*System` 和 `Core.Event` | 展示状态、提交命令、监听全局事实 |

未列出的跨 System 依赖必须先确认职责归属并更新本文；禁止为了复用一个方法临时扩大 System 接口。

## 7. 生命周期与数据约束

- `ARCH-LIFE-001`：`Entry` 是 Player Build 唯一直接入口，`Core` 是 Kit 唯一组合根，`GameplayKit` 是 System 唯一组合根；System 自有基础设施由该 System 负责完整释放。
- `ARCH-LIFE-002`：System 初始化顺序必须满足依赖方晚于被依赖方；释放必须逆序执行。Entity 必须先于依赖其数据的其他 System 完成释放。
- `ARCH-DATA-001`：UI 不得成为权威玩法数据源；UI 只读取接口暴露的只读状态并提交命令。
- `ARCH-DATA-002`：配置 SO、只读 DTO、事件载荷和生命周期句柄可以跨接口传递，但不得借此泄漏可替换服务实现。

## 8. 工程与文档约束

- `ARCH-DOC-001`：修改成型系统的链路、职责、所有权或依赖方向时，必须同步更新对应中文 Markdown 文档和本 ArchSpec。
- `ARCH-UNITY-001`：Unity 资产新增、移动或删除时必须同步维护 `.meta`，移动时必须保留原 GUID。
- `ARCH-EDITOR-001`：配表相关编辑器代码、配置资产、JSON 工具和文档必须位于项目级 `Assets/Editor`；系统专属 Editor 代码可保留在系统目录的 `Editor` 子目录。
- `ARCH-EDITOR-002`：配表工具菜单必须位于 `Prometheus/...`，禁止新增平行根菜单。
- `ARCH-CODE-001`：新增代码必须带有解释意图、关键参数和边界情况的清晰注释；代码优先保持紧凑，但不得以单行为由牺牲可读性。

## 9. 禁止示例

```csharp
// 禁止：按具体实现查询 System。
WorldSystem world = Core.Gameplay.GetSystem<WorldSystem>();

// 禁止：ServiceSystem 之外的 Gameplay System 直接创建底层网络客户端。
INetworkClient client = NetworkClientFactory.Create();

// 禁止：创建第二条全局静态事件总线。
public static event Action<PoiOpenedEvent> PoiOpened;
```

## 10. 例外规则

第三方代码、生成代码、测试友元访问和独立工具不自动成为生产架构先例。确需违反本规范时，必须先在评审中记录原因、影响范围、退出条件和负责人，并在本文件增加带编号的限时例外；没有文档记录的实现视为违规。

## 11. 静态验证

```powershell
rg -n 'GetSystem<[^I][^>]*System>|TryGetSystem<[^I][^>]*System>' Assets/Prometheus -g '*.cs'
rg -n 'StaticEventKit|IStaticEventKit|EventHandler<' Assets/Prometheus -g '*.cs'
rg -n 'INetworkClient|NetworkClientFactory' Assets/Prometheus/Gameplay Assets/Prometheus/UI -g '*.cs' | rg -v 'ServiceSystem[/\\]ServiceSystem.cs'
dotnet build Runtime.csproj --no-restore
dotnet build Prometheus.NetworkKit.csproj --no-restore
git diff --check
```

## 12. 当前非目标

- 公共 System 依赖统一通过 `Core.Gameplay.GetSystem<IContract>()` 或 `TryGetSystem<IContract>()` 获取；只有 System 独占的内部策略、运行时适配器和配置允许构造注入。
- 当前不按每个 System 拆分独立 asmdef；接口边界先由访问级别、注册约束和 ArchSpec 保证。
- `ToolKit` 下的 `MonoSingleton`、UI 辅助类和纯工具不是正式 Kit，不得注册为 `IKitContract` 实现。
- 当前不拆分多个领域网络 Gateway；所有游戏业务请求和业务 Push 先统一收口到 `IServiceSystem`，领域状态仍归对应 System，NetworkKit 只传输通用 Packet。
