# 世界系统（World System）设计

> 对应 Spec：`Assets/Prometheus/Gameplay/WorldSystem/WorldSystemSpec.md`
> 本文档为 `WorldSystem` 的落地设计，描述数据结构、编辑器/烘焙流程、运行时生命周期管理，以及与现有 `EntitySystem` 的对接方式。

---

## 1. 目标与范围

### 1.1 目标
- 管理大世界中 **8 种兴趣点（POI）** 的生命周期：传送锚点、七天神像、宝箱、神瞳、采集物、副本、地图 Boss、怪物营地。
- 世界按网格分割，POI 归属到所在 Region，运行时**只加载/实例化玩家附近的 POI**，控制活跃实体数量。
- `WorldSystem` 只负责**生命周期**；POI 的**具体逻辑**通过 entity-logic-component（`EntitySystem`）承载。

### 1.2 范围界定
- **做**：数据结构、烘焙生成、region 邻域 + 兴趣半径的按需激活/失活、POI 实体的实例化与回收。
- **不做**：持久化（按 Spec 暂不考虑，每次运行用初始化数据）、各 POI 的业务细节（奖励、战斗、传送等）——它们在各类型的 Logic 内实现，本文只给出挂载方式与骨架。

### 1.3 对接的现有架构
| 现有组件 | 位置 | 在本系统中的角色 |
|----------|------|-----------------|
| `XSystem` | `Assets/Prometheus/Framework/GameplayKit/XSystem.cs` | `WorldSystem` 的基类，通过 `GameplayKit.AddSystem` 注册 |
| `IGameplayKit` | `Assets/Prometheus/Framework/GameplayKit/GameplayKit.cs` | 提供 `GetSystem<T>()` / `TryGetSystem<T>()` 供系统间协作 |
| `EntitySystem` | `Assets/Prometheus/Gameplay/EntitySystem/EntitySystem.cs` | 托管 POI 实体（注册/逐帧调度/回收） |
| `Entity` | `Assets/Prometheus/Framework/GameplayKit/Entity.cs` | POI 实体基类（直接继承 `Entity`） |
| `IComponent` / `ILogic` | Framework 内 | POI 的"数据组件"与"行为逻辑" |

> Spec 中提到的 "entity-logic-component 架构（即 EntitySystem）" 即本项目已有的一套组合式实体模型：`Entity` 组合多个 `IComponent`（数据）与多个 `ILogic`（行为），由 `EntitySystem` 统一调度。

---

## 2. 总体流程

```
[编辑器]                 [烘焙]                  [运行时]
放置 PoiMono  ----烘焙-->  WorldRegionsConfig  --> 按玩家位置
(场景摆放+配置)          (整表数据资产)            加载 9 格邻域 + 兴趣半径
                                                     │
                                        +--------------+--------------+
                                        ▼                            ▼
                                    激活: 实例化 PoiEntity        失活: 回收 PoiEntity
                                        (Entity)                     (EntitySystem 托管)
                                        │
                                        ▼
                            按 PoiType 注册 Component(数据) + Logic(行为)
```

`PoiMono` 仅存在于**编辑阶段**，用于在场景里摆放与配置；运行时唯一数据源是烘焙生成的 `WorldRegionsConfig`。

---

## 3. 数据结构设计

### 3.1 枚举与 POI 定位

```csharp
/// <summary>八种兴趣点类型。</summary>
public enum PoiType
{
    TeleAnchor,  // 传送锚点
    Statue,      // 七天神像
    Chest,       // 宝箱
    SpiritCore,  // 神瞳
    Gathering,   // 采集物
    Dungeon,     // 副本
    MapBoss,     // 地图 Boss
    MonsterCamp  // 怪物营地
}
```

- **网格坐标**：`cellX = floor(worldPos.x / regionSize)`，`cellY = floor(worldPos.z / regionSize)`。
- **RegionId**：`$"{cellX}x{cellY}"`，如 `1x1`、`12x345`。
- **PoiId**：编辑器写回的 32 位无分隔小写 UUID；与 Region、位置、类型和遍历顺序无关，删除后永不复用。

### 3.2 PoiConfig（兴趣点配置基类，可序列化）

