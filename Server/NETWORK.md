# 服务器网络架构

服务器网络链路按 Transport/Framing/Session/Service 分层。`internal/netx` 负责 TCP 监听、连接会话、4 字节大端长度帧、Protobuf 解码和响应写入；`internal/room` 管理唯一默认房间及在线玩家坐标；`internal/service` 负责 POI、玩家背包和抽卡等权威业务。会话写入通过连接级互斥锁串行化，避免坐标推送与请求响应互相穿插。

## 协议

协议定义位于 `Server/proto/poi.proto`，由 `Server/gen_proto.ps1` 生成 Go 和 Unity C# 类型。每个请求在 `Packet.request_id` 写入非零关联 ID，响应复制该 ID；服务器主动坐标推送使用 `request_id=0`。帧格式为 `[4 字节大端长度][Packet Protobuf 字节]`。

## 默认房间与坐标

服务器启动后创建 `room.DefaultRoomID` 对应的唯一默认房间。客户端发送 `JoinRoomRequest` 后绑定玩家身份；`UpdatePositionRequest` 写入该玩家坐标，服务器向默认房间全部在线会话广播 `PlayerPositionPush`，包括发送者自身，便于单客户端回环验证。

## 抽卡

`GachaRequest` 由服务器按当前会话玩家处理：先消耗一个 `Anemoculus`，再从 `config/items.json` 中排除 `Anemoculus` 的物品定义随机选择一个，发放一件奖励并返回最新背包快照。没有足够神瞳或没有可用奖励定义时返回失败，不会发放奖励。

## 并发与会话边界

同一 TCP 连接重复发送 `JoinRoomRequest` 是幂等的；更换玩家标识时先离开旧身份再加入新身份。服务器会话身份读写受服务器锁保护，连接写入由连接级互斥锁串行化。POI 查询和交互响应、背包查询均使用独立快照，网络序列化不会直接读取正在修改的权威对象。

## 验证

服务器侧测试位于 `internal/netx/server_test.go`、`internal/room/room_test.go` 和 `internal/service/gacha_test.go`；在 `Server` 目录执行 `go test -race ./...` 可验证单客户端坐标回推、TCP 抽卡、房间幂等加入及并发抽卡库存约束。
