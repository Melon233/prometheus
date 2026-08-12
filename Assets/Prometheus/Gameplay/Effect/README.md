# Prometheus Effect System

本目录实现以 `EffectSignal -> EffectTriggerDefinition -> EffectRequest -> EffectInstance` 为主链路的效果系统。

## 目录

- `Core`：信号类型、标签、条件和数值公式。
- `Definitions`：效果定义与触发规则资产。
- `Operations`：伤害、属性修改、控制状态修改、二次效果和发信号等原子操作。
- `Runtime`：请求队列、触发路由、堆叠、Tick、移除、递归保护以及运行时效果库。
- `Editor`：Effect 定义的自定义 Inspector 等正式编辑器扩展。
- `Tests/Editor`：EditMode 自动化测试、示例配置工厂和示例资产生成菜单。
- `Assets/BundleResources/Config/Effect`：与脚本目录分离的持久化 Effect、Trigger 和 Library 配置资产。

## 快速接入

1. `GameplayKit` 为当前单局注册唯一的 `EffectSystem`，不再创建场景单例。
2. `Entity` 注册到 `GameplayKit` 后，通过 `Entity.GameplayKit.GetSystem<EffectSystem>()` 获取本局效果系统。
3. `EffectLogic` 将 EffectSystem 和触发注册句柄写入 `EffectComponent`，攻击逻辑不直接持有 EffectRuntime。
4. 攻击命中时通过 `EffectComponent.Runtime` 发布事实信号。
5. Entity 销毁时由 `EffectLogic -> EffectComponent` 自动注销规则、移除持续效果并回滚属性句柄。

```csharp
// 从 Entity 所属的单局 GameplayKit 获取唯一 EffectSystem。
EffectSystem effectSystem = attacker.GameplayKit.GetSystem<EffectSystem>();

// EffectLogic 已经把运行时和注册句柄集中写入 EffectComponent。
attacker.TryGetComp(out EffectComponent effectComponent);

// 在命中确认后只发布事实信号，后续效果由触发规则和统一队列完成。
effectSystem.DefaultLibrary.PublishFireAttack(effectComponent.Runtime, attacker, target);

// Entity 销毁时 EffectComponent.DisposeBindings 会自动完成清理。
```

## 示例规则

- `DirectAttackDamage.asset`：即时效果，按直接释放者 Caster 的当前攻击力造成伤害。
- `Burning.asset`：持续十秒，每秒造成十点火焰 DOT，同一实际源头 Source 重复添加时刷新时间。
- `CombatFlow.asset`：持续三秒，最多五层，每层增加 10% 攻击力和 5% 攻速，叠层时刷新时间。
- `Stun.asset`：持续三秒，通过 `ControlStateModifierOperation` 施加眩晕，实例移除时自动回滚自身句柄。
- `AttackTriggers.asset`：攻击命中产生直接伤害，最终 `DamageApplied` 为火属性时额外施加燃烧，带 `Control` 标签时额外施加眩晕。
- `CombatFlowTriggers.asset`：实际攻击伤害大于零时叠加战意，并通过 `LacksAnyTags(Dot)` 排除 DOT。

## 伤害属性

- `DamageAttribute` 固定包含火、水、雷、冰、草、光、暗、物理八种属性，角色基础元素配置在 `PropertyConfig.elementAttribute`。
- 普通攻击和特殊攻击默认使用物理属性，技能和大招默认使用角色元素属性；持续 `DamageAttributeModifierOperation` 可以按动作范围和优先级覆盖结果，并在 Effect 移除时自动回滚。
- 火克冰、冰克草、草克雷、雷克水、水克火，光暗互克，物理中立；仅克制方在独立乘区获得 1.3 倍伤害，不存在被克减伤。
- `DamageOperation` 可以继承命中信号属性、读取 Caster 当前动作属性或使用固定属性；最终属性、动作类型、克制关系和属性倍率会写入 `DamageApplied` 与 `Killed` 信号。

## 运行约束

- Trigger 只能产生 EffectRequest，不能直接递归执行效果。
- 每个 GameplayKit 只注册一个 EffectSystem，多个单局上下文之间不共享 EffectRuntime。
- EffectDefinition 是共享只读配置，所有层数、时间和句柄必须保存在 EffectInstance。
- 持续属性修改必须使用实例资源句柄，实例移除时由运行时统一回滚。
- 持续控制必须使用 `ControlStateModifierOperation`；不要再用成对的 Start/End Event 手动阻塞 Logic。
- 子信号必须保留 SignalChainId 并增加 ChainDepth，以便 OncePerSignalChain 和递归上限生效。
- `Caster` 始终表示直接释放当前行为的实体，`Source` 始终表示整条因果链的实际源头实体。
- 表现层应监听结果信号播放 VFX、音效和飘字，不应反向修改效果运行时。
- `CombatAudioPresentationSystem` 统一消费实际值大于零的 `DamageApplied` 并在信号世界坐标播放命中音效；普通、周期和致命伤害都不依赖受击动画，受击 AnimationLine 不得重复绑定同一命中事件。
- `SignalProcessed` 的只读观察者异常会被逐个隔离并写入 Effect trace，表现故障不得中断战斗结算或阻止其他表现模块收到同一事实。

