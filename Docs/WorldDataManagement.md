# 大世界世界数据管理设计

> 适用范围：单一大场景（无缝开放世界）· 需要玩家存档 · 数百到一千个 POI
> 涉及对象：传送锚点、七天神像、宝箱、神瞳、采集物、副本、地图 Boss、怪物营地

---

## 1. 目标与约束

### 1.1 目标
- 用**一套统一框架**承载 8 类 POI，同时让每类的差异化逻辑能**独立扩展**，不写 `switch(Type)` 地狱。
- **静态定义**（摆什么、在哪、给什么）与**运行时状态**（是否已开、下次刷新时间）彻底分离，满足存档。
- 空间查询高效（范围取最近 POI、地图图标批量查询），满足无缝大场景 + 数百到千级数量。
- 策划可**在编辑器里摆点并填写配置**，不需要写代码。

### 1.2 约束（本项目现状）
- 无现成存档/序列化系统 —— 需要**自建一套轻量持久化接口**（见 §7）。
- 已具备 `Gameplay/Entity`（实体）、`Gameplay/Config`（配置）、`Gameplay/Logic`（逻辑）、`Gameplay/Minimap`（小地图）、`Framework/*`（CoreKit/EventKit/AssetKit）等基础模块 —— 新模块应**挂靠这些体系**而非另起炉灶。
- 数量级数百到一千：**无需**重型 BVH，用**规则格网 + 懒遍历**即可。

---

## 2. 核心概念：把"一个世界点"拆成三样东西

每个世界点（World Point）在代码里有三个正交的身份，必须**分开建模**：

| 概念 | 是什么 | 存哪 | 生命周期 |
|------|--------|------|---------|
| **Definition（定义）** | 不变配置：类型、位置、奖励表、刷新周期、图标 | ScriptableObject 资产 | 永久 |
| **Instance（实例）** | 场景里摆放的运行时对象，承载表现与交互 | 场景 GameObject | 随场景 |
| **State（状态）** | 可变数据：是否已解锁/已开启、当前等级、下次刷新时间 | 存档 | 随玩家 |

> 铁律：**只持久化 State，永不持久化场景摆放或 Definition**。场景里永久摆好所有 POI，存档里只记 `id → state`。

---

## 3. 类型与生命周期归组

先把 8 类归到 3 个行为族，每族共享一套机制：

| 行为族 | 成员 | 状态模型 | 刷新？ |
|--------|------|---------|--------|
| **一次性解锁** | 传送锚点、七天神像、副本 | 解锁等级 / 解锁 bool（进度另存） | 否 |
| **一次性收集** | 宝箱、神瞳 | 已开启 / 已收集 bool | 否 |
| **可刷新资源** | 采集物、地图 Boss、怪物营地 | 当前可用状态 + `nextRespawnTime` | **是** |

三个族共同点：都有 `id`、`位置`、`激活状态`、`交互入口`。差异点正好对应上面三族，用**策略/组件注入**承载。

---

## 4. 模块与命名空间规划（贴合现有目录）

新增目录挂在 `Assets/Prometheus/Gameplay/World/` 下，程序集归入现有的 Gameplay 体系：

```
Assets/Prometheus/Gameplay/World/
├── Definition/          # 静态定义 SO
│   ├── WorldPointDefinition.cs
│   ├── WorldPointType.cs
│   ├── ChestDefinition.cs
│   ├── BossDefinition.cs
│   └── CollectibleDefinition.cs
├── Instance/            # 运行时世界点
│   ├── WorldPoint.cs            # 统一根组件（挂场景对象上）
│   ├── WorldPointState.cs       # 可变状态（可序列化）
│   └── Interact/                 # 交互策略（每种交互一个组件）
│       ├── IInteractable.cs
│       ├── TeleportInteract.cs
│       └── ChestInteract.cs
├── Manager/             # 注册表 / 查询 / 刷新
│   ├── WorldDataManager.cs
│   ├── WorldPointRegistry.cs
│   ├── RespawnScheduler.cs
│   └── WorldSpatialIndex.cs
├── Save/                # 存档
│   ├── IWorldStateStore.cs
│   └── WorldSaveData.cs
└── Editor/              # 编辑/作者工具
    └── WorldPointEditorWindow.cs
```

---

## 5. 核心类设计

### 5.1 统一实例组件 `WorldPoint`
场景里每个 POI 的 GameObject 上**只挂这一个根组件**（外壳），差异靠子组件注入：

