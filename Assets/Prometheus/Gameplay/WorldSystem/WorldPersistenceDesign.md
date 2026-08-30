# 大世界 POI 服务器权威 + 状态同步 设计

> 关联：`WorldSystemSpec.md` / `WorldSystem.md`
> 本文档描述 POI 的**服务器权威**数据流：策划导出 → 服务器入库 → 启动同步 → 请求确认交互。

---

## 0. 背景与目标

需求是把 POI 的**可变状态**（解锁/开启/收集/击败等）托管到**服务器**，客户端不再直接改状态，而是**发请求、服务器确认后才做表现**。当前通过 `Framework/NetworkKit` 的 TCP + protobuf 链路访问 Go 服务器，服务器使用 MongoDB 持久化 POI 与背包状态。

### 核心原则
- **服务器是权威**：一切状态变更经服务器校验、落库、回包 true 后，客户端才更新表现。
- **单一 UUID 主键**：`PoiConfig.Id` 是编辑器首次烘焙/导出时写回场景的 32 位无分隔小写 UUID，和位置、类型、Chunk、名称及遍历顺序无关；客户端与服务器都使用它作为同步键和数据库主键，删除后永不复用。

---

## 一、完整工作流（4 步）

### 1. 策划端导出（编辑器）
策划在场景摆放 `PoiMono`（挂 `PoiConfig`），执行菜单 **Prometheus/World/Export POI Data (JSON)**。
导出内容包括：**PoiId / PoiType / 坐标 Position / 旋转 Rotation / aoiExempt / 各类型专属配置**。

- 缺失或非 UUID 的 Id 会生成新的 UUID，并把 Id、Region、ChunkId、Position **写回场景 `PoiMono.Config`**，随后保存场景。
- 已有合法 UUID 永远复用；发现重复 UUID 时整批导出失败，并报告对象路径，避免复制 POI 后发生引用冲突。
- 导出为 JSON：`Assets/Resources/Config/PoiExport.json`，运行时通过 `PoiExportLoader`（Resources.Load）读取。

### 2. 服务器入库
`PoiSyncService` 初始化时：
- 读取 `PoiExport.json`（策划导出的定义）。
- 读取已有数据库（`JsonPoiDataStore` → `persistentDataPath/poi_states.json`）。
- 服务器先校验整批 ID 非空、格式合法且不重复，再按 UUID upsert；已有记录只更新静态字段并保留玩家状态。
- 交互请求会裁剪首尾空白并统一 UUID 为小写后查找活动 POI，避免表示差异造成 ID 查找失败。
- 数据库记录为 `PoiState`（见 §二）。本次导出缺失的合法 UUID 标记为 `Retired`，保留历史状态但不再同步或允许交互。

> 因此每次重启不会重复分配 UUID；策划移动或改类型不会改变 ID，类型变更应删除旧 POI 并创建新 UUID。

### 3. 游戏启动同步
`WorldSystem.AfterNew`：

- 先通过 `PoiNetworkClient.ConnectAsync` 执行一次 POI 服务器连接检测；连接成功后才扫描场景、注册 POI 实体并启用更新与交互逻辑。
- 初始化检测失败时仅记录一次 Warning，并保持 `WorldSystem` 禁用；本局不再执行 AOI 刷新、区块同步或服务器交互，避免服务器未启动时持续输出连接错误。
- 扫描场景 `PoiMono` 绑定 `PoiEntity`，按 **PoiId** 建立索引 `poisByPoiId`。
- 调 `sync.PullAll()` 取全部 `PoiState`，**按 poiId** 匹配实体，经 `PoiStateApplier.Apply` 写入对应 Logic。

### 4. 交互请求确认
客户端交互不再直接改 Logic，而是：
```
WorldSystem.TryInteract(entity, op)
  → sync.RequestApply(PoiInteraction{ PoiId, Op })   // 服务器校验并落库
  → 返回 true ?
     是 → PoiInteractionHandler.Apply(entity, op)    // 触发本地表现（状态+事件）
     否 → 不表现
```

---

## 二、数据模型

### 静态定义 `PoiConfig`（客户端 + 导出）
- `PoiId` / `PoiType` / `Position` / `Rotation` / `aoiExempt` / 各类型 Config。
- **不含可变状态**；`PoiId` 本身就是客户端与服务器共用的 UUID 主键。