```csharp
[Serializable]
public class PoiConfig
{
    public string       PoiId;             // 不可变 UUID，同步键与服务器数据库主键
    public PoiType      PoiType;           // 八种兴趣点之一
    public Vector3      Position;          // 世界坐标（烘焙时写入，距离过滤必需）
    public bool         aoiExempt;         // 豁免 AOI 裁剪：大体建筑（神像/副本/锚点）常驻，远离不回收
    public StatueConfig       Statue;      // 七天神像
    public TeleAnchorConfig   TeleAnchor;  // 传送锚点
    public ChestConfig        Chest;       // 宝箱
    public SpiritCoreConfig   SpiritCore;  // 神瞳
    public GatheringConfig    Gathering;   // 采集物
    public DungeonConfig      Dungeon;     // 副本
    public MapBossConfig      MapBoss;     // 地图 Boss
    public MonsterCampConfig  MonsterCamp; // 怪物营地
}
```

> 说明：`Position` 为设计补充字段——Spec 未显式列出坐标，但运行时做"兴趣半径距离过滤"与"region 归属"都必须用到，故在此补齐。烘焙阶段写入。

### 3.3 各类型专属 Config（每个类型一个）

```csharp
[Serializable] public class TeleAnchorConfig  { public bool initiallyUnlocked; }
[Serializable] public class StatueConfig      { /* 初始祝福 / 升级曲线等 */ }
[Serializable] public class ChestConfig       { /* 奖励表引用等 */ }
[Serializable] public class SpiritCoreConfig  { /* 所属区域、收集里程碑等 */ }
[Serializable] public class GatheringConfig   { /* 资源类型、掉落表、刷新周期等 */ }
[Serializable] public class DungeonConfig     { /* 关联副本场景、解锁条件等 */ }
[Serializable] public class MapBossConfig     { /* 战斗配置、掉落表、刷新周期等 */ }
[Serializable] public class MonsterCampConfig { /* 敌人配置、掉落、刷新周期等 */ }
```

> Spec 原稿仅列 7 个 Config、缺"地图 Boss"，经确认补齐 `MapBossConfig`。

### 3.4 Region 与整表

```csharp
[Serializable]
public class RegionConfig
{
    public string            RegionId; // "1x1" / "12x345"
    public List<PoiConfig>   Pois;     // 区域内兴趣点列表
}

// 烘焙产物资产
public class WorldRegionsConfig : ScriptableObject
{
    public float          RegionSize; // 区域边长（默认 100m）
    public List<RegionConfig> Regions;
}
```

---

## 4. 编辑器阶段与烘焙

### 4.1 PoiMono（编辑器摆放组件）
场景中每个 POI 挂一个 `PoiMono`，内含一个 `PoiConfig`，供策划就地配置：

```csharp
/// <summary>仅编辑器使用的摆放组件：在大世界场景放置一个 POI 并配置其数据。</summary>
public sealed class PoiMono : MonoBehaviour
{
    [SerializeField] public PoiConfig Config;
}
```

- `PoiMono` 使用 Unity 默认 Inspector，不再注册专用 `CustomEditor`；`PoiConfig` 的通用字段绘制由其 PropertyDrawer 负责。
- **运行时**：此对象不参与逻辑。加载阶段完全基于烘焙数据，运行时场景里不应依赖 `PoiMono`。

### 4.2 烘焙工具（编辑器窗口）
- 配置 `Region Size`（区域边长，默认 `100m`）。
- 点击**烘焙**：扫描场景中所有 `PoiMono`，按 `Position` 计算归属网格，聚合成 `RegionConfig`，写入（或重建）`WorldRegionsConfig` 资产。
- 归属规则：`cellX = floor(x / size)`、`cellY = floor(z / size)`；同一格内的 POI 归入该 Region。POI UUID 不参与网格计算，也不会因布局变化而改变。
- 进入 PlayMode 时 `ServerProcessManager` 自动构建并启动 Go 服务器；服务器异常残留时可使用菜单 `Prometheus/World/Stop POI Server` 停止项目对应的 `Server/bin/server.exe`。

