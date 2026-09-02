# ServiceSystem 设计

## 1. 定位

`IServiceSystem` 是 Gameplay 层唯一网络服务入口，`ServiceSystem` 是由 `GameplayKit` 创建、注册、更新和释放的内部实现。它独占单局唯一 `INetworkClient`，其他 System 不得创建客户端、订阅底层会话或直接调用 NetworkKit Service。

ServiceSystem 只负责网络通信，不保存 POI、背包、任务等领域状态，也不决定请求成功后的玩法表现。

## 2. 当前职责

- 使用稳定的本地玩家 ID，并独占由 NetworkKit 创建的唯一 `INetworkClient`。
- 在 `EnterWorldAsync` 业务流程内部串行执行本局唯一一次连接与 JoinRoom 请求，并缓存成功结果或失败原因。
- 统一组装具体业务请求 Packet、解析业务响应，并隐藏底层连接和协议信封。
- 在 `OnUpdate` 中调用 `INetworkClient.PumpEvents`，保证 Push 在 Unity 主线程分发。
- 监听底层通用 `PushReceived`，识别 `PlayerPositionPush` 后通过 `IServiceSystem.PositionReceived` 向业务订阅者转发。
- 监听 NetworkKit 的业务无关 `Disconnected`，并把已进入世界后的意外断线转换为 `IsWorldAvailable=false` 与 `WorldUnavailable` 业务通知。
- 为全部异步业务接口组合调用方令牌与系统生命周期令牌，释放时先取消操作，再等活动调用退出后释放客户端和同步原语。
- 在自身释放时解除 Push 订阅并关闭底层客户端。

## 3. 请求接口

| 接口 | 当前调用方 | 领域状态归属 |
| --- | --- | --- |
| `EnterWorldAsync` | `WorldSystem` 启动流程 | ServiceSystem 完成连接与进入房间；World 应用返回的持久化位置 |
| `PullChunkAsync` / `PullAllAsync` | `WorldSystem` | WorldSystem 应用 POI 状态 |
| `InteractAsync` | `WorldSystem` | WorldSystem 校验结果并触发 POI 表现 |
| `UploadPositionAsync` | `WorldSystem` | WorldSystem 提供当前玩家坐标 |
| `GetItemsAsync` | `BagSystem` | BagSystem 保存物品快照和修订号 |
| `DrawGachaAsync` | 尚无正式调用方 | 后续抽卡 System 负责结果和表现 |

除 `EnterWorldAsync` 外的业务请求会先确保已经进入世界。首次进入失败后 ServiceSystem 缓存失败，本局后续业务请求不再访问服务器，避免多个 System 各自重试并持续输出错误。连接、断连和重连属于 NetworkKit 内部能力，不属于 `IServiceSystem` 的游戏业务契约。

## 4. Push 流程

```text
NetworkSession 后台接收
        │
        ▼
INetworkClient 通用 Packet Push 队列
        │ ServiceSystem.OnUpdate -> PumpEvents
        ▼
ServiceSystem 按 Packet Body 分类
        │
        ▼
IServiceSystem.PositionReceived 订阅者（Unity 主线程）
```

当前 `PlayerPositionPush` 包含 `PlayerId`。订阅者必须按业务身份过滤，WorldSystem 不把房间广播直接当作本地玩家恢复坐标；本地首次恢复只使用 `JoinRoomResponse.Position`。

## 5. 生命周期

`GameplayKit.RegisterGameplaySystems` 在其他网络消费者之前注册 ServiceSystem。WorldSystem 和 BagSystem 不保存注入实例，而是在调用点通过 `Core.Gameplay.GetSystem<IServiceSystem>()` 获取当前单局接口。GameplayKit 逆序释放 System，因此消费者先取消自身异步操作，ServiceSystem 最后取消剩余业务调用并关闭网络连接。

`IServiceSystem` 的所有异步接口都接受 `CancellationToken`。ServiceSystem 为每次调用登记活动计数并组合自身生命周期令牌；同步 `Dispose` 只负责标记释放、退订和取消，不会立即销毁仍被 continuation 使用的 `enterWorldLock` 或 `INetworkClient`。最后一个活动调用退出后才执行最终资源释放，从而避免快速退出时出现信号量 Release、连接完成或回调写入已释放系统的竞态。

当前策略不自动重连。NetworkSession 在接收或发送异常时把 `Disconnected` 排队到 `PumpEvents` 调用线程；ServiceSystem 收到后清除已进入世界状态、缓存断线原因并发布一次 `WorldUnavailable`。后续业务请求快速失败，WorldSystem 同步停止坐标上传、区块拉取和交互，避免持续输出断线日志。

新增业务请求时只扩展 `IServiceSystem` 与 ServiceSystem 的组包、解包逻辑，禁止向 `INetworkClient` 添加业务方法。新增业务 Push 时由 ServiceSystem 从通用 `PushReceived` 识别 Packet Body，并在 `OnUpdate` 主线程阶段通过业务接口发布。禁止让业务 System 绕过 ServiceSystem 获取客户端或会话。

项目级硬约束见 `Docs/ArchSpec.md`。
