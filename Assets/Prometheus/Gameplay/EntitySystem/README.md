# EntitySystem 角色生成与表现绑定

## 目录约定

新增类型放哪里，只由一条判据决定：

> **这个类型能脱离 Entity 独立存在吗？**
> 不能（Component / Logic）→ 属于 EntitySystem，按原型分组
> 能（资产格式、纯算法、状态机）→ 属于它自己的库（如 `Gameplay/Animation`、`Gameplay/Ai`）

EntitySystem 内部按**实体原型**分组，每个原型再按类型种类分层：

```text
EntitySystem/
  Character/              玩家角色
    Component/            全部纯 C# 组件（平铺）
    Config/               全部配置 SO（平铺）
    Logic/                通用 Logic
      Talent/             天赋特性簇：Logic + 其纯状态类型
      Growth/             成长特性簇：等级 / 装备 / 武器的 Logic、配置模型与曲线工具
    CharacterBinder / PlayerBinder / PlayerEntity
  Monster/                敌人
    Component/            含 EnemyAiComponent（AI 库的实体侧数据）
    Logic/                含 EnemyAiLogic（实现 Ai 库的 IEnemyAiAgent 端口）
    SlimeBinder / SlimeEntity
  Poi/                    世界交互物
  Common/                 跨原型共用 Logic
  Presentation/           实体表现层 MonoBehaviour（血条、飘字）
```

特性簇（`Logic/Talent/`、`Logic/Growth/`）的作用是把一组强相关的 Logic 与它们独占的纯状态、配置模型放在一起。**判断标准是"这组类型是否总是一起改动"**，不是按技术层切分——按技术层把成长、AI、动画从实体里抽走，正是历史上造成 `Gameplay/Growth`、`Gameplay/Ai` 这类平行目录的原因。

如果 EntitySystem 继续膨胀，正确的拆法是把 `Character` / `Monster` / `Poi` 拆成并列模块，而不是再次抽取技术层。

## 当前职责

`EntitySystem` 管理 EntityId、注册、稳定更新、字段监听和回收事务。它不实例化角色 Prefab、不读取 Prefab 内部组件，也不维护表现层 Collider 索引；角色表现由 Entity 构造阶段注册的 `GameObjectLogic` 负责。

## 角色生成链

```text
EntitySystem.SpawnEnemy / CreateTeam
  → new SlimeEntity / PlayerEntity(资源地址、位置、旋转、父节点)
  → Entity 构造函数注册全部纯 C# Component
  → 第一个注册 GameObjectLogic
  → 注册其余 Gameplay Logic
  → EntitySystem.AddEntity 分配 EntityId
  → Entity.AfterNew
      GameObjectLogic 使用 Core.Asset 实例化 Prefab
      校验根 PlayerBinder / SlimeBinder
      把宿主 Entity 写入 Binder.EntityColliderProxies
      写入 GameObjectComponent
      初始化既有 IEntityBinderComponent
      其余 Logic.AfterNew
  → Active
```

外部调用方不得编写 `Instantiate + new Entity(gameObject)` 生成角色。测试和既有场景对象可以使用 `SceneBound` 构造入口，但 Entity 回收时不会销毁场景拥有的对象。

## Prefab 约束

- Yefa、Yousaer、Senyin 根节点各挂一个 `PlayerBinder`。
- Slime 根节点挂一个 `SlimeBinder`。
- Binder 集中保存 CharacterController、SkeletonAnimation、AnimationLibrary、ColliderProxy、VFX 槽位和只读配置资产。
- Prefab 不挂载任何 ELC Component；`PropertyComponent`、`MotionComponent`、`SpineComponent` 等全部由 Entity 构造函数使用 `new` 创建。
- CharacterRootMotionComponent 和 ColliderProxy 不实现 `IComponent`；前者只转发 Root Motion，后者只转发碰撞回调并保存初始化阶段写入的宿主 Entity 引用。

## 碰撞目标反查

每个可作为战斗或 AI 目标的 Collider 都必须挂载 `ColliderProxy`，并由根 Binder 通过 `EntityColliderProxies` 显式持有。`GameObjectLogic.AfterNew` 在其他 Logic 初始化前把宿主 Entity 写入这些 Proxy；攻击和 AI 收到 Collider 后直接从同节点 Proxy 取得 `HostEntity`，再读取纯 C# `PropertyComponent`。`GameObjectLogic.OnDispose` 最后解除宿主引用，`EntitySystem` 不参与这条表现反查链。

## 释放顺序

`GameObjectLogic` 使用最早的 `OrderTag.GameObject`，因此最先执行 `AfterNew`，并通过 Entity 既有逆序释放机制最后执行 `OnDispose`。其他 Logic 会先解除动画、Root Motion、事件和碰撞代理回调，随后 GameObjectLogic 解除 Binder 感知 Component 与 ColliderProxy 宿主引用，并按所有权销毁 Spawned 对象或仅解绑 SceneBound 对象。
