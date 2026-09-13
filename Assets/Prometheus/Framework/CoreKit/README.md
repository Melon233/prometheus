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

1. `Entry.Awake` 将 `GameRoot` 标记为跨场景保留。
2. `Entry.Start` 创建唯一 `Core`，不携带任何参数。
3. `Core` 构造时依次创建并注册 `AssetKit`、`EventKit`、`UIKit`；每个 Kit 在**注册**时由 `Core` 统一发布对应静态入口。
4. `Entry` 调用 `Core.Configure(installer)`：Core 以该安装器创建、注册最后一个 Kit `GameplayKit`。Core 只知道一个安装器，不认识任何玩法参数类型。
5. `Entry` 调用每个 Kit 的 `AfterNewAsync`，通过 `UniTask.WhenAll` 并发等待。
6. `GameplayKit.CreateSessionAsync` 等待 AssetKit 就绪，调用安装器注册全部公共 System，再并行驱动各 System 的 `AfterNewAsync`；EffectSystem 在该阶段按自己的私有地址加载 EffectLibrary。
7. `WhenAll` 完成后，`Core.AfterNew` 按注册顺序执行每个 Kit 的同步初始化；`GameplayKit.AfterNew` 依次初始化全部 System，再由安装器 `CreateInitialContent` 创建初始实体。
8. 全部 Kit 就绪后，`Entry` 通过 `Core.UI` 打开 `HudPanel`，并在每帧驱动 `Core.OnUpdate`。

`Core.Dispose` 按注册顺序逆序释放，因此最后注册的 `GameplayKit` 最先释放，`AssetKit` 最后释放其资源和场景句柄。

## 静态入口的唯一写入点

`Core` 是项目唯一的跨模块访问入口：

| 静态入口 | 模块契约 |
| --- | --- |
| `Core.Asset` | `IAssetKit` |
| `Core.Event` | `IEventKit` |
| `Core.UI` | `IUIKit` |
| `Core.Gameplay` | `IGameplayKit` |

这四个入口**只在 `Core.RegisterKit` 中写入**（`Core.PublishStaticEntry`），并只在 `Core.Dispose` 中清空。Kit 的构造函数不产生任何全局副作用：`new` 一个 Kit 与把它接入当前 Core 是两件可以分别发生的事，这使 Kit 可以被独立构造和测试。

`Core.GetKit<TKit>()` 只保留给 Core 自身管理和诊断查询，不作为业务模块之间的依赖传递方式（该禁令由架构测试 `Sources_DoNotResolveKitsByGetKit` 执行）。System 之间通过 `Core.Gameplay.GetSystem<IContract>()` 在使用点互相访问，不长期保存对方实例。

该约定依赖固定初始化顺序：`Core.Asset` 先于 UIKit 建立，`Core.Event` 和 `Core.UI` 先于 GameplayKit 建立，`Core.Gameplay` 先于所有 XSystem 与 Entity 初始化建立。因此正常业务链路不对这些入口增加空值兜底；生命周期外调用属于入口时序错误。手工搭建 Core 的测试必须提供同样完整的入口集合。

输入源等单局领域数据仍通过构造参数或方法参数显式传入。它们不是基础 Kit，也不属于本条跨模块访问规则。

Entity、Logic、纯 C# Component、Prefab Binder 和 GameObject 生命周期见 `Assets/Prometheus/Framework/GameplayKit/README.md`。项目当前 Kit、System、事件、网络、生命周期与 Editor 工具的硬性约束统一记录在 `Docs/Arch/ArchSpec.md`；修改成型链路时必须同步维护该规范和对应系统文档。