```csharp
public sealed class WorldPoint : MonoBehaviour
{
    [SerializeField] string               definitionId; // 关联 Definition
    [SerializeField] WorldPointState      state;        // 运行时可变状态（非存档用，临时）
    [SerializeField] MonoBehaviour        interaction;  // 注入的交互策略（需实现 IInteractable）

    public string         Id         => definitionId;
    public WorldPointType Type       => Definition.Type;
    public WorldPointDefinition Definition => ConfigLoader.Get(definitionId);

    // 由 WorldPointRegistry 在 Awake 时调用
    internal void Register(WorldPointRegistry registry);
    internal void ApplyState(WorldPointSaveState saved); // 从存档恢复
    internal WorldPointSaveState CaptureState();          // 供存档写入
}
```

### 5.2 状态模型 `WorldPointState`（可序列化）
```csharp
[Serializable]
public sealed class WorldPointSaveState
{
    public string          id;
    public WorldPointType  type;
    public bool            unlocked;      // 解锁类：锚点/神像/副本入口
    public int             level;         // 神像供奉等级 / 副本进度位
    public bool            consumed;      // 一次性收集：宝箱已开/神瞳已收
    public long            nextRespawnTicks; // 可刷新类：下次可用的世界时间（Unix ms）
}
```
> 统一一个扁平 state 即可覆盖三族；个别字段闲置（如一次性对象不填 `nextRespawnTicks`）。

### 5.3 交互策略 `IInteractable`
```csharp
public interface IInteractable
{
    bool   CanInteract(WorldPoint self);
    void   Interact(WorldPoint self, object interactor);
    string GetPrompt(WorldPoint self); // 提示文本
}
```
- 锚点 → `TeleportInteract`（解锁 + 传送到传送点）
- 神像 → `StatueInteract`（回复 + 供奉 + 切祝福）
- 宝箱 → `ChestInteract`（给奖励 + 标记 consumed）
- 副本 → `InstanceInteract`（校验解锁 + 进入副本场景）
- 采集物/Boss/营地 → 各自交互，并实现 `IRespawnable`

### 5.4 注册表与空间索引 `WorldPointRegistry` + `WorldSpatialIndex`
```csharp
public sealed class WorldPointRegistry
{
    Dictionary<string, WorldPoint>             _byId;     // id → 实例（存档恢复用）
    Dictionary<WorldPointType, List<WorldPoint>> _byType; // 类型索引
    WorldSpatialIndex                          _spatial;  // 格网空间索引
    Dictionary<string, List<WorldPoint>>       _byRegion; // 区域索引

    IEnumerable<WorldPoint> QueryNear(Vector3 pos, float radius, WorldPointType? type = null);
    IEnumerable<WorldPoint> QueryRegion(string regionId);
    WorldPoint GetById(string id);
    void RefreshAllStatesFromSave(WorldSaveData save); // 一次性从存档恢复
}
```

**空间索引策略**（数百~千级）：
- 一张**固定格网**（cell 尺寸取交互半径，如 50m）。POI 只在 Awake 注册进所属 cell。
- 范围查询 = 取包围的 cell 集合 + 精确距离过滤。O(cell 数)，远优于全遍历。
- 不必引入复杂结构，千级规模足够。

### 5.5 刷新调度器 `RespawnScheduler`
统一管理所有"可刷新"对象（采集物、Boss、营地），**时间驱动**，避免每类自己写计时器：

```csharp
public sealed class RespawnScheduler : MonoBehaviour
{
    void Tick(long nowUtcMs)
    {
        // 惰性检查：只在每次世界时钟变化时，把 nextRespawnTicks <= now 的对象重新激活
        foreach (var respawnable in _dueSet.GetDue(nowUtcMs))
            respawnable.Respawn();
    }
}
```
- 由 `WorldDataManager.Tick`（或现有 EventKit 的时间事件）驱动。
- **重点：离线重生**。`nextRespawnTicks` 直接存绝对世界时间（Unix ms），而非"距离上次 xx 秒"。这样玩家 3 天后回来，Boss/采集物自动判定已重生，无需离线模拟。

---

## 6. 各类 POI 的差异化落点（对照表）

| 类型 | Definition 里的专属字段 | 交互策略 | 状态字段 | 刷新机制 |
|------|------------------------|----------|---------|---------|
| 传送锚点 | 锚点图标、是否初始解锁 | `TeleportInteract` | unlocked | — |
| 七天神像 | 初始祝福、升级曲线 | `StatueInteract` | unlocked, level | — |
| 宝箱 | 奖励表引用 | `ChestInteract` | consumed | — |
| 神瞳 | 所属区域、收集奖励里程碑 | `OculusInteract` | consumed | —（进度由 Progression 汇总） |
| 采集物 | 资源类型、掉落表、刷新周期 | `CollectInteract` | nextRespawnTicks | RespawnScheduler |
| 副本 | 入口位置、关联副本场景、解锁条件 | `InstanceInteract` | unlocked, level | — |
| 地图 Boss | 战斗配置、掉落表、刷新周期 | `BossInteract` | nextRespawnTicks | RespawnScheduler |
| 怪物营地 | 敌人配置、掉落、刷新周期 | `CampInteract` | nextRespawnTicks | RespawnScheduler |