### 4.3 配置字段绘制
`PoiConfigDrawer` 仅负责 `PoiConfig` 属性的通用折叠和类型字段显示，不再通过 `PoiMonoEditor` 接管 `PoiMono` 的 Inspector。

---

## 5. 运行时生命周期

### 5.1 两层加载过滤
加载遵循 Spec 的两层过滤：

1. **区域过滤**：只考虑"玩家所在 Region + 其 3×3 邻域（±1 格，共 9 个 Region）"内的 POI。
2. **兴趣半径过滤**：在这 9 个 Region 内，仅 `distance(玩家, poi) <= InterestRadius` 的 POI 被实例化并执行逻辑。

`InterestRadius` 为**独立配置项**，默认取 `RegionSize * 0.5f`（即格子边长的一半），使激活范围略小于 9 格区域，避免边界 POI 过早/过多加载。

### 5.2 WorldSystem（核心，已实现于 `Gameplay/WorldSystem/WorldSystem.cs`）

```csharp
public sealed class WorldSystem : XSystem
{
    private readonly List<PoiEntity> allPois = new List<PoiEntity>(); // 保存由场景 PoiMono 组合出的全部 POI Entity。
    private readonly Dictionary<string, PoiEntity> poisById = new Dictionary<string, PoiEntity>(); // 按语义 Id 查询当前场景 POI。
    private float tickAccumulator; // 以 0.25 秒间隔执行 AOI 与网络同步。

    public float RegionSize { get; set; } = ChunkIdCodec.ChunkSize; // AOI 网格边长与服务器 chunk 保持一致。
    public float InterestRadius { get; set; } = 15f; // 普通 POI 的世界距离显隐半径。

    public override void AfterNew()
    {
        // 基础模块和玩法系统统一从 Core 获取，不保存或注入 IGameplayKit、IAssetKit、IEventKit。
        Core.Event.AddListener<EntityDiedEvent>(Event.EntityDied, OnEntityDied);
        Core.Asset.LoadAssetSync<WorldMapDefinition>("WorldMapDefinition");
    }

    public override void OnUpdate(float dt) { /* 低频读取 Core.Gameplay.Player 坐标并刷新 POI 显隐与附近 chunk。 */ }
    public void RefreshAt(Vector3 playerPos) { /* 对场景 POI 执行 3x3 邻域、兴趣半径和消费状态判断。 */ }
}
```

要点：
- 静态地图定义经 `Core.Asset` 加载，场景 POI 由 `LoadFromScene` 扫描 `PoiMono` 建立 Entity 与 Id 索引。
- 玩家位置在 `OnUpdate` 中取 `Core.Gameplay.Player`（即 `TeamSystem.ActiveMember`）的 `bindGo` 坐标。
- Entity 注册、敌人生成和营地补刷统一通过 `Core.Gameplay.GetSystem<EntitySystem>()` 完成。

> 当前实现的服务器接入流程以场景 `PoiMono` 为静态定义来源：`AfterNew` 先注册怪物营地死亡监听并生成初始史莱姆，`InitializeAsync` 首先扫描场景并建立本地 POI 索引，保证地图和交互列表可以独立于网络显示；随后创建 `PoiNetworkClient` 并通过 `ConnectAsync` 恢复玩家坐标、启用服务器状态同步和交互请求。`OnUpdate` 先执行营地补刷，再调用 `PumpEvents` 分发 NetworkKit 推送，最后按玩家位置同步 chunk；服务器不可用时保持 `isAvailable=false`，但已扫描的静态 POI 仍保留，待网络恢复后再执行服务器请求。

### 5.3 激活与失活
- **初始化**：`LoadFromScene` 为每个合法 `PoiMono` 创建 `PoiEntity` 或 `NpcEntity`，注册到 EntitySystem 并保留场景 GameObject，不在 AOI 阶段重复实例化或销毁。
- **显隐**：`RefreshAt` 计算 3x3 邻域和兴趣半径，通过场景 GameObject 的 `SetActive` 切换普通 POI 显示；被消费的 POI 始终隐藏。
- **AOI 豁免（`aoiExempt`）**：常驻 POI 无视邻域和兴趣半径，但仍服从消费状态。
- **生命周期**：WorldSystem 释放前，POI Entity 仍由 EntitySystem 统一托管；GameplayKit 销毁时先释放全部 Entity，再逆序释放其他 System。

