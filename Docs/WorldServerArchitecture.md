# 大世界 POI 服务器架构（Go + protobuf + MongoDB）

> 适用范围：prometheus 项目大世界 POI（采集物/收集物/交互物）的服务器权威同步。
> 本文档描述 POI 数据同步链路：语义化 ID + chunk 分区 + Go 服务器 + MongoDB + protobuf 通信。

---

## 1. 概述

POI 采用**服务器权威 + chunk 分区同步**：

- **身份**：语义化字符串 ID（`{Region}_{类型}_{序号}`，如 `Mond_Chest_1`），同时是 MongoDB 文档 `_id` 与客户端同步键。
- **空间分区**：`chunkId`（三位编码 `chunkX*1000 + chunkY`，chunk 坐标非负），用于按区块查询。
- **通信**：裸 TCP + 4 字节大端长度前缀 + protobuf `Packet` 信封。
- **持久化**：MongoDB（`prometheus` 库，`poi_states` 与 `backpack` 集合由服务按需写入）。
- **同步**：客户端按需拉取玩家附近 chunk 的状态（`PullChunk`），全量 `PullAll` 保留作调试。
- **权威**：服务器是唯一权威；客户端仅在 `Interact` 返回 `success=true` 后才做表现。

```
┌────────────── Unity 客户端 ──────────────┐   TCP   ┌────────── Go 服务器 ──────────┐   ┌──────────┐
│ WorldSystem ─ PoiNetworkClient           │ ──────► │ netx ─ service(PullChunk等)   │──►│ MongoDB  │
│   扫描场景 / AOI 显隐 / chunk 按需拉取      │  9000   │   读导出→播种→按 chunk 索引     │   │ 两个业务集合 │
└──────────────────────────────────────────┘        └───────────────────────────────┘   └──────────┘
```

---

## 2. 目录结构

```
Server/                          # Go 服务器（模块名 prometheus）
├── main.go / go.mod / go.sum
├── gen_proto.ps1                # 手动重新生成协议代码（仅 proto 变更时执行）
├── proto/poi.proto              # 协议唯一定义
├── gen/protocol/poi.pb.go       # protoc 生成的 Go 代码
├── internal/poi/                # 领域模型：Poi 记录 + 常量 + 导出读取
├── internal/store/              # Store/ItemStore 接口 + MongoDB POI 与背包实现
├── internal/room/               # 唯一默认房间与在线玩家坐标
├── internal/service/            # 权威逻辑：Seed / Pull / Interact / Gacha / Inventory
└── internal/netx/               # TCP 服务：编解码 + 分发

Tools/protoc/                    # protoc 编译器
Assets/Gen/Protocol/             # protoc 生成的 C# 代码 + 程序集定义
Assets/Plugins/Google.Protobuf/  # Google.Protobuf 运行时 dll
Assets/Prometheus/Gameplay/WorldSystem/
├── WorldSystem.cs               # 客户端编排：扫描场景 + AOI + chunk 按需拉取
├── ChunkIdCodec.cs              # chunkId 编解码
├── PoiOp.cs / PoiExportList.cs
├── Data/PoiConfig.cs            # Id / Region / ChunkId / 位置旋转
├── Network/                     # PoiNetworkClient / PoiStateApplier / PoiInteractionHandler
└── Editor/                      # WorldBakeWindow（导出）/ ServerProcessManager（自动启停）
```

---

## 3. 身份与 chunk 编码

### 3.1 语义化 ID

格式 `{Region}_{类型}_{序号}`，序号 1 起按类型独立计数，烘焙时生成并写回场景（已分配则复用）：

| 类型 | 示例 |
|------|------|
| 宝箱 | `Mond_Chest_1`、`Mond_Chest_2` |
| 七天神像 | `Mond_Statue_1` |
| 采集物 | `Mond_Gathering_1` |

### 3.2 chunkId

- 三位编码：`chunkId = chunkX * 1000 + chunkY`，`chunkX/chunkY ∈ [0,999]`。
- chunk 坐标非负，从 0 起；客户端编辑只向正方向添加 chunk（无负坐标偏移）。
- 世界坐标映射：`chunkX = floor(x / ChunkSize)`，`ChunkSize = 20m`。

---

## 4. 协议定义（Server/proto/poi.proto）

```proto
package poi;
option go_package = "prometheus/gen/protocol;protocol";
option csharp_namespace = "Xuan.Prometheus.Protocol";

enum PoiType { TELE_ANCHOR=0; STATUE=1; CHEST=2; SPIRIT_CORE=3; GATHERING=4; DUNGEON=5; MAP_BOSS=6; MONSTER_CAMP=7; }
enum PoiOp   { UNLOCK=0; OPEN_CHEST=1; COLLECT_CORE=2; GATHER=3; DEFEAT=4; }

message PoiState { string id=1; PoiType poi_type=2; /* 各类型 bool 状态字段 */ }
message PullAllRequest {}           message PullAllResponse { repeated PoiState states=1; }
message PullChunkRequest { int32 chunk_id=1; }
message PullChunkResponse { int32 chunk_id=1; repeated PoiState states=2; }
message InteractRequest { string id=1; PoiOp op=2; }
message InteractResponse { bool success=1; PoiState state=2; }
message Packet { uint64 request_id=100; oneof body { ... POI / room / position / gacha requests and responses ... } }
```