### 服务器记录 `PoiState`（可序列化，数据库）
```csharp
[Serializable]
public sealed class PoiState
{
    public string PoiId;     // 编辑器分配的 UUID，数据库主键与同步键
    public PoiType poiType;  // 服务器据此决定 Unlock 落到哪个字段
    public bool retired;     // 已从当前导出删除，仅保留历史状态，不下发且不可交互

    public bool  statueUnlocked; public int statueLevel; public float statueProgress; // 神像
    public bool  anchorUnlocked;            // 锚点
    public bool  gatheringGathered;         // 采集（消费态）
    public bool  chestOpened;               // 宝箱
    public bool  dungeonUnlocked;           // 副本
    public bool  mapBossDefeated;           // 地图 Boss（消费态）
    public bool  spiritCoreCollected;       // 神瞳
}
```

### 交互请求 `PoiInteraction`
```csharp
public enum PoiOp { Unlock, OpenChest, CollectCore, Gather, Defeat }

public struct PoiInteraction { public string PoiId; public PoiOp Op; }
```

---

## 三、服务器（模拟）职责：`PoiSyncService`

| 接口 | 说明 |
|------|------|
| `PoiSyncService(store, exportedPois)` | 校验导出 UUID + upsert，并将缺失项标记 Retired |
| `PullAll()` | 启动时下发全部状态 |
| `RequestApply(PoiInteraction) -> bool` | 交互请求：校验 + 落库，成功返回 true |

**校验规则**：
- **一次性操作**（Unlock / OpenChest / CollectCore）：重复请求返回 `false`（已做过）。
- **可刷新操作**（Gather / Defeat）：总是返回 `true`（重生在客户端，见 §五）。

数据库存储 `IPoiDataStore` / `JsonPoiDataStore`：`List<PoiState>` 序列化为 JSON 文件，模拟真实库。

---

## 四、文件规划

```
WorldSystem/
├─ Server/                          ← 原 Persistence 更名
│  ├─ PoiState.cs                   // 服务器数据库记录
│  ├─ IPoiDataStore.cs              // 数据库存储抽象
│  ├─ JsonPoiDataStore.cs           // JSON 文件模拟数据库（原 LocalPoiDataStore）
│  ├─ PoiSyncService.cs             // 模拟服务器（读导出/UUID入库/PullAll/RequestApply）
│  ├─ PoiExportLoader.cs            // 读取策划导出 JSON
│  ├─ PoiInteraction.cs             // PoiOp 枚举 + PoiInteraction
│  ├─ PoiInteractionHandler.cs      // 服务器确认后应用到本地 Logic
│  └─ PoiStateApplier.cs            // 启动时按 poiId 应用状态
├─ Data/PoiConfig.cs                // 保存编辑器分配的 UUID 与静态定义
├─ PoiMono.cs                       // 场景 POI 配置载体
├─ Editor/WorldBakeWindow.cs        // UUID 分配、重复校验与 JSON 导出
└─ WorldSystem.cs                   // 按 PoiId 同步 + TryInteract 请求确认
```

---

## 五、注意点 / 边界

- **Rotation 仅存储暂不应用**：`PoiConfig.Rotation` 已导出记录，运行时绑定 `PoiEntity` 时暂未设置 `bindGo` 旋转；后续需要时在 `WorldSystem.LoadFromScene` 中应用即可。
- **可刷新 POI（采集/地图 Boss）**：服务器只记录"消费态"（gathered/defeated），重生计时在客户端 `RespawnablePoiLogic`。因此重复采集/击败请求服务器恒返回 true。
- **怪物营地**：刷新属边缘逻辑，**不入库**，`ClearCamp` 保持纯本地。
- **PoiId 稳定性**：同步依赖编辑器写回的 UUID；策划移动 POI 或修改静态配置不会改变 ID。复制对象若产生重复 UUID，导出会阻止整批写入并报告对象路径；删除的 POI 标记 `Retired`，UUID 永不复用。类型变更应删除旧 POI 并创建新 UUID。

---

## 六、当前真实网络接入

客户端 `PoiNetworkClient` 通过 `Framework/NetworkKit` 的 Transport → Framing → Protocol → Session/RPC → Services 分层访问服务器；`TryInteractAsync`、chunk 拉取、坐标推送和抽卡均复用同一会话。`WorldSystem.AfterNew` 显式调用 `ConnectAsync`，连接成功后才扫描场景并启用同步与交互；连接失败时保持系统禁用并记录一次告警。

服务器侧保留 POI 状态校验与 MongoDB 持久化，网络边界由 `Server/internal/netx` 负责帧解码、request_id 关联和业务分发。