### 5.4 玩家位置来源
`WorldSystem` 需要玩家世界坐标，经确认**复用 `TeamSystem` 现成成员**，不再另设位置定位器：

```csharp
// 取 Core.Gameplay 当前玩家的世界坐标作为兴趣中心；玩家尚未创建时跳过本轮刷新。
if (Core.Gameplay.Player != null && Core.Gameplay.Player.bindGo != null)
{
    Vector3 playerPos = Core.Gameplay.Player.bindGo.transform.position;
    RefreshAt(playerPos);
}
```

- 切人（`TeamSystem.SwitchToSlot`）后 `ActiveMember` 已切换，下一轮 `Tick` 自动以新成员位置为准，无需额外同步。
- `ActiveMember` 为 `null`（暂无可用成员）时跳过本轮加载，避免空引用。

---

## 6. POI 实体（entity-logic-component）

### 6.1 PoiEntity（Entity 子类）

```csharp
/// <summary>一个兴趣点的运行时实体：绑定实例化的 POI 预制体，组合 PoiComponent(数据) 与按类型选择的 Logic(行为)。</summary>
public class PoiEntity : Entity
{
    // 由 WorldSystem 已实例化预制体后构造（与 PlayerEntity/SlimeEntity 的 bindGameObject 模式一致）
    public PoiEntity(GameObject bindGameObject, PoiConfig config)
    {
        bindGo = bindGameObject;                                  // 绑定场景表现对象
        AddComp(new PoiComponent { Config = config });            // 数据组件
        AddLogic(PoiLogicFactory.Create(config.PoiType));         // 行为逻辑按类型注入
    }
}
```

### 6.2 PoiComponent（数据组件）

```csharp
/// <summary>承载一个 POI 的运行时数据（源自烘焙的 PoiConfig）。</summary>
public class PoiComponent : IComponent
{
    public Entity    Entity { get; set; }
    public PoiConfig Config { get; set; }
}
```

### 6.3 各类型 Logic
- 统一行为接口由 `ILogic` 提供；以 **`PoiLogic` 基类 + 各类型子类** 表达差异。`PoiLogic` 提供读取 `Poi`/`Config` 的入口与默认空实现（在 `PoiLogic.cs`）。
- 可刷新三类（采集物/地图Boss/怪物营地）共用 **`RespawnablePoiLogic`** 基类：`Consume()` 后按 `Config` 配置的重生周期倒计时，`OnUpdate` 到期自动重生。
- 各类型由 `PoiLogicFactory` 创建（`PoiLogicFactory.cs`）；关键状态变化经静态事件总线 `EventHandler<T>` 广播（`PoiEvents.cs`：`PoiOpenedEvent`/`PoiCollectedEvent`/`PoiGatheredEvent`/`PoiUnlockedEvent`/`PoiDefeatedEvent`）。

| 类型 | 公开行为 | 状态 | 广播事件 |
|------|---------|------|---------|
| `TeleAnchorLogic` | `Unlock()` | `IsUnlocked` | `PoiUnlockedEvent` |
| `StatueLogic` | `Unlock()` / `Upgrade()` | `IsUnlocked` / `Level` | `PoiUnlockedEvent` |
| `DungeonLogic` | `Unlock()` / `Advance()` | `IsUnlocked` / `Progress` | `PoiUnlockedEvent` |
| `ChestLogic` | `Open()`（幂等） | `IsOpened` | `PoiOpenedEvent` |
| `SpiritCoreLogic` | `Collect()`（幂等） | `IsCollected` | `PoiCollectedEvent` |
| `GatheringLogic` | `Gather()` | `Available`（重生） | `PoiGatheredEvent` |
| `MapBossLogic` | `Defeat()` | `Available`（重生） | `PoiDefeatedEvent` |
| `MonsterCampLogic` | 暂无交互 | 营地实体由 `WorldSystem` 维护 | 史莱姆死亡由 `Core.Event` 广播 |

