# GameplayKit 与 ELC 架构

## 文档状态

本文描述 GameplayKit、Entity、Logic、Component 与 Unity GameObject 的当前架构。PlayerEntity、SlimeEntity 及其四个正式 Prefab 已完成根 Binder 和纯 C# ELC 迁移；`Entity.bindGo` 暂时作为旧 Logic 的只读兼容访问面保留，但实例化、绑定和释放责任已经归属 `GameObjectLogic`。

## 核心决策

运行时实例统一采用 ELC：Entity 表示身份与生命周期，Component 保存纯 C# 状态，Logic 实现实例行为。需要 Unity GameObject 的 Entity 自己声明并拥有表现对象，通过 `GameObjectLogic + GameObjectComponent + Prefab 根 Binder` 完成实例化、引用绑定和释放，外部不再先创建 GameObject 再注入 Entity。

3D Prefab 与 UI Prefab 采用相同的“根节点集中引用”原则，但不复用 UIKit 的自动绑定和代码生成管线：

- 每个可生成的 Entity Prefab 根节点必须且只能挂载一个强类型 Binder。
- Binder 中的引用由开发者在 Inspector 手动维护。
- 运行时只允许 `GameObjectLogic` 在根节点读取一次 Binder，不允许各 Logic 分散调用 `GetComponent` 或 `GetComponentInChildren`。
- 编辑器可以提供引用完整性校验，但不得自动扫描、自动填充或生成业务绑定代码。

## 所有权树

```text
Core
└─ GameplayKit
   └─ EntitySystem
      └─ Entity
         ├─ GameObjectComponent
         │  ├─ GameObject Instance
         │  ├─ EntityBinder Binder
         │  ├─ SpawnSpec
         │  └─ Ownership / ReleaseHandle
         ├─ 纯 C# Gameplay Components
         └─ Logics
            ├─ GameObjectLogic
            └─ Gameplay Logics
```

Entity 拥有 GameObject 生命周期，`Core.Asset` 负责实际资源实例化，EntitySystem 负责生成事务、EntityId、调度和回滚。外部只提交 Entity 类型与出生参数，不接触 Prefab 实例和内部绑定引用。

## 职责划分

| 对象 | 负责 | 不负责 |
| --- | --- | --- |
| `EntitySystem` | 接收生成请求，编排 Entity 创建、EntityId 分配、注册、初始化、更新、回收和失败回滚 | 选择具体 Prefab 内部组件、逐项 `GetComponent`、保存角色表现引用 |
| `Entity` | 声明自身 Component、Logic、表现规格和完整生命周期 | 对外暴露半成品 GameObject 组装步骤 |
| `GameObjectComponent` | 保存实例、Binder、出生规格、所有权和释放句柄 | 玩法流程、逐帧 Update、Unity 组件搜索 |
| `GameObjectLogic` | 通过 `Core.Asset` 创建或接管对象、读取根 Binder、绑定目标 ColliderProxy 的宿主、初始化既有 Binder 感知 Component 并在最后释放 | 动态增加 Component、保存角色数值、技能状态或跨 Entity 业务规则 |
| 纯 C# Component | 保存该 Entity 的运行时状态和配置投影 | 继承 MonoBehaviour、依赖 Unity 生命周期函数 |
| Gameplay Logic | 读取纯 C# Component 和 Binder 能力，执行实例行为 | 自行搜索层级、加载 Prefab、销毁根 GameObject |
| Prefab Binder | 集中保存 Prefab 内 Unity 组件、子节点和配置资产引用 | 运行时业务状态、Update、事件编排、服务查询 |

## Binder 设计约束

### 强类型而非万能字典

Binder 必须按稳定实体族定义强类型字段，例如共享角色引用放入 `CharacterBinder`，玩家专属引用放入 `PlayerBinder`，怪物专属引用放入 `SlimeBinder`。禁止使用字符串到 `UnityEngine.Component` 的运行时字典模拟 Service Locator，也禁止让 Logic 依赖绑定表索引。

共享 Logic 应依赖明确的绑定能力或共享 Binder 基类。例如移动 Logic 只依赖 CharacterController 和根 Transform，不能为了复用而接收完整 PlayerBinder。实体专属 Logic 可以读取具体 Binder 类型。

### Binder 允许保存的内容

