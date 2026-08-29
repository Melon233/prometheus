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
- **PoiId**：`$"{RegionId}_{局部id}"`，局部 id 从 1 开始，如 `1x1_1`、`12x345_1`。

### 3.2 PoiConfig（兴趣点配置基类，可序列化）

```csharp
[Serializable]
public class PoiConfig
{
    public string       PoiId;             // 唯一 id，网格坐标 + 局部 id，如 "1x1_1"
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

- 自定义 Inspector：根据 `Config.PoiType` 显示对应类型的 Config 字段（见 4.3）。
- **运行时**：此对象不参与逻辑。加载阶段完全基于烘焙数据，运行时场景里不应依赖 `PoiMono`。

### 4.2 烘焙工具（编辑器窗口）
- 配置 `Region Size`（区域边长，默认 `100m`）。
- 点击**烘焙**：扫描场景中所有 `PoiMono`，按 `Position` 计算归属网格，聚合成 `RegionConfig`，写入（或重建）`WorldRegionsConfig` 资产。
- 归属规则：`cellX = floor(x / size)`、`cellY = floor(z / size)`；同一格内的 POI 归入该 Region，局部 id 按格内顺序从 1 递增。

### 4.3 类型分面编辑器
Inspector 依据 `PoiType` 切换显示对应的 Config 面板（`Statue`/`TeleAnchor`/`Chest`/`SpiritCore`/`Gathering`/`Dungeon`/`MapBoss`/`MonsterCamp` 之一），保证一次只编辑一个类型的数据，避免误填。

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
    private WorldRegionsConfig  baked;            // 烘焙数据（运行时唯一数据源）
    private Dictionary<string, RegionConfig> byRegion; // RegionId -> Region
    private Dictionary<string, PoiConfig>    poiById;  // PoiId -> PoiConfig
    private Dictionary<string, PoiEntity>    activePois; // 已实例化的 POI 实体
    private float tickAccumulator;              // 低频驱动（默认 0.25s 一跳）

    public float RegionSize => baked != null ? baked.RegionSize : 100f;
    public float InterestRadius { get; set; } = 50f;   // 加载后默认 RegionSize * 0.5f
    public int   ActiveCount => activePois.Count;      // 诊断
    public IEnumerable<string> ActivePoiIds => activePois.Keys; // 诊断/测试

    public override void AfterNew(IGameplayKit ownerGameplayKit)
    {
        gameplayKit = ownerGameplayKit;
        // 经 AssetKit.Ins 地址化加载烘焙数据；缺失时降级为空世界并告警。
        // 测试/编辑器场景可绕过此加载，直接调用 LoadBaked(config)。
    }

    public void LoadBaked(WorldRegionsConfig config) { /* 建 byRegion / poiById 索引，初始化 InterestRadius */ }
    public override void OnUpdate(float dt) { /* 低频驱动，取 gameplayKit.Player 位置调用 RefreshAt */ }
    public void RefreshAt(Vector3 playerPos) { /* 9 格邻域 + 兴趣半径，差量激活 / 回收 */ }
}
```

要点：
- 烘焙加载（`AfterNew` 经 `AssetKit.Ins`）与核心刷新（`LoadBaked` / `RefreshAt`）**分离**，便于独立测试。
- 玩家位置在 `OnUpdate` 中取 `gameplayKit.Player`（即 `TeamSystem.ActiveMember`）的 `bindGo` 坐标。

> 当前实现的服务器接入流程以场景 `PoiMono` 为静态定义来源：`AfterNew` 先注册怪物营地死亡监听并生成初始史莱姆，再创建 `PoiNetworkClient`，通过 `ConnectAsync` 确认 POI 服务器可用后扫描场景并启用 AOI/交互逻辑。`OnUpdate` 先执行营地补刷，再调用 `PumpEvents` 分发 NetworkKit 推送，最后按玩家位置同步 chunk；服务器不可用时保持 `isAvailable=false`，不发起后续 POI 请求。

### 5.3 激活与失活
- **激活**：`RefreshAt` 计算 9 格邻域，候选 POI 满足"在 9 格内 + 在兴趣半径内"且尚未实例化 → 经 `AssetKit.Ins.InstantiateSync("Poi_<Type>", position)` 实例化对应预制体、把 `PoiId + PoiType` 写入 Label，再 `new PoiEntity(go, config)` → `EntitySystem.AddEntity` + `AfterNew`，记入 `activePois`。回收时 `RequestDispose` 会销毁绑定的场景对象。
- **失活**：POI 移出 9 格邻域或移出兴趣半径 → 从 `activePois` 移除并 `entity.RequestDispose()`（内部走 `EntitySystem.RequestRemoveEntity` 安全边界，下一帧排水）。
- **AOI 豁免（`aoiExempt`）**：`PoiConfig.aoiExempt` 为 true 的 POI（大体建筑：七天神像/副本/传送锚点）**常驻**——无视 9 格邻域与兴趣半径始终实例化，且永不回收（`LoadBaked` 时收入 `persistentPois` 列表）。
- **差量策略**：`RefreshAt` 每跳重算期望 9 格集合，仅对"新增/移出"的 POI 做创建/回收，避免每帧全量遍历。

### 5.4 玩家位置来源
`WorldSystem` 需要玩家世界坐标，经确认**复用 `TeamSystem` 现成成员**，不再另设位置定位器：

```csharp
// 取当前上场成员的世界坐标作为兴趣中心（TeamSystem.ActiveMember 为空时跳过本轮加载）
if (gameplayKit.TryGetSystem(out TeamSystem teamSystem) && teamSystem.ActiveMember != null)
{
    Vector3 playerPos = teamSystem.ActiveMember.bindGo.transform.position;
    Tick(playerPos);
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
  1. 取 gameplayKit.Player（当前上场成员）位置
  2. RefreshAt(playerPos)：cell = floor(playerPos / RegionSize)，9 格邻域（±1）
  3. 差量：
     - 离开 9 格邻域或超出兴趣半径的 POI：RequestDispose()
     - 邻域内未激活且位于兴趣半径内的 POI：创建 PoiEntity
       -> EntitySystem.AddEntity -> PoiEntity.AfterNew()（各类型 Logic 开始跑）
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
| **P1 烘焙** | `PoiMono` + 编辑器窗口 + 类型分面 Inspector | ✅ 已实现 |
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