> 业务数据（奖励表、战斗、传送目标、副本场景）依赖尚未存在的奖励/战斗/传送系统，此处仅实现**生命周期状态、幂等切换、重生计时与事件广播**；真实业务在 `ChestLogic.Open()` 等公开方法处接入。

> 与现有 `EntitySystem.CreateEnemies` 的套路一致：构造 `Entity` → `AddComp` → `AddLogic` → `AddEntity` → `AfterNew`。`PoiEntity` 复用同样的生命周期，天然获得 EntitySystem 的逐帧调度与安全回收。

---

## 7. 各类型差异对照

| PoiType | 专属 Config | 注入 Logic | 生命周期特征 |
|---------|------------|-----------|-------------|
| TeleAnchor | `TeleAnchorConfig` | `TeleAnchorLogic` | 解锁后常驻 |
| Statue | `StatueConfig` | `StatueLogic` | 解锁后常驻 |
| Chest | `ChestConfig` | `ChestLogic` | 一次性开启 |
| SpiritCore | `SpiritCoreConfig` | `SpiritCoreLogic` | 一次性收集 |
| Gathering | `GatheringConfig` | `GatheringLogic` | 可刷新（重生） |
| Dungeon | `DungeonConfig` | `DungeonLogic` | 解锁 + 进度 |
| MapBoss | `MapBossConfig` | `MapBossLogic` | 可刷新（重生） |
| MonsterCamp | `MonsterCampConfig` | `MonsterCampLogic` | `WorldSystem` 按每个场景营地实例生成一只史莱姆；史莱姆首次致死后通过 `Core.Event` 通知并在同帧安全阶段于原营地位置补刷一只 |

> 采集物与地图 Boss 通过共享基类 `RespawnablePoiLogic` 处理周期重生；怪物营地的史莱姆由 `WorldSystem` 监听全局死亡事件后立即补刷。POI 重生属逻辑层，距离显隐仍由 `WorldSystem` 管理。

---

## 8. 关键流程 / 时序

```
玩家移动 -> WorldSystem.OnUpdate（低频，默认 0.25s 一跳）
  1. 取 Core.Gameplay.Player（当前上场成员）位置
  2. RefreshAt(playerPos)：cell = floor(playerPos / RegionSize)，9 格邻域（±1）
  3. 遍历场景已注册的 PoiEntity：
     - 离开 9 格邻域、超出兴趣半径或已经消费：GameObject.SetActive(false)
     - 位于邻域和兴趣半径内，或配置 aoiExempt：GameObject.SetActive(true)
  4. 根据玩家所在 chunk 拉取附近 3x3 chunk 的服务器状态
```

---

## 9. 边界情况与权衡

1. **烘焙数据唯一权威**：运行时场景里不依赖 `PoiMono`，改配置只需重新烘焙，不动场景摆放，降低耦合。
2. **距离过滤的必要性**：`Position` 虽 Spec 未列，但兴趣半径过滤和 region 归属都必须用它，属必要补充。
3. **差量更新**：跨格时才重算，避免每帧遍历全部 POI；活跃 POI 数量始终≈兴趣半径内，可控。
4. **帧内安全回收**：失活用 `RequestDispose` 走 EntitySystem 安全边界，避免在遍历集合时删除导致的问题（与 `EntitySystem` 既有 `isUpdatingEntities` 保护一致）。
5. **玩家位置解耦**：不直接持有玩家对象，通过 GameplayKit 解析，便于单测与替换。

---

## 10. 落地步骤（建议顺序）

| 阶段 | 内容 | 状态 |
|------|------|------|
| **P0 数据** | `PoiType`、`PoiConfig` + 8 个类型 Config、`RegionConfig`、`WorldRegionsConfig` | ✅ 已实现 |
| **P1 烘焙** | `PoiMono` + 编辑器窗口 + `PoiConfig` 属性绘制 | ✅ 已实现 |
| **P2 WorldSystem** | 加载烘焙数据、region 邻域 + 兴趣半径、POI 激活/失活差量 | ✅ 已实现 |
| **P3 PoiEntity** | `PoiEntity` + `PoiComponent` + `PoiLogic` 基类 + 工厂 | ✅ 已实现 |
| **P4 逐类型** | 8 个 Logic（状态/幂等/重生 + 事件广播） | ✅ 已实现 |