- Unity 组件引用，例如 CharacterController、SkeletonAnimation、Collider、AudioSource 和粒子组件。
- 子节点 Transform、碰撞盒、特效槽位和挂点引用。
- Prefab 作者配置的只读 ScriptableObject 或其他 Unity 资产引用。
- Unity 回调桥接组件引用，例如碰撞、动画事件和 Root Motion 适配器。

### Binder 禁止保存的内容

- HP、能量、冷却、Buff、AI 状态、装备状态等运行时业务数据。
- Entity、GameplayKit、System 或其他全局服务引用。
- 业务 Update、技能执行、伤害结算和跨模块事件编排。
- 通过 Awake、Start 或运行时扫描自动补齐的隐藏引用。

Binder 是序列化引用容器，不是新的聚合业务 MonoBehaviour。

## MonoComponent 迁移原则

`MonoComponent` 同时承担 Unity 序列化载体和 ELC 状态节点，会造成组件散落、Entity 构造期重复 `GetComponent`、生命周期来源不一致。当前实现已经删除 `MonoComponent` 基类，所有 ELC Component 继承纯 C# `Component`。

原 MonoComponent 必须按“运行态与 Unity 引用”拆分，不能把整个类原样搬进 Binder：

| 当前类型 | 目标纯 C# Component | Binder 或 Unity 适配引用 |
| --- | --- | --- |
| `PropertyComponent` | HP、属性修正、控制状态和监听字段 | PropertyConfig 等只读配置引用 |
| `MotionComponent` | 速度、接地状态、Root Motion 增量 | CharacterController、根 Transform、Root Motion 桥接器 |
| `SpineComponent` | 播放会话、Owner、优先级和结束状态 | SkeletonAnimation、AnimationLibrary |
| `AttackComponent` | 当前攻击段、命中状态和天赋运行态 | ColliderProxy、命中挂点、TalentConfig |
| `EffectComponent` | EffectSystem 绑定、触发句柄和活动效果快照 | 无必须的 MonoBehaviour 身份 |
| `CharaLevelComponent`、`EquipmentComponent`、`WeaponComponent` | 等级、经验、装备和武器运行态 | 对应只读配置资产 |
| `EnemyAiComponent` | AI 状态、目标和计时数据 | 感知点、导航或表现侧 Unity 引用 |

Unity 引擎要求使用 MonoBehaviour 接收的回调桥接器可以保留，例如碰撞转发、动画事件和 Root Motion 回调；它们不实现 `IComponent`，只把 Unity 事件转交给 Entity Logic 或纯 C# Component。

## 生成与初始化时序

不为 Entity 增加表现对象专用生命周期。`GameObjectLogic` 与其他 Logic 一样在 Entity 构造函数中通过 `AddLogic` 注册，并统一参与既有 `AfterNew`、`OnDispose` 协议；区别仅在于它必须第一个初始化，从而在其他 Logic 初始化前准备好 Binder：

```text
Entity 构造
  → AddComp<GameObjectComponent>()
  → AddComp<其他纯 C# Component>()
  → AddLogic<GameObjectLogic>()，作为第一个 Logic 注册
  → AddLogic<其他 Gameplay Logic>()
  → EntitySystem.AddEntity 分配 EntityId
  → Entity.AfterNew
      GameObjectLogic.AfterNew 首先执行：
        创建或接管 GameObject
        从根节点读取一次 Binder
        校验 Binder 类型和全部必需引用
        向 Binder 声明的 ColliderProxy 写入宿主 Entity
        写入 GameObjectComponent
        初始化构造阶段已经存在的 IEntityBinderComponent
      其他 Logic.AfterNew 按排序顺序执行，并从 GameObjectComponent 读取 Binder
  → Active
  → DespawnRequested
  → Entity 逆序 OnDispose
      普通 Logic 先清理
      GameObjectLogic 最后释放或归还 GameObject
  → Disposed
```

Entity 构造结束时必须已经注册全部 Component 和 Logic，任何 Logic 都不得在 `AfterNew` 中增加 Component 或改变 Entity 组成。`GameObjectLogic.AfterNew` 只处理表现绑定：创建或接管 GameObject、读取并校验根 Binder、写入已经存在的 `GameObjectComponent`，再为构造阶段已经存在的 `IEntityBinderComponent` 发布 Binder 引用。

