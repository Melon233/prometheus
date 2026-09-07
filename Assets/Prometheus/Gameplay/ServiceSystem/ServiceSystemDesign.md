# ServiceSystem 与领域 Gateway 设计

## 1. 定位

网络职责按**两层**划分：

| 层 | 契约 | 回答的问题 |
| --- | --- | --- |
| 会话与通道 | `IServiceSystem` | 连接是否可用？把这个 Packet 发出去。收到了一个 Packet。 |
| 领域适配器 | `IPoiGateway`、`IBagGateway` | 这个玩法动作对应哪个 Packet？这个响应里我关心哪个字段？ |

`ServiceSystem` 独占单局唯一 `INetworkClient`，其他 System 不得创建客户端、订阅底层会话或直接调用 NetworkKit。

**为什么要分这两层。** 早期版本把全部业务请求（PullChunk / Interact / GetItems / Gacha / UploadPosition…）都放在 `IServiceSystem` 上。这等于把 `ARCH-NET-001` 明令禁止 `INetworkClient` 承担的东西——房间、POI、背包、抽卡、位置——原样搬到了上一层：规则赢了，耦合没解决。每加一个玩法就要改 ServiceSystem，它会持续长成一个跨领域的服务上帝接口。现在每个领域自带 Gateway，新增一个网络请求只改动该领域自己的文件。

## 2. 职责划分

### ServiceSystem（业务无关）

- 使用稳定的本地玩家 ID，独占由 NetworkKit 创建的唯一 `INetworkClient`。
- 在 `EnterWorldAsync` 内部串行执行本局唯一一次连接与 JoinRoom，并缓存成功结果或失败原因。
- `RequestAsync(Packet, ct)`：确保已进入世界后发送**任意**业务 Packet 并等待关联响应。它不解释 Packet 内容。
- 在 `OnUpdate` 中调用 `INetworkClient.PumpEvents`，并把收到的 Packet **原样**转发到 `PushReceived`。
- 监听 NetworkKit 的业务无关 `Disconnected`，把已进入世界后的意外断线转换为 `IsWorldAvailable=false` 与 `WorldUnavailable`。
- 为每次调用组合调用方令牌与系统生命周期令牌，登记活动计数；释放时先取消操作，再等活动调用退出后释放客户端和同步原语。

### Gateway（领域适配器）

- 组装本领域的请求 Packet，经 `RequestAsync` 发出，取出本领域关心的响应字段。
- 从 `PushReceived` 的通用 Packet 中挑出本领域关心的推送，以强类型事件转发。
- 翻译领域枚举与协议枚举（例如 `World.PoiOp` ↔ `Protocol.PoiOp`）。
- **禁止**持有连接状态、重试策略或并发原语——那些全部归 ServiceSystem。
- **禁止**保存领域状态——响应的解释与缓存归对应的 System。

## 3. 请求接口

| 契约 | 接口 | 当前调用方 | 领域状态归属 |
| --- | --- | --- | --- |
| `IServiceSystem` | `EnterWorldAsync` | `PoiSystem` 启动流程 | ServiceSystem 完成连接与进入房间；World 应用返回的持久化位置 |
| `IServiceSystem` | `IsWorldAvailable` / `WorldUnavailable` | `PoiSystem` | 会话状态，不属于任何领域 |
| `IPoiGateway` | `PullChunkAsync` / `PullAllAsync` | `PoiSystem` | PoiSystem 应用 POI 状态 |
| `IPoiGateway` | `InteractAsync` | `PoiSystem` | PoiSystem 校验结果并触发 POI 表现 |
| `IPoiGateway` | `UploadPositionAsync` | `PoiSystem` | PoiSystem 提供当前玩家坐标 |
| `IPoiGateway` | `PositionReceived` | 尚无正式订阅者 | 订阅者按 `PlayerId` 过滤 |
| `IBagGateway` | `GetItemsAsync` | `BagSystem` | BagSystem 保存物品快照和修订号 |
| `IBagGateway` | `DrawGachaAsync` | 尚无正式调用方 | 后续抽卡 System 负责结果和表现 |

除 `EnterWorldAsync` 外的请求会先确保已经进入世界。首次进入失败后 ServiceSystem 缓存失败，本局后续请求不再访问服务器，避免多个 System 各自重试并持续输出错误。

## 4. Push 流程

```text
NetworkSession 后台接收
        │
        ▼
INetworkClient 通用 Packet Push 队列
        │ ServiceSystem.OnUpdate -> PumpEvents
        ▼
IServiceSystem.PushReceived（通用 Packet，Unity 主线程，不做任何解释）
        │ 各 Gateway 各自过滤
        ▼
IPoiGateway.PositionReceived 等强类型领域事件
```

`PlayerPositionPush` 包含 `PlayerId`。订阅者必须按业务身份过滤：PoiSystem 不把房间广播直接当作本地玩家恢复坐标，本地首次恢复只使用 `JoinRoomResponse.Position`。

## 5. 生命周期

注册顺序为 `ServiceSystem` → `PoiGateway` / `BagGateway` → 领域消费者，由 `PrometheusSystemInstaller.RegisterSystems` 保证。`GameplayKit` 逆序释放，因此：

1. 消费者（PoiSystem / BagSystem）先取消自身异步操作；
2. Gateway 再退订 `PushReceived`——此时通道仍然可用；
3. ServiceSystem 最后取消剩余调用并关闭网络连接。

各方都不保存对方实例，而是在调用点通过 `Core.Gameplay.GetSystem<IContract>()` 解析（`ARCH-SYS-005`）。

`IServiceSystem` 与全部 `I*Gateway` 的异步接口都接受 `CancellationToken`。ServiceSystem 为每次调用登记活动计数并组合自身生命周期令牌；同步 `Dispose` 只负责标记释放、退订和取消，不会立即销毁仍被 continuation 使用的 `enterWorldLock` 或 `INetworkClient`。最后一个活动调用退出后才执行最终资源释放，从而避免快速退出时出现信号量 Release、连接完成或回调写入已释放系统的竞态。

当前策略不自动重连。NetworkSession 在接收或发送异常时把 `Disconnected` 排队到 `PumpEvents` 调用线程；ServiceSystem 收到后清除已进入世界状态、缓存断线原因并发布一次 `WorldUnavailable`。后续请求快速失败，PoiSystem 同步停止坐标上传、区块拉取和交互。

## 6. 扩展方式

- **新增一个领域网络请求**：在该领域的 `*Gateway.cs` 中增加一个方法。不要改 `IServiceSystem`，也不要向 `INetworkClient` 添加业务方法。
- **新增一个领域**：新增 `I<Domain>Gateway` + `<Domain>Gateway`，与该领域同目录，并在组合根中注册在 `ServiceSystem` 之后、该领域消费者之前。
- **新增一种业务 Push**：在对应 Gateway 的 `OnPushReceived` 中识别 Packet Body，并以强类型事件发布。

`RequestAsync` 只允许由 `*Gateway.cs` 调用，由架构测试 `Sources_DoNotUseRequestChannelOutsideGateways` 执行。项目级硬约束见 `Docs/ArchSpec.md`。
