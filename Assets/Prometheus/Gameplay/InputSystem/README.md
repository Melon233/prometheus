# 输入与 UI 点击链路

## 职责边界

输入系统只负责连续输入、键鼠快捷键、手柄输入和这些输入的动作级控制权仲裁。普通 UI Button 只使用 UIKit 生成的 `Button.onClick` 监听，不挂接 `OnScreenButton`，也不会伪装成虚拟设备按钮进入 InputAction。

屏幕摇杆需要连续二维值，因此继续使用 `OnScreenStick` 写入 `<PrometheusVirtualInput>/move`。`PrometheusVirtualInputDevice` 只声明这一项移动控件。

## 快捷键链路

`UnityInputActionSource` 在运行时创建 `Gameplay` Action Map，采样键盘、鼠标和手柄并生成 `InputFrame`。`InputSystem` 按 `InputContext`、绑定优先级和 `InputDeliveryMode` 分发动作。HUD 打开类快捷键交给独立 `HudCommandSystem.ReceiveInput`，小队数字键交给 `TeamSystem.ReceiveInput`，玩法动作交给当前上场实体的 `InputComponent`。

HUD 当前快捷键为：`L` 打开抽奖、`M` 打开小地图、`J` 打开任务、`P` 打开菜单、`G` 打开引导、`F5` 打开活动、`C` 打开角色、`B` 打开背包。数字键 `1`、`2`、`3` 切换三个固定小队槽位。技能为 `E`，大招为 `R`，跳跃为 `Space`，闪避为鼠标右键，鼠标左键只有在 GameView 屏幕范围内且未命中 UI 时才触发普通攻击；SceneView 和编辑器其他区域的点击不会进入玩法攻击。

## UI Button 链路

`UIPanelCodeGenerator` 为 Binder 中的普通 `Button` 生成抽象点击回调，并由生成的 `PanelBase` 自动注册和移除监听。业务代码只实现对应的 `OnXxxClick`，Prefab 的 Button `On Click()` 持久化列表保持为空。与 `OnScreenStick` 同节点的 Button 不生成点击回调，因为该节点表达连续拖拽而不是离散点击。

界面打开类按钮向 `HudCommandSystem.Execute` 提交命令，快捷键由该系统独立监听并调用同一入口，HudPanel 本身不实现 `IInputReceiver`。头像按钮直接调用 `TeamSystem.SwitchToSlot`。攻击、技能、大招、闪避和跳跃按钮调用 `InputSystem.QueueEntityButtonActions`，定向记录当前上场实体和离散动作；下一次输入阶段先清理上一帧输入，再通过 `InputComponent.ApplyButtonActions` 写入命令，随后 Entity Logic 在同一玩法帧消费。该定向命令不参与 InputAction 控制权仲裁。

## 生命周期

`HudCommandSystem` 在单局初始化时申请 `HudCommands` 控制租约，并在系统释放时归还；该生命周期不依赖 HudPanel 是否打开。HUD 打开时只获取点击需要的命令系统、战斗输入系统和小队系统，缓存关闭时释放字段监听，最终解绑时清空系统引用。UI 战斗点击命令在 `InputSystem` 每次输入阶段处理后清空，目标实体已经离开运行世界时该次点击随目标生命周期结束。

## 扩展约定

新增普通 UI Button 时，先把 Button 加入 `UIComponentBinder`，再运行 `Tools/Prometheus/UIKit/Generate Selected Panel`，最后在业务 Panel 中实现生成的点击回调。新增快捷键时，依次扩展 `InputActionMask`、`InputFrame`、`UnityInputActionSource` 和对应的 `IInputReceiver`；不要给 Button 重新添加 `OnScreenButton`。
