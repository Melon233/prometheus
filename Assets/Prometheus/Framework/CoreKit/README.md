# CoreKit 与游戏入口

## 正式入口

`Assets/Resources/Entry.unity` 是 Player Build 中唯一直接配置的入口场景。场景只包含常驻根对象 `GameRoot`，其 `Entry` 组件保存资源包、玩法场景、EffectLibrary、角色、敌人和出生坐标等全部启动参数。`SampleScene` 不保存入口脚本或启动参数。

## 初始化顺序

1. `Entry.Awake` 将 `GameRoot` 标记为跨场景保留。
2. `Entry.Start` 创建唯一 `Core`，并把 Inspector 参数复制到不可变的 `GameplayStartupOptions`。
3. `Core` 先建立 `Core.Asset`，再依次创建并注册 `AssetKit`、`EventKit` 和 `UIKit` 三个基础 Kit，确保 UIKit 构造后即可通过 `Core.Asset` 加载资源。
4. `Core.Configure` 配置 AssetKit，并创建、配置和最后注册 `GameplayKit`；GameplayKit 构造时立即建立 `Core.Gameplay`。
5. `Entry` 调用每个 Kit 的 `AfterNewAsync`，通过 `UniTask.WhenAll` 并发等待全部异步任务。
6. `AssetKit.AfterNewAsync` 初始化 `DefaultPackage`；`GameplayKit.AfterNewAsync` 等待 AssetKit 就绪，然后异步加载 EffectLibrary 和 `SampleScene`。
7. `WhenAll` 完成后，`Core.AfterNew` 按注册顺序执行每个 Kit 的同步初始化；GameplayKit 在这里初始化玩法 System 和初始 Entity。
8. 全部 Kit 就绪后，`Entry` 通过 `Core.UI` 打开 `HudPanel`，并在每帧驱动 `Core.OnUpdate`。

## 模块访问

`Core` 是项目唯一的跨模块访问入口。Kit、Gameplay System、Entity Logic、UI 和辅助对象不通过构造参数、初始化参数或 Entity 字段逐层传递基础模块，统一使用以下静态入口：

| 静态入口 | 模块契约 |
| --- | --- |
| `Core.Asset` | `IAssetKit` |
| `Core.Event` | `IEventKit` |
| `Core.UI` | `IUIKit` |
| `Core.Gameplay` | `IGameplayKit` |

`Core.GetKit<TKit>()` 只保留给 Core 自身管理和诊断查询，不作为业务模块之间的依赖传递方式。`GameplayKit` 内部 System 通过 `Core.Gameplay.GetSystem<TSystem>()` 互相访问；Entity 只保存 `EntityId`，不保存 GameplayKit 引用。

该约定依赖固定初始化顺序：`Core.Asset` 先于 UIKit 建立，`Core.Event` 和 `Core.UI` 先于 GameplayKit 建立，`Core.Gameplay` 先于所有 XSystem 与 Entity 初始化建立。因此正常业务链路不对这些入口增加空值兜底；生命周期外调用属于入口时序错误。

EffectLibrary、输入源、RuntimeRoot、Film 绑定等单局配置或领域数据仍通过构造参数或方法参数显式传入。它们不是基础 Kit，也不属于本条跨模块访问规则。

Entity、Logic、纯 C# Component、Prefab Binder 和 GameObject 生命周期的目标重构方案见 `Assets/Prometheus/Framework/GameplayKit/README.md`；该文档明确标注了当前实现与目标架构的边界。

`Core.Dispose` 按注册顺序逆序释放，因此最后注册的 `GameplayKit` 最先释放，`AssetKit` 最后释放其资源和场景句柄。

## 参数归属

玩法场景不能反向提供启动参数。敌人出生点以世界坐标保存在入口组件中；EffectLibrary 使用 YooAsset 地址加载；角色、敌人和场景同样只保存地址。这样 `GameplayKit.AfterNewAsync` 在加载 `SampleScene` 前已经拥有完整且稳定的配置。
