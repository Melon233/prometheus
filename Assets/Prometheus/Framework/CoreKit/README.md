# CoreKit 与游戏入口

## 分层与组合根

运行时代码划分为四个装配，依赖方向由 asmdef 强制，编译器不允许反向引用：

```text
Runtime（Bootstrap，唯一组合根）
  └─ Prometheus.UI
       └─ Prometheus.Gameplay
            └─ Prometheus.Framework
```

`Prometheus.Framework` 只提供与玩法领域无关的能力：Core、Kit 生命周期、事件总线、资源、UI 框架、Entity/Logic/Component 基类和一组组合端口。它**不认识任何具体 System**。

"这一局由哪些 System 组成、初始世界里有什么"由玩法层的组合根 `Bootstrap/PrometheusSystemInstaller.cs` 回答。它是全工程唯一被允许认识具体实现类型的位置，因此三个下层装配都通过 `InternalsVisibleTo("Runtime")` 只向它开放 `internal` 实现。

## 正式入口

`Assets/Resources/Entry.unity` 是 Player Build 中唯一直接配置的入口场景。场景只包含常驻根对象 `GameRoot`，其 `Entry` 组件**没有任何序列化字段**。

## 启动链路不传递参数

启动过程不存在"启动参数"这个概念。每一项配置都归属到真正使用它的位置：

| 配置 | 归属 |
| --- | --- |
| YooAsset 资源包名 | `AssetKit.DefaultPackageName` |
| EffectLibrary 地址 | `EffectSystem` 的私有常量，由 `AfterNewAsync` 自行消费 |
| 固定小队成员地址 | `TeamSystem.MemberAddresses` |
| 敌人预制体地址 | `EntitySystem` 内部常量 |
| 玩法场景地址 | 组合根 `PrometheusSystemInstaller` 内部常量 |
| 跨场景运行时根节点 | `PersistentRoot.Shared`，由需要场景锚点的系统按需自取 |

这样做的判据是：**一项配置只被一处消费，就应当由那一处持有。** 把它们汇总成一个启动配置对象，只会让每个新增系统都去修改同一个中心类型，并让配置与使用点相隔数层传递。

`PersistentRoot` 同理：`CameraSystem`、`EntitySystem` 等各自需要场景锚点，从前由入口把 `Transform` 逐层传下去；现在它们直接向 `PersistentRoot.Shared` 索取，根节点的创建与 `DontDestroyOnLoad` 由单点持有，`Core.Dispose` 负责复位。

## 初始化顺序

启动是 `Entry.BootAsync` 中的一条线性链路，不经过开屏、热更或登录界面（2026-09-27 起，见 `Docs/Arch/GameFlow.md` 第 13 节）：

1. `Entry.Awake` 将 `GameRoot` 标记为跨场景保留。
2. `Entry.Start` 创建唯一 `Core`，不携带任何参数；`Core` 构造时依次注册 `AssetKit`、`ConfigKit`、`EventKit`、`UIKit`、`GameplayKit`，每个 Kit 在**注册**时由 `Core` 统一发布对应静态入口。`GameplayKit` 只是会话工厂，此时不产生任何玩法 System。
3. `Entry` 通过 `UniTask.WhenAll(core.CreateAfterNewTasks())` 并发等待全部 Kit 的 `AfterNewAsync`（资源包在这一步就绪），再调用 `Core.AfterNew` 执行同步初始化。
4. `Core.Config.Load()` 一次性加载全部配表（`ARCH-CONFIG-001`）。
5. `Core.Gameplay.CreateSessionAsync(new PrometheusSystemInstaller())`：安装器按依赖顺序构造并注册全部 System，GameplayKit 并行驱动各 System 的 `AfterNewAsync`（各自加载私有配置），再按注册顺序执行 `AfterNew`。
6. `Entry` 加载 `WorldCatalog`，按主世界定义加载场景，再以 `WorldContext`（世界标识、场景、出生位姿）调用 `Core.Gameplay.EnterWorldAsync`，各 System 按注册顺序建立世界内容（小队、营地敌人、POI 索引等）。
7. 主世界定义 `ShowsHud` 为真时，`Entry` 通过 `Core.UI` 打开 `HudPanel`。此后 `Entry.Update` 每帧驱动 `Core.OnUpdate`。

`Entry` 持有一个生命周期令牌，`OnDestroy` 中先取消它再释放 Core：尚未走完的启动步骤停在当前 await，不会在 Core 释放后继续访问 Kit。

`Core.Dispose` 按注册顺序逆序释放，因此最后注册的 `GameplayKit` 最先释放，`AssetKit` 最后释放其资源和场景句柄。

## 静态入口的唯一写入点

`Core` 是项目唯一的跨模块访问入口：