> 已全部实现并在 MainWorld 场景的测试烘焙数据上验证（激活/回收、正确挂载 Logic、各类型状态与重生、事件广播）。真实业务（奖励/战斗/传送/副本场景）待对应系统接入。

---

## 11. 已确认的接入决策

| 项 | 结论 | 落点 |
|----|------|------|
| `WorldRegionsConfig` 加载 | 走现有 `IAssetKit` / YooAsset **地址化加载**（`LoadAssetSync<WorldRegionsConfig>(location)`） | §5.2 `AfterNew` |
| 玩家位置取用 | 复用 `TeamSystem.ActiveMember` 的 `bindGo` 世界坐标，不新增定位器 | §5.4 |
| 可刷新类重生 | 通过共享基类 `RespawnablePoiLogic` 实现（`Consume` → 按 `Config` 周期倒计时 → 自动重生），**不抽独立 `RespawnScheduler`** | §6.3 各类型 Logic |

## 12. 后续 / 待实现时对齐

- `WorldSystem` 已接入 `GameplayKit.RegisterGameplaySystems`（`GameplayKit.cs`），随玩法开局自动注册。烘焙资产需纳入 YooAsset 包内地址 `Assets/Prometheus/Gameplay/WorldSystem/Data/WorldRegionsConfig.asset`；缺失时降级为空世界并告警。
- 各类型真实业务（奖励、战斗、传送目标、副本场景）在对应 `Logic` 公开方法处接入，依赖奖励/战斗/传送系统就绪。
- 交互触发入口（玩家接近/按键/射线检测调 `Open()`/`Collect()`/`Gather()` 等）不在本 Spec 范围，待交互层接入。
- `WorldRegionsConfig` 的 YooAsset 地址（`location`）命名，随烘焙工具的资产输出约定确定。
- 8 个类型 Logic 的业务细节（奖励、战斗、传送、副本进入）在各自实现中展开，不在本文档范围。

## 13. 地图数据接口

`WorldSystem` 同时是当前世界地图的唯一运行时数据出口。`AfterNew` 通过 AssetKit 读取 `WorldMapDefinition`，对外提供 `MapTexture`、`MapWorldLength`、`MapWorldWidth`、`MapInitialZoom`、`MapZoom`、`WorldToMapNormalized`、`TryGetPlayerPosition` 和只读 `AllPois`；HUD 小地图和 `MapPanel` 不直接访问网络客户端或 POI 内部逻辑。

地图相关变化通过 `Core.Event` 发布：`WorldMapReady` 表示地图资源已解析，`WorldMapPoiChanged` 表示 POI 集合或状态变化。玩家坐标不通过高频事件传递，HUD 和 `MapPanel` 各自逐帧读取当前玩家实体的 `bindGo.transform.position`；AOI 刷新和网络同步仍按低频 tick 执行。大地图滚轮缩放以当前指针所在的视口点为锚点，缩放前后保持该地图点位于相同屏幕位置。地图纹理由编辑器菜单 `Prometheus/World/Map Capture` 生成，运行时不再创建俯拍相机和 RenderTexture。

大地图的传送请求也由 `WorldSystem.TryTeleportToPoi` 统一处理。该接口只接受当前已加载且类型为 `Statue` 或 `TeleAnchor` 的 POI；传送时暂时停用玩家 `CharacterController`、写入目标位置并清空移动速度与 Root Motion，避免下一帧运动逻辑覆盖传送结果。随后 HUD 和大地图直接读取同一份实体 Transform，保证两个视图继续使用同一份世界坐标事实。
### NPC POI（第一阶段）

当 `PoiConfig.PoiType` 为 `Npc` 时，WorldSystem 使用 `PoiConfig.Npc` 创建 `NpcEntity`，仍沿用 POI 的 AOI 激活、EntitySystem 注册和安全回收链路。NPC 身份与行为由 NpcSystem 的 `NpcDefinition`、`NpcComponent` 和 `NpcLogic` 提供；WorldSystem 不直接控制对话、镜头或任务推进。