## 控制状态

`PropertyComponent` 聚合全部 `ControlStateModifier`，并缓存 `ActiveControlStates`。每个来源拿到独立 Modifier 句柄，所以两个 Effect 同时施加 Root 时，任意一个先结束都不会错误解除另一个来源的 Root。

| 状态 | CanAct | CanMove | CanUseActiveSkill |
| --- | --- | --- | --- |
| 无控制 | true | true | true |
| Stun | false | false | false |
| Root | true | false | true |
| Silence | true | true | false |

主动玩法 Logic 默认声明 `LogicControlRequirement.Act`。移动、跳跃、闪避、巡逻和追击声明 `Move`；输入采样、重力/物理、受击、死亡与 Effect 生命周期声明 `None`。玩家 TalentLogic 在通过 Act 门禁后单独查询 `CanUseActiveSkill`，因此 Silence 只拦截技能和大招，不影响普通攻击。

## 重新生成示例资产

在 Unity 菜单执行 `Tools/Prometheus/Effect System/Create Or Update Example Assets`。该测试工具会在 `Assets/BundleResources/Config/Effect` 中更新已有有效资产；只有检测到旧资产脚本绑定无效时才会重建该资产。

选中任意 `EffectDefinition` 资产后，可以在自定义 Inspector 当前有效的生命周期列表中点击 `Add Operation`，直接添加伤害、属性修改、控制状态修改、二次效果或发信号操作。Instant 只显示 On Apply；Duration 才显示 Duration；持续效果按 Tick Interval 和 Stack Policy 继续显示实际会执行的配置。On Stack 只在 AddStack 类策略且 Max Stacks 大于一时显示；On Refresh 只在有限 Duration 使用 RefreshDuration 类策略时显示。

重复施加持续效果时，层数变化与时长刷新分别产生 `EffectStacked` 和 `EffectRefreshed`。`RefreshDuration` 不执行 On Stack；`AddStackAndRefreshDuration` 未满层时同时执行 On Stack 与 On Refresh，满层后只执行 On Refresh；刷新 `ElapsedTime` 时保留 `TickElapsedTime`，避免改变周期效果的既有结算节奏。

Effect Id 和 Trigger Id 都提供 `Automatic` 与 `Custom` 两种模式；Automatic 分别使用资产名和 Signal Type，减少手写标识符错误。新增 `Property Modifier` 时会自动展开 `valuePerStack`，其默认公式为 `One × 0 + 0`。`Key Policy` 默认使用 `Automatic`，按 `PropertyType + PropertyModifierMode` 生成实例资源键；只有同一效果需要多条相同属性和相同模式的独立 Modifier 时才选择 `Custom`。在任意 `PropertyModifierOperation` 配置框内点击鼠标右键，可以复制完整配置并粘贴到其他 Property Modifier。

`EffectValueFormula` 的 Property 来源由 `EffectValueEntity + EffectPropertyValue` 正交组成，因此 Caster、Target、Source 都能自由读取任意运行时属性。`Hp/CoreEnergy/UltEnergy` 表示当前资源，`MaxHp/CoreEnergyLimit/UltEnergyLimit` 表示可被运行时 Modifier 或基础值入口更新的上限副本；公式只读取 `PropertyComponent`，不会在战斗过程中回读 `PropertyConfig`。旧版 `CasterAttack/TargetAttack/CasterMaxHp/TargetMaxHp/CasterCoreEnergy` 的序列化整数会在反序列化时自动迁移到新结构。

`CoreEnergyGainOperation` 接受有符号公式结果：正数增加核心能量，负数扣除核心能量，最终值统一限制在 `0～CoreEnergyLimit`。因此满能量后的清空可以直接配置为运行时能量（或等值上限）乘以 `-1`，无需额外清空操作。

正式资产集成测试必须从当前 `EffectDefinition`、`EffectTriggerSet` 或其他被测资产读取配置并独立计算预期结果，不得在断言中复制增量、阈值、持续时间等配置常量；只有使用测试内存对象验证纯算法边界时，才应显式写出输入和对应期望值。
