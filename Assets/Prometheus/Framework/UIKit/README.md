# UIKit 面板与按钮绑定

## 初始化与 EventSystem

`UIKit.AfterNew` 初始化时会检查 `EventSystem.current`。场景已经存在 EventSystem 时直接沿用；场景未提供时，UIKit 动态创建包含 `EventSystem` 和 `InputSystemUIInputModule` 的 `[UIKit.EventSystem]` 节点，并通过 `DontDestroyOnLoad` 保持跨场景可用。

UIKit 只记录和释放自己创建的事件系统，不接管场景预置的 EventSystem。项目启用新版 Input System，因此运行时事件系统固定使用 `InputSystemUIInputModule`，不创建旧版 `StandaloneInputModule`。

`UIKit` 使用无参构造并在构造时注册 `Core.UI`。面板 Prefab 和世界 UI 资源统一通过 `Core.Asset` 加载，不保存或注入 `IAssetKit`；Core 的固定创建顺序保证 UIKit 存活期间 AssetKit 一定可用。

## 面板生成链路

UI Prefab 根节点使用 `UIComponentBinder` 保存稳定名称和组件引用。选中 Prefab 后执行 `Tools/Prometheus/UIKit/Generate Selected Panel`，生成器会覆盖 `Assets/Prometheus/Framework/UIKit/Generated` 下的强类型 `PanelBase`，但只在业务 Panel 不存在时创建业务脚本，因此重复生成不会覆盖已经实现的界面逻辑。

生成的 `PanelBase` 负责按 Binder 索引和名称取得组件、注册 Button 点击监听、最终解绑监听并清空 Unity 对象引用。业务 Panel 负责实现生成的抽象 `OnXxxClick` 回调以及 `OnBind`、`OnInitialize`、`OnOpen`、`OnUpdate`、`OnClose`、`OnUnbind` 生命周期。

## Button 规则

Binder 中的普通 Unity `Button` 一律生成点击回调，不根据同节点是否存在 Input System 组件改变按钮语义。Prefab 的 Button `On Click()` 持久化列表必须保持为空，避免 Inspector 监听和生成监听重复执行。

普通按钮不允许挂接 `OnScreenButton`。鼠标和触屏点击直接进入 UIKit 回调；键盘与手柄快捷键由独立的业务命令系统通过项目 `InputSystem` 监听，UIPanel 不实现 `IInputReceiver`。

`OnScreenStick` 是唯一例外。它表达连续二维拖拽值而不是离散点击，因此与 `OnScreenStick` 同节点的 Button 不生成点击回调，摇杆继续把移动值写入 `<PrometheusVirtualInput>/move`。

## HUD 约定

HUD 的抽奖、小地图、任务、菜单、引导、活动、角色和背包按钮向 `HudCommandSystem` 提交命令，对应快捷键由 `HudCommandSystem` 自己监听并汇入同一个 `Execute` 入口。三个头像按钮直接调用 `TeamSystem.SwitchToSlot`。攻击、技能、大招、闪避和跳跃按钮通过 `InputSystem.QueueEntityButtonActions` 为当前上场实体提交一次离散玩法命令，以保证点击发生在任意 Unity 更新时点都不会被下一帧输入重置覆盖。

快捷键不调用 Button 的 `onClick.Invoke()`，也不读取 Button 是否显示或可交互。`HudCommandSystem` 是界面命令的业务入口，UIKit 点击和 InputAction 快捷键分别调用它，从而避免键盘输入依赖某个具体面板实例。

## 生命周期约定

生成监听只在面板绑定时注册一次，并在最终解绑时移除。UIKit 每帧只向处于打开状态且实例有效的面板分发 `OnUpdate(float dt)`；缓存关闭不会执行该回调。业务 Panel 在 `OnClose` 只释放数据监听；快捷键租约由独立 `HudCommandSystem` 按单局生命周期持有和释放。最终 `OnUnbind` 必须清空业务持有的 System 和实体引用，跨模块事件订阅统一通过 `Core.Event` 注册和移除。

HUD 小地图复用 Binder 已生成的 `MiniMapButton` 作为容器，不把运行时地图图层加入稳定绑定表。`HudPanel.OnInitialize` 创建 RawImage、径向虚化材质和 POI 标记层；`OnOpen`/`OnClose` 订阅或解除 `WorldSystem` 的地图事件，世界坐标映射统一调用 `WorldMapDefinition`。大地图使用独立的 `MapPanel`，但同样只读取 `WorldSystem` 的地图和 POI 数据。
