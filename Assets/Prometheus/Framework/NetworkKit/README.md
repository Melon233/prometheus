# NetworkKit 网络架构

> **当前状态（2026-09-27，单机版）**：玩法层 `Prometheus.Gameplay` 与组合根均不再引用 NetworkKit；
> 唯一引用方是未接入组合根的联网实现装配 `Prometheus.Gameplay.Online`（`Gameplay/ServiceSystem/`）。
> 本文描述的网络链路在恢复联网之前不会运行，代码与测试保留以便日后接回，详见 `Gameplay/ServiceSystem/ServiceSystemDesign.md`。

NetworkKit 将客户端网络职责分为 Transport、Framing、Protocol 和 Session/RPC 四层。`Transport` 只收发原始字节；分帧器处理定长 Head 与变长 Body；`PacketCodec` 负责 Protobuf；`NetworkSession` 负责连接生命周期、request_id 请求关联和通用 Push 队列；这些具体实现均为程序集内部类型。

传输层每个 `TransportPacket` 固定由 `[Head][Body]` 组成。`PacketHead` 当前固定为 4 字节，第一字段 `BodyLength` 使用网络大端序并描述随后变长 Body 的准确字节数；接收端先完整读取 Head，再按 `BodyLength` 完整读取 Body，因此 TCP 半包和连续 Packet 不会破坏业务消息边界。Body 是序列化后的业务 Protobuf `Packet`，`request_id` 和 oneof 消息仍属于业务协议，不进入传输 Head。

`INetworkClient` 是业务无关的基础设施契约，只公开连接、主动断连、重连、通用 `Packet` 请求关联、通用 `Packet` Push 和主线程泵送。NetworkKit 不读取具体 Packet Body 的业务含义，不处理玩家 ID、加入房间、POI、背包、抽卡或位置同步，也禁止为这些业务扩展 `INetworkClient`。

Gameplay 层只有 `ServiceSystem` 可以通过 `NetworkClientFactory` 创建并持有 `INetworkClient`。ServiceSystem 只提供业务无关的会话与通用请求通道（`EnterWorldAsync` / `RequestAsync` / `PushReceived`）；组装和解析具体业务 Packet 由各领域自己的 Gateway（`IPoiGateway`、`IBagGateway`）负责。连接、断连、重连和 `PumpEvents` 等网络基础能力不得出现在 `IServiceSystem`。

联网版中 PoiSystem 进入世界时经 `IPoiGateway.EnterWorldAsync` 转发到 `IServiceSystem.EnterWorldAsync` 进入默认世界；ServiceSystem 在该业务流程内部建立底层连接并发送 JoinRoom 请求。ServiceSystem 在自身 `OnUpdate` 中调用 `PumpEvents`，并把通用 Packet 原样转发到 `PushReceived`；按业务类型分类由各领域 Gateway 完成，并以强类型事件在主线程发布。

同一会话可以并行发起多个请求，`NetworkSession` 用 `request_id` 关联响应并用写锁串行化帧发送；主动推送统一进入队列，只有调用 `PumpEvents` 时才在调用线程触发通用 `PushReceived`。接收或发送异常会关闭传输，并通过同样在 `PumpEvents` 线程触发的业务无关 `Disconnected` 事件通知上层；主动断连不触发该事件。主动断连保留客户端实例以便显式重连，永久释放后禁止再次连接或请求。

协议代码由 `Server/gen_proto.ps1` 从 `Server/proto/game.proto` 生成到 `Server/gen/protocol` 和 `Assets/Gen/Protocol`，禁止手工修改生成文件。