> 神瞳/宝箱这类"收集进度"：**State 只记单个 POI 是否已收**，总数/里程碑统计放在 `Growth`/`Progression` 模块汇总，不在 World 模块重复记账。

---

## 7. 存档方案

项目暂无存档系统，这里**只定义 World 模块需要的接口**，实现可挂到现有存档模块或 EventKit：

```csharp
// 由存档系统实现，World 模块只依赖它读写自己那一段
public interface IWorldStateStore
{
    void WriteWorldState(WorldSaveData data);
    WorldSaveData ReadWorldState();
}

[Serializable]
public sealed class WorldSaveData
{
    public string                       version;
    public List<WorldPointSaveState>    points;
}
```

**存哪些：**
- 一次性收集 → `consumed` bool
- 可刷新 → `nextRespawnTicks`
- 永久解锁 → `unlocked` / `level`
- 存档里存 **`id`**，不存场景路径/索引 —— 否则删改配置会错位。

**恢复流程（进游戏）：**
1. `WorldPointRegistry` 遍历场景里所有 `WorldPoint`，建立 id 索引。
2. 从 store 读出 `WorldSaveData`，按 `id` 匹配，把 state 写回对应实例。
3. 存档里存在但场景没有的 id → 忽略（配置已删）；场景有但存档没有 → 用默认态（新内容）。

---

## 8. 编辑器 / 作者工具

为了让策划不写代码即可布点：
- **WorldPoint 一键挂载工具**：选中空 GameObject → "Add World Point"，选类型自动注入对应 Definition 与交互策略。
- **批量摆放**：在场景里框选/复制多个 WorldPoint，统一生成唯一 `id`。
- **校验窗口**：检查重复 id、缺失 Definition、坐标越界、刷新周期为负。
- **小地图联动**：`Minimap` 模块按 `QueryNear` 结果 + 类型图标渲染标记，State 变化（如宝箱开启）通过 EventKit 广播，图标自动切换。

---

## 9. 关键决策与权衡

1. **为什么用一个扁平 `WorldPointSaveState` 而不是每类一个存档类？**
   简化存档与恢复逻辑；字段冗余可接受（一次性对象不填刷新时间）。等类型膨胀再拆。

2. **为什么交互用策略组件而不是继承？**
   "宝箱"和"神瞳"逻辑几乎一样（都是收一次），继承会让类层次膨胀。策略组合更灵活，还能复用（如某宝箱也可当采集物）。

3. **为什么刷新存绝对时间而非"距上次秒数"？**
   天然支持离线重生，无需离线计时模拟。

4. **为什么只存 State 不存场景摆放？**
   场景是权威（source of truth），存档只是状态覆盖层，改配置不破坏存档。

5. **空间索引用格网而非 BVH？**
   千级 POI 格网足够，实现简单、编辑器友好。若未来上万再换 BVH（接口已抽象在 `WorldSpatialIndex` 后）。

---

## 10. 落地步骤（分阶段）

| 阶段 | 内容 | 产出 |
|------|------|------|
| **P0 骨架** | `WorldPointType`、`WorldPointDefinition`、`WorldPoint`、`WorldPointRegistry` | 通用框架可跑 |
| **P1 三类机制** | `IInteractable` 策略 + `RespawnScheduler` + 空间索引 | 一次性/可刷新/解锁三类能力齐全 |
| **P2 逐个类型** | 8 类 Definition + 各自交互策略 + Editor 工具 | 所有类型可摆放 |
| **P3 存档** | `IWorldStateStore` + 恢复流程 + 事件广播 | 重进游戏状态保留 |
| **P4 联动** | Minimap 图标、Progression 汇总、副本进入 | 完整闭环 |

建议**按 P0 → P3 顺序先打通"一个宝箱 + 一个 Boss"的最小闭环**，验证架构后再铺满全部类型。

---

## 11. 待确认 / 后续问题

- 现有 `Gameplay/Entity` 与 `Gameplay/Logic` 的实体体系是否该作为 WorldPoint 的表现层载体，还是 WorldPoint 独立？（影响 Instance 层如何与现有实体/角色交互）
- `Growth`/`Progression` 模块的奖励发放接口是什么，用于宝箱/神像/神瞳的奖励落点。
- 存档系统最终挂在哪（是否有计划中的 SaveManager），`IWorldStateStore` 的具体实现归属。