| 静态入口 | 模块契约 |
| --- | --- |
| `Core.Asset` | `IAssetKit` |
| `Core.Event` | `IEventKit` |
| `Core.UI` | `IUIKit` |
| `Core.Gameplay` | `IGameplayKit` |

这四个入口**只在 `Core.RegisterKit` 中写入**（`Core.PublishStaticEntry`），并只在 `Core.Dispose` 中清空。编辑器关闭域重载时，它们还会在进入播放模式时由引擎复位，见下一节。Kit 的构造函数不产生任何全局副作用：`new` 一个 Kit 与把它接入当前 Core 是两件可以分别发生的事，这使 Kit 可以被独立构造和测试。

`Core.GetKit<TKit>()` 只保留给 Core 自身管理和诊断查询，不作为业务模块之间的依赖传递方式（该禁令由架构测试 `Sources_DoNotResolveKitsByGetKit` 执行）。System 之间通过 `Core.Gameplay.GetSystem<IContract>()` 在使用点互相访问，不长期保存对方实例。

该约定依赖固定初始化顺序：`Core.Asset` 先于 UIKit 建立，`Core.Event` 和 `Core.UI` 先于 GameplayKit 建立，`Core.Gameplay` 先于所有 XSystem 与 Entity 初始化建立。因此正常业务链路不对这些入口增加空值兜底；生命周期外调用属于入口时序错误。手工搭建 Core 的测试必须提供同样完整的入口集合。

输入源等单局领域数据仍通过构造参数或方法参数显式传入。它们不是基础 Kit，也不属于本条跨模块访问规则。

## 关闭域重载时的静态状态

项目在 Unity 6.6 下关闭了进入播放模式时的域重载与场景重载，静态字段会从上一次播放**原样保留**到下一次。以前靠域重载顺带清掉的残留，现在必须显式复位，否则第二次进入播放就会出错。例如上一局 `Core.Dispose` 中途抛异常，`Core.Current` 就会残留，导致新的 `Core` 构造时抛出 "A Core instance is already active"。

复位统一使用 Unity 6.5 起提供的 `[AutoStaticsCleanup]`（命名空间 `Unity.Scripting.LifecycleManagement`）：

- 标注在**需要复位的静态成员**上，而不是整个类型上。
- 所在类型必须声明为 `partial`，因为清理代码由源生成器注入。
- 复位语义：非 readonly 字段和自动属性会重新求值内联初始化器，没有初始化器时回到默认值；readonly 集合保留原实例，只调用 `Clear()`；静态构造函数不会重跑。
- **成员类型实现了 `Dispose()` 时**，生成器会先对旧值调用 `Dispose()`，再重新求值初始化器。这种情况下成员**必须写出初始化器**，哪怕只是 `= null`。否则生成器会抛出 `RoslynSymbolException`，**整个程序集的清理代码都不会生成**，而 Unity 编译时不会报任何错。`Core.Current` 就属于这种情况：进出播放模式时，它指向的旧 Core 会被再 Dispose 一次，因此 `Core.Dispose` 必须保持幂等。

当前标注了该属性的静态状态：

| 成员 | 残留时的后果 |
| --- | --- |
| `Core.Current` / `Asset` / `Config` / `Event` / `UI` / `Gameplay` | 新 `Core` 构造时抛异常，或业务读到上一局已释放的 Kit |
| `PersistentRoot.shared` | 指向上一局已销毁的根节点 |
| `Bootstrap.Entry.current` | 新入口被误判为重复实例而销毁自身 |
| `MonoSingleton<T>._instance` / `_applicationIsQuitting` | 退出播放时 `OnApplicationQuit` 会把后者置为 true，下一局 `Ins` 就一直返回 null |
| `MapPanelViewState.Zoom` | 大地图缩放被带进下一局 |
| `FmodAudioRuntime.ReportedFailures` | 下一局不再输出同一条 FMOD 失败警告 |

`PrometheusRenderQualityController` 仍保留原先的 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 手动复位。它的复位必须早于同类的 `BeforeSceneLoad` 初始化，且复位时还要退订 `QualitySettings` 事件，属于带副作用的复位，不适合交给自动清理。

新增可变静态状态时，必须判断它是否只应在一局内有效。如果是，就标注 `[AutoStaticsCleanup]`；如果复位带有副作用（退订事件、销毁对象），改用 `[OnEnteringPlayMode]` 静态方法手动处理。纯缓存（类型注册表、表达式解析缓存等）跨局保留不影响正确性，可以不标注。

Entity、Logic、纯 C# Component、Prefab Binder 和 GameObject 生命周期见 `Assets/Prometheus/Framework/GameplayKit/README.md`。项目当前 Kit、System、事件、网络、生命周期与 Editor 工具的硬性约束统一记录在 `Docs/Arch/ArchSpec.md`；修改成型链路时必须同步维护该规范和对应系统文档。
