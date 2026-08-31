# FilmSystem 阶段一

## 目标

阶段二在阶段一的基础上增加 Timeline 交互 Marker、对话/QTE 异步等待和可取消交互服务。分支、并行节点和嵌套演出仍属于后续阶段。

## 运行链路

`FilmSystem` 由 `GameplayKit` 注册并在单局初始化后创建 `[FilmSystem]` 根节点。每次 `Play` 创建独立的 `FilmInstance` 和 `PlayableDirector`，按 `PlayableBinding.streamName` 查找 `FilmBindingContext` 中的对象，然后调用 `SetGenericBinding` 完成轨道绑定。

当 `FilmDefinition.lockGameplayInput` 开启时，实例使用 `InputContexts.Cutscene` 和 `InputActionMask.All` 获取输入控制租约，普通玩法输入会在演出期间被屏蔽。声明为 `FilmCamera` 的绑定必须是 `CinemachineCamera`，实例会从 `CameraSystem` 获取镜头优先级租约，结束时恢复原优先级。

阶段一和阶段二只允许一个前台演出实例。所有结束路径（自然完成、主动停止、系统释放、配置失败、交互失败）都会释放输入租约、镜头租约、交互取消令牌和运行时对象。

## 配置步骤

1. 在 Project 窗口创建 `Prometheus/Film/Film Definition`。
2. 填写唯一 `FilmId`，指定 `TimelineAsset`。
3. 在 Timeline 中给需要运行时绑定的轨道设置唯一的轨道名称；该名称必须与 `FilmBindingDefinition.key` 一致。
4. 在 `FilmDefinition.bindings` 中声明这些名称。普通轨道使用 `Generic`，演出镜头使用 `FilmCamera`。
5. 业务侧创建 `FilmBindingContext`，把场景中的 `Animator`、`GameObject`、`AudioSource` 或 `CinemachineCamera` 注入对应名称，再调用 `FilmSystem.Play`。

## 生命周期接口

- `FilmSystem.Play`：绑定并启动演出，返回 `FilmHandle`。
- `FilmHandle.Pause` / `Resume`：暂停或恢复 Timeline，暂停期间保留系统租约。
- `FilmHandle.Stop` 或 `FilmSystem.StopCurrent`：主动停止并释放全部运行时资源。
- `FilmHandle.WaitForCompletionAsync`：等待 `Completed`、`Stopped` 或 `Failed` 终态。
- `FilmHandle.State` / `StopReason` / `Time`：查询实例状态、停止原因和当前时间。

## 阶段二交互

在 Timeline 的 Marker Track 上添加 `FilmInteractionMarker`，设置 `InteractionType`、`InteractionId`；QTE 还需设置 `QteSuccessActions` 和可选 `QteTimeoutSeconds`。Marker 到达时 Timeline 自动暂停，FilmSystem 调用 `IFilmInteractionService`，交互成功后恢复时间轴，交互失败或超时则以 `InteractionFailed` 结束演出。

`FilmSystem` 默认使用 `ManualFilmInteractionService`。它会触发 `DialogueRequested` 和 `QteRequested` 事件，外部测试代码或临时 UI 可以调用 `CompleteDialogue` / `CompleteQte` 完成交互。正式对话系统可通过 `new FilmSystem(runtimeRoot, customService)` 注入自己的服务实现。QTE 的输入会通过现有 `InputSystem` 的 Cutscene 控制租约交给交互服务，默认手动服务会在成功动作本帧按下时自动完成。

## 手动验证

创建一个包含 `ActivationTrack` 或 `AnimationTrack` 的 Timeline，给轨道设置名称 `Actor`，在 FilmDefinition 中声明 `Actor` 为必需 Generic 绑定，并把一台场景对象注入 `FilmBindingContext`。在 Marker Track 添加一个 `FilmInteractionMarker`，将类型设为 Dialogue、ID 设为 `manual_dialogue`。运行后监听 `ManualFilmInteractionService.DialogueRequested`，调用 `CompleteDialogue(instanceId, "manual_dialogue", true)`，确认 Timeline 先暂停、完成后恢复；再调用 `Pause`、`Resume` 和 `Stop`，确认状态变化且实例 Director 被销毁。

若测试镜头，额外创建一台禁用的 `CinemachineCamera`，声明绑定角色为 `FilmCamera`，播放期间确认其优先级高于 `Player Follow Camera`，停止后恢复创建前的优先级。

## 后续阶段

- 阶段二（已完成）：Timeline Marker 命令桥接、Dialogue/QTE 交互和可取消异步等待。
- 阶段三：条件分支、并行节点、等待事件、嵌套演出和优先级抢占。
- 阶段四：跳过策略、断点恢复、存档和网络同步。
## 阶段三流程编排（已实现）

### 条件分支

在 Marker Track 添加 `FilmBranchMarker`，填写 `VariableKey`、`ExpectedValue`、`TrueTime` 和 `FalseTime`。调用 `FilmSystem.Play` 时可传入 `FilmFlowContext`，例如 `new FilmFlowContext().Set("choice", "yes")`。Marker 到达后比较字符串并跳转 Timeline 时间；未找到变量时按 false 分支处理。

### 等待事件

添加 `FilmWaitEventMarker` 并填写唯一 `EventId`。交互服务需要同时实现 `IFilmFlowService`，事件完成前 Timeline 会暂停并处于 `WaitingForInteraction`。默认的 `ManualFilmInteractionService` 会触发 `EventRequested`，测试或临时 UI 可调用 `CompleteEvent(instanceId, eventId, succeeded)`。

### 嵌套与并行

`FilmSubFilmMarker` 会暂停父演出并等待子 `FilmDefinition` 完成；`FilmParallelMarker` 会同时启动多个子演出并等待全部完成，任一子演出失败则父演出以 `InteractionFailed` 结束。阶段三子演出复用父级 `FilmBindingContext`，子定义必须使用相同的绑定名称。

并行子演出必须自行避免输入和镜头租约冲突：建议关闭子定义的 `LockGameplayInput`，并避免绑定同一个 `FilmCamera`。父演出在子演出运行期间会暂时释放自身租约，子演出结束后自动恢复。

### 优先级抢占

`FilmDefinition.Priority` 数值越大优先级越高。顶层 `Play` 只有在新定义优先级更高时才会抢占当前演出；被抢占实例进入 `Stopped`，`StopReason` 为 `Replaced`。同优先级或更低优先级调用会抛出 `InvalidOperationException`。

## 阶段四：跳过、存档与同步

将 `FilmDefinition.SkipMode` 设置为 `ToEnd` 后，业务可通过 `FilmHandle.Skip()` 将演出推进到 Timeline 结尾；结束状态为 `Stopped`，原因是 `Skipped`。保持 `None` 时调用会抛出异常。

`FilmHandle.CaptureSnapshot()` 返回 `FilmPlaybackSnapshot`，包含 `FilmId`、Timeline 时间、状态和流程变量副本。存档系统可保存这些字段，并在重新创建场景绑定后调用 `FilmSystem.PlayFromSnapshot` 恢复。恢复会把已到达时间点之前的 Marker 标记为已处理，避免重复触发。

`FilmSystem.SnapshotCaptured` 是同步通知入口。网络层或存档层可以订阅该事件并自行序列化、发送或持久化快照；FilmSystem 不依赖具体网络协议。