初始化顺序由既有 Logic 排序协议保证，不增加 Entity 生命周期状态：为 `GameObjectLogic` 在 `OrderTag` 中保留一个早于 `Input` 的专用标签，并同时要求它在构造函数中第一个注册。`Entity` 按 `OrderTag`、注册顺序稳定执行 `AfterNew`，释放时按同一列表逆序调用 `OnDispose`，因此 `GameObjectLogic` 自然最先初始化、最后释放。

## 生成入口

外部只允许提供 `SpawnSpec` 一类值对象，内容包括资源地址、世界位置、旋转、父节点和创建模式。资源地址属于生成配置，不是基础模块依赖注入。

EntitySystem 对外提供原子生成入口，内部顺序为：构造并完整组成 Entity、分配 EntityId、调用既有 `Entity.AfterNew`、进入 Active。GameObject 的实例化与绑定发生在第一个执行的 `GameObjectLogic.AfterNew` 中；任一 Logic 初始化失败都由 EntitySystem 走同一条既有逆序回滚链，调用方不再编写 `Instantiate + new Entity(gameObject) + AddEntity + AfterNew + catch Destroy`。

## GameObject 创建模式

| 模式 | 来源 | 所有权与释放 |
| --- | --- | --- |
| `Spawned` | `Core.Asset` 根据资源地址实例化 | Entity 销毁时释放实例和资源句柄 |
| `SceneBound` | 场景已有对象，例如 PoiMono 根节点 | GameObjectComponent 记录场景所有权；Entity 回收时按配置停用或解绑，不默认销毁场景资产 |
| `Pooled` | 尚未实现 | 后续接入对象池时必须复用相同 Logic 生命周期，不增加 Entity 状态 |

当前 `Spawned` 与 `SceneBound` 共享同一个 GameObjectComponent、Binder 校验和 Logic 生命周期，仅所有权数据不同，不建立特殊 Entity 基类。

## Prefab 制作规则

1. Binder 必须位于 Prefab 根节点且每个根节点只能有一个。
2. Binder 的具体类型必须与 Entity 声明的类型一致。
3. 所有 Logic 需要的 Unity 引用必须在 Binder 中显式序列化，禁止运行时层级搜索。
4. Binder 引用不能为空，不允许以“缺失时再 GetComponent”作为兜底。
5. Prefab 可以保留 Unity 回调桥接器，但不得再挂载实现 `IComponent` 的 MonoComponent。
6. 每个可作为 Entity 目标的 Collider 必须挂载 ColliderProxy，并由 Binder 的 EntityColliderProxies 显式引用。
7. Prefab 进入资源构建前必须通过 Binder 完整性校验；校验只报告问题，不自动修改绑定。

## 当前迁移状态

1. `GameObjectComponent`、`GameObjectLogic`、`EntityBinder` 和最早的 `OrderTag.GameObject` 已接入既有 Entity 生命周期。
2. Yefa、Yousaer、Senyin 使用 `PlayerBinder`，Slime 使用 `SlimeBinder`；Prefab 根节点不再挂载 ELC Component。
3. Property、Motion、Spine、Attack、Effect、技能、成长和 EnemyAI Component 均为纯 C# 对象。
4. `EntitySystem.SpawnEnemy` 和初始小队创建只构造 Entity，不再预先实例化 GameObject。
5. Root Motion 与 ColliderProxy 作为 Unity 回调桥接器保留；ColliderProxy 在初始化阶段直接持有宿主 Entity，碰撞目标不经过 EntitySystem 反查。
6. Poi/NPC 的 SceneBound 链仍使用现有专用 Entity，后续可迁入同一 Binder 协议；`Entity.bindGo` 待旧 Logic 全部改用 `GameObjectComponent` 后删除。

## 验收标准

- 外部生成角色或怪物时不创建、不持有、不销毁其 GameObject。
- 每个 Entity Prefab 根节点只有一个符合实体类型的 Binder。
- Entity 初始化链最多读取一次根 Binder，不对各玩法组件执行分散 `GetComponent`。
- ELC Component 全部是纯 C# 对象，不继承 MonoBehaviour。
- Binder 不保存运行时业务状态，不执行玩法逻辑。
- EntitySystem 的生成入口对实例化、绑定、注册和 Logic 初始化提供统一失败回滚。
- Spawned 与 SceneBound 对象使用同一生命周期协议，并按所有权正确释放。
