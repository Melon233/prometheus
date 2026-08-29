# NetworkKit 网络架构

NetworkKit 将客户端网络职责分为 Transport、Framing、Protocol、Session/RPC 和 Services 五层。`Transport` 只收发原始字节；`LengthPrefixedFrameCodec` 处理 TCP 4 字节大端长度帧；`PacketCodec` 负责 Protobuf；`NetworkSession` 负责连接、request_id 请求关联和服务器推送队列；业务服务只暴露默认房间、POI 和抽卡接口。

`NetworkClient.ConnectAsync` 会连接服务器并加入唯一默认房间。网络接收循环可在后台运行，但坐标推送通过 `PumpEvents` 在 Unity 主线程触发，避免网络线程直接修改场景或 UI。`DefaultRoomService.UploadPositionAsync` 上传坐标，服务器会把 `PlayerPositionPush` 回推给同房间全部玩家，包括发送者，便于单客户端回环验证。

现有 WorldSystem 继续通过 `PoiNetworkClient` 兼容外观访问 POI；该外观同时提供 `UploadPositionAsync(Vector3)`、`PositionReceived` 和 `DrawGachaAsync`，而框架层服务只接受三个浮点坐标，因此旧业务无需直接依赖会话、传输或帧编解码即可接入坐标同步和抽卡。

同一会话可以并行发起多个请求，`NetworkSession` 用 `request_id` 关联响应并用写锁串行化帧发送；主动推送统一进入队列，只有调用 `PumpEvents` 时才在主线程触发业务事件。

由于项目运行时代码位于显式引用程序集 `Runtime` 中，`Assets/Prometheus/Runtime.asmdef` 已声明对 `Prometheus.NetworkKit` 的依赖，避免兼容外观在 Unity 编译时丢失框架引用。

协议代码由 `Server/gen_proto.ps1` 从 `Server/proto/poi.proto` 生成到 `Server/gen/protocol` 和 `Assets/Gen/Protocol`，禁止手工修改生成文件。