> `PoiState` 只同步可变状态 + `id`；位置/旋转/chunkId/region 属静态定义，存于导出配置（客户端从场景读取，服务器从导出读取），不随状态同步。

---

## 5. Go 服务器架构

| 包 | 职责 |
|----|------|
| `internal/poi` | `Poi` 完整记录（Id/Region/Type/Position/Rotation/ChunkID + 状态）、类型/操作常量、`LoadExport` |
| `internal/store` | `Store`/`ItemStore` 接口 + `MongoStore`/`ItemStore`，POI 与玩家背包持久化 |
| `internal/room` | 唯一 `default` 房间、玩家引用计数与权威坐标快照 |
| `internal/service` | `Service`：POI 内存索引、按玩家背包、交互掉落与抽卡 |
| `internal/netx` | TCP 会话、长度帧、request_id 关联、广播写锁与按 oneof 分发 |

**启动流程**：连 MongoDB → 加载 `config/items.json` 与默认玩家背包 → 读 `PoiExport.json` → `Seed`（按 id 判重，新 POI 入库）→ 创建唯一默认房间并监听 TCP。

**权威逻辑**：一次性操作（Unlock/OpenChest/CollectCore）重复请求返回 false；可刷新（Gather/Defeat）总是成功；变更后立即 Upsert。

---

## 6. 客户端-服务器同步流程

### 6.1 启动

`WorldSystem.AfterNew`：创建 `PoiNetworkClient` 并通过 `ConnectAsync` 显式确认服务器连接；成功后扫描场景 `PoiMono` → 绑定 `PoiEntity`（按 `Id` 建索引）→ 启用 AOI 与交互。兼容外观内部使用 `Framework/NetworkKit`，NetworkKit 会话连接后自动加入默认房间。

### 6.2 chunk 按需拉取

低频 tick（0.25s）时，计算玩家所在 chunk 及 3×3 邻域（裁剪到非负），对未同步的 chunk 发起 `PullChunk`，按 `Id` 应用状态到本地实体。

### 6.3 交互

`TryInteractAsync(entity, op)` → `InteractAsync(id, op)` → 服务器确认后 `PoiInteractionHandler.Apply` 触发表现。

### 6.4 坐标与抽卡

`UploadPositionAsync(Vector3)` → `UpdatePositionRequest` → 默认房间广播 `PlayerPositionPush`（包含发送者自身）；客户端通过 `PumpEvents` 在主线程触发 `PositionReceived`。`DrawGachaAsync` → `GachaRequest` → 服务器扣除一个 `Anemoculus`，从物品配置中排除该道具后随机发放一件，并返回最新背包快照。

---

## 7. MongoDB 数据组织

- **数据库**：`prometheus`，Docker 使用 MongoDB 7；本地端口绑定为 `127.0.0.1:27017`。
- **POI 集合**：`poi_states`，`_id` 为语义化字符串，`chunk_id` 建普通索引，文档保留领域状态子文档。
- **背包集合**：`backpack`，以 `(player_id, item_id, quality)` 作为 ReplaceOne + Upsert 条件。
- **玩家语义**：POI 状态是世界共享权威状态；背包按 `player_id` 隔离，玩家首次请求时从 MongoDB 懒加载。

---

## 8. 运行方式

- **日常开发**：进 Unity Play，`ServerProcessManager` 自动 `go build` + 启动，退出 Play 关闭。
- **手动启动**：`cd Server && go build -o bin/server.exe . && ./bin/server.exe -addr 127.0.0.1:9000 -export "../Assets/Resources/Config/PoiExport.json"`。
- **重新生成协议**（仅 proto 变更时）：`cd Server && ./gen_proto.ps1`。
- **导出 POI**：Unity 菜单 `Tools/World/Export POI Data (JSON)`（生成语义 Id + chunkId 并写回场景）。
- **MongoDB**：在 `docker/mongo` 执行 `docker compose up -d`；MongoDB 与 mongo-express 仅绑定回环地址，默认服务端连接串为 `mongodb://admin:admin123@localhost:27017/?authSource=admin`。

---

## 9. 关键决策

1. **语义化字符串 ID 替代雪花**：POI 是静态配置实体（百~千级、低频），稳定可读的字符串 ID 更合适，雪花保留给运行时动态实体。
2. **身份与空间分区分离**：`id`（稳定身份，含 Region+类型+序号）与 `chunkId`（空间分区，用于查询）各司其职。
3. **chunkId 非负三位编码**：客户端只在正方向添加 chunk，无需负坐标偏移。
4. **chunk 按需拉取替代全量**：客户端按玩家 AOI 拉取附近 chunk 状态，避免一次全量。
5. **领域模型与协议解耦**：Go 侧 `poi.Poi` 使用 BSON 标签持久化，与 `protocol.PoiState`（protobuf 类型）分离，`netx` 层互转；查询返回独立快照，避免序列化读写竞争。
6. **枚举数值对齐**：C#/Go/proto 枚举同数值，网络边界直接 cast。
7. **分层网络会话**：客户端按 Transport → Framing → Protocol → Session/RPC → Services 分层，服务器 `netx` 只负责会话与协议分发，业务状态由 `room`/`service` 管理。
