# CoreKit 与游戏入口

## 正式入口

`Assets/Resources/Entry.unity` 是 Player Build 中唯一直接配置的入口场景。场景只包含常驻根对象 `GameRoot`，其 `Entry` 组件保存资源包、玩法场景、EffectLibrary、角色、敌人和出生坐标等全部启动参数。`SampleScene` 不保存入口脚本或启动参数。

## 初始化顺序

1. `Entry.Awake` 将 `GameRoot` 标记为跨场景保留。
2. `Entry.Start` 创建唯一 `Core`，并把 Inspector 参数复制到不可变的 `GameplayStartupOptions`。
3. `Core` 依次注册 `AssetKit`、`EventKit` 和 `UIKit` 三个基础 Kit。
4. `Core.Configure` 配置 AssetKit，并创建、配置和最后注册 `GameplayKit`。
5. `Entry` 调用每个 Kit 的 `AfterNewAsync`，通过 `UniTask.WhenAll` 并发等待全部异步任务。
6. `AssetKit.AfterNewAsync` 初始化 `DefaultPackage`；`GameplayKit.AfterNewAsync` 等待 AssetKit 就绪，然后异步加载 EffectLibrary 和 `SampleScene`。
7. `WhenAll` 完成后，`Core.AfterNew` 按注册顺序执行每个 Kit 的同步初始化；GameplayKit 在这里初始化玩法 System 和初始 Entity。
8. 全部 Kit 就绪后，`Entry` 通过 `Core.UI` 打开 `HudPanel`，并在每帧驱动 `Core.OnUpdate`。

## 模块访问

需要按契约解耦的代码使用当前 Core 实例的 `GetKit<TKit>()`。明确依赖正式全局模块的表现层和快捷调用可以直接使用以下静态入口：

| 静态入口 | 模块契约 |
| --- | --- |
| `Core.Asset` | `IAssetKit` |
| `Core.Event` | `IEventKit` |
| `Core.UI` | `IUIKit` |
| `Core.Gameplay` | `IGameplayKit` |

`Core.Dispose` 按注册顺序逆序释放，因此最后注册的 `GameplayKit` 最先释放，`AssetKit` 最后释放其资源和场景句柄。

## 参数归属

玩法场景不能反向提供启动参数。敌人出生点以世界坐标保存在入口组件中；EffectLibrary 使用 YooAsset 地址加载；角色、敌人和场景同样只保存地址。这样 `GameplayKit.AfterNewAsync` 在加载 `SampleScene` 前已经拥有完整且稳定的配置。
