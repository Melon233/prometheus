# Kit 与 System 接口边界设计

## 目标

运行时代码只通过稳定接口访问 Kit 和 System，具体实现仅由 `Core`、`GameplayKit` 等组合根创建并驱动。配置资产、领域事件、值对象和生命周期句柄是接口参数或返回数据，不因名称归属某个系统而机械增加接口。

## Kit 边界

`Core` 只允许注册和查询继承 `IKitContract` 的接口，并通过 `IAssetKit`、`IEventKit`、`IUIKit` 和 `IGameplayKit` 暴露正式入口。`AssetKit`、`EventKit`、`UIKit` 与 `GameplayKit` 均为 `internal` 实现，业务程序集不能实例化、向下转换或替换 `Core` 的正式实现。`IFsmKit` 已声明完整操作契约；对应实现仍属于程序集内部的预留基础设施，当前不进入正式 `Core` 注册链。

## System 边界

所有可由 `GameplayKit` 注册的系统接口都继承 `ISystemContract`。`GameplayKit.AddSystem<TContract>` 只在程序集内部开放，注册键必须是接口类型；`IGameplayKit.GetSystem<TContract>` 与 `TryGetSystem<TContract>` 只能查询接口契约，不能按实现类型定位系统。

| 公开契约 | 内部实现 | 主要职责 |
| --- | --- | --- |
| `IEntitySystem` | `EntitySystem` | Entity 注册、查询、监听与安全回收 |
| `IInputSystem` | `InputSystem` | 输入源、动作路由与控制租约 |
| `IEffectSystem` | `EffectSystem` | 效果运行时与默认配置库 |
| `ICombatAudioPresentationSystem` | `CombatAudioPresentationSystem` | 战斗伤害音频表现订阅 |
| `ICameraSystem` | `CameraSystem` | 玩法镜头与演出镜头租约 |
| `IFilmSystem` | `FilmSystem` | Timeline 演出与交互等待 |
| `INpcSystem` | `NpcSystem` | NPC 单会话交互协调 |
| `IQuestSystem` | `QuestSystem` | 任务配置、状态流转与快照 |
| `IServiceSystem` | `ServiceSystem` | 游戏业务请求、业务响应解析与业务 Push 主线程分发 |
| `IWorldSystem` | `WorldSystem` | POI、地图、AOI、玩家位置与世界状态同步 |
| `IBagSystem` | `BagSystem` | 背包缓存与修订通知 |
| `ITeamSystem` | `TeamSystem` | 固定容量小队与上场成员切换 |
| `IHudCommandSystem` | `HudCommandSystem` | HUD 命令到 UI 行为的转换 |

## 跨系统依赖规则

1. 业务代码通过 `Core.Gameplay.GetSystem<IContract>()` 或 `TryGetSystem(out IContract)` 获取公共 System；System 之间禁止构造注入或长期保存另一 System 实例，构造参数只用于当前 System 独占的内部策略、适配器和配置。
2. System 不公开其内部子服务、会话或传输对象；需要跨系统使用的动作应提升为所属系统接口的方法。
3. 公共适配器只能接收接口，例如 `QuestEventAdapters` 接收 `IQuestSystem`；禁止在公共方法签名中泄漏内部实现。
4. `FilmHandle`、`ControlLease`、`ListenHandle`、配置 SO、领域 DTO 和只读运行时数据可以作为接口返回值，它们表达一次操作或数据事实，不是可替换服务。
5. 新增 System 时必须先定义最小 `I*System : ISystemContract`，再由 `GameplayKit` 以该接口注册；实现类保持 `internal sealed`。

## 事件边界

跨 System、Entity 与 UI 的全局玩法事实只通过 `IEventKit` 和 `Core.Event` 发布。事件载荷实现 `IEvent`、构造后不可变，并与唯一 `Event` 枚举值绑定；订阅者在释放或失活时对称退订。对象内部或明确端口上的生命周期回调可以保留 C# `event`，但不得形成第二条全局总线。旧 `StaticEventKit` 与 POI 的静态 `EventHandler<T>` 通道已删除。

## 测试可见性

项目内测试程序集通过 `InternalsVisibleTo` 验证具体实现和生命周期细节，但注册与查询仍使用接口键。该友元关系只服务测试，不构成生产 API，也不能作为业务代码依赖具体实现的理由。

## NetworkKit 边界

底层网络只通过 `INetworkClient` 提供连接、断连、重连、通用 Packet 请求关联和通用 Push 能力，实例由 `NetworkClientFactory` 创建。`INetworkClient` 不包含房间、POI、背包、抽卡或位置等业务接口；`NetworkClient`、`NetworkSession`、分帧器和协议编解码器均为程序集内部实现，`IByteTransport` 作为可替换传输扩展点保持公开。

`GameplayKit` 在网络消费者之前创建并注册单局唯一 `ServiceSystem`。WorldSystem 与 BagSystem 不接收或保存 ServiceSystem，而是在业务调用点通过 `Core.Gameplay.GetSystem<IServiceSystem>()` 获取接口。只有 ServiceSystem 可以持有 `INetworkClient`、在业务流程内部管理连接、组装业务 Packet 和调用 `PumpEvents`；其他 System 只调用纯业务接口、解释结果并维护领域状态。GameplayKit 逆序释放 System，因此 ServiceSystem 在网络消费者之后关闭连接。

所有 `IServiceSystem` 异步业务接口都接受 `CancellationToken`。消费方传入自身生命周期令牌，释放时先取消未完成请求；ServiceSystem 同时维护系统级取消源和活动操作计数，最后一个异步调用退出后才释放客户端与同步原语。NetworkKit 的意外断线事件经 `PumpEvents` 回到主线程，ServiceSystem 将其转换为世界不可用状态，WorldSystem 随即停止坐标上传、区块拉取和交互。

项目级硬约束见 `Docs/ArchSpec.md`。
