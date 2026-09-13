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
2. `Entity` 注册到 `EntitySystem` 后只保存 `EntityId`，Effect 接入通过 `Core.Gameplay.GetSystem<EffectSystem>()` 获取本局效果系统。
3. `EffectLogic` 将 EffectSystem 和触发注册句柄写入 `EffectComponent`，攻击逻辑不直接持有 EffectRuntime。
4. 攻击命中时通过 `EffectComponent.Runtime` 发布事实信号。
5. Entity 销毁时由 `EffectLogic -> EffectComponent` 自动注销规则、移除持续效果并回滚属性句柄。

```csharp
// 从 Core.Gameplay 获取当前单局唯一 EffectSystem。
EffectSystem effectSystem = Core.Gameplay.GetSystem<EffectSystem>();

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

## 伤害元素

元素身份统一使用配表生成的 `Prometheus.Config.ElementType`（火、水、雷、冰、草、风、岩、物理），角色基础元素配置在 `PropertyConfig.elementAttribute`。项目里**不存在第二个元素枚举**——两个枚举必须手工保持同步，迟早会错位。

- 普通攻击和特殊攻击默认使用物理，技能和大招默认使用角色元素；持续 `ElementInfusionOperation` 可以按动作范围和优先级覆盖结果（即元素附魔），并在 Effect 移除时自动回滚。
- **元素之间没有克制倍率**。元素相互作用全部由 `ReactionMatrix` 配表承载，由 `IElementSystem` 判定身份、由 `DamageCalculator` 求值。
- `DamageOperation` 可以继承命中信号元素、读取 Caster 当前动作元素或使用固定元素；最终元素、动作类型与触发的反应标识会写入 `DamageApplied` 与 `Killed` 信号。

## 伤害结算

`DamageOperation` 自己不做任何数值计算，它把一次伤害交给 `Gameplay/Combat/DamagePipeline`：

1. 解析最终元素（附魔在此生效）；
2. 调用 `IElementSystem.Apply` 写入附着并取回命中的反应行；
3. 按反应类别折算增幅乘区、激化加算或剧变独立伤害；
4. 走 `DamageCalculator.Evaluate` 的主公式；
5. 把主伤害落地，再把剧变伤害作为**独立的第二笔伤害**落地。

暴击走 `EffectRuntime.NextCriticalRoll`（运行时自己的确定性随机源），因此伤害结果可随种子重放。

`EffectRuntime` 在构造时**必须**传入 `IElementSystem`：允许它缺席会让元素反应在没有任何报错的情况下整体失效。

落地本身走公共入口 `DamageSettlement.Settle`。抽出来是因为伤害不只来自命中——草原核到期引爆同样要扣血、发 `HpChanged`、发 `DamageApplied`、致死再发 `Killed`，这串顺序有第二份实现就迟早会漏掉其中一条。

## 反应产物

三个伪元素状态各有一个产物资产，位于 `BundleResources/Config/Effect/EffectDefinitions/`，由 `EffectLibrary` 的反应产物字段引用：

| 资产 | 承载 |
| --- | --- |
| `Eff_Frozen` | `ControlStateModifierOperation(Stun)`，冻结期间禁止目标行动 |
| `Eff_Quicken` | 仅 Buff 标记；超激化与蔓激化的数值全部来自反应矩阵 |
| `Eff_Bloom_Core` | 仅 Buff 标记；引爆伤害由 `ReactionProductSystem` 按剧变公式结算 |
| `Eff_Crystallize` | `ShieldOperation`，15 秒结晶护盾；四种结晶共用这一份，元素与吸收量随信号传入 |

前三者一律配成 `Permanent`：**它们的存活时间由 `IElementSystem` 的伪元素状态决定**，在资产上再写一份时长会造成两个时钟，一旦不同步就会出现「状态没了但还冻着」。施加与撤销由 `ReactionProductSystem` 完成。

`Eff_Crystallize` 相反，是 `Duration`：结晶护盾的 15 秒是一个**固定**时长，没有第二个时钟与它竞争，因此由资产持有最自然。它由 `DamageOperation` 在命中时直接施加给施加者，重复触发走 `RefreshDuration` 并重算护盾。

**新增 `DamageOperation` 的行为开关时必须同步检查既有资产**：新字段在旧资产上会反序列化成默认值，而默认值通常是「关闭」。元素附着就这样在正式资产里静默关闭过一次（见 [承压接缝台账](../../../../Docs/Arch/LoadBearingSeams.md) S-03），EditMode 用例结构上发现不了——它们每次都显式传参。

产物按 `EffectId` 从 `EffectLibrary.reactionProducts` 查找——「哪一行反应产出哪个 Effect」已经写在 `ReactionMatrix` 的 `effectId` 列里，代码里不再复述一遍。

资产由菜单 `Prometheus/Effect System/Create Or Update Reaction Product Assets` 生成，不手写 Unity YAML。工具放在编辑器测试程序集，因为写入 `EffectDefinition` 私有序列化字段的反射入口只在那里可见——正式运行时定义保持只读。

## 硬直

打断是**分级比较**而不是数值阈值（08 第 2.3 节）：

```text
打断成立  ⟺  攻击打断等级 > 目标抗打断等级
```

- 攻击侧：`DamageOperation.staggerLevel`，每段攻击的固定整数配置（0 无打断 ～ 5 极强），与伤害数值无关；
- 受击侧：`PropertyConfig.staggerResistance` → `PropertyType.StaggerResistance`；
- 霸体：`PropertyType.SuperArmor` 非零时**覆盖**抗打断等级，任何 Effect 都能用 `PropertyModifierOperation` 写它，不需要专用操作。

结算发布两条互斥的事实：打断成立发 `StaggeredEvent`（受击动画订阅），未成立发 `StaggerResistedEvent`（闪白与命中特效订阅，表现属排期第 9 步）。致死伤害与零扣血两条都不发——死亡动画会抢占受击动画，被护盾完全挡下的攻击不构成受击。

剧变伤害与草原核引爆的打断等级固定为 0：打断由触发它们的那一击负责。

`EffectPropertyValue.Toughness` 等旧名保留（枚举值是资产的序列化索引），但读写的已经是抗打断等级。

## 运行约束

- Trigger 只能产生 EffectRequest，不能直接递归执行效果。
- 每个 GameplayKit 只注册一个 EffectSystem，多个单局上下文之间不共享 EffectRuntime。
- EffectLibrary 的资源地址是 EffectSystem 的私有配置；EffectSystem 在 `AfterNewAsync` 中自行加载，组合根、测试和工具均不得从构造函数注入配置库。
- EffectSystem 与 Entity Logic 不保存或注入 IGameplayKit，跨玩法模块访问统一从 `Core.Gameplay` 开始。
- EffectDefinition 是共享只读配置，所有层数、时间和句柄必须保存在 EffectInstance。
- 运行时动态养成使用 `EffectDefinition.CreateRuntime` 创建带 `HideAndDontSave` 的 Entity 独占定义，仍必须经过标准 `EffectInstance` 与资源句柄生命周期；替换时先移除实例，再释放临时定义。
- 持续属性修改必须使用实例资源句柄，实例移除时由运行时统一回滚。
- 持续控制必须使用 `ControlStateModifierOperation`；不要再用成对的 Start/End Event 手动阻塞 Logic。
- 子信号必须保留 SignalChainId 并增加 ChainDepth，以便 OncePerSignalChain 和递归上限生效。
- `Caster` 始终表示直接释放当前行为的实体，`Source` 始终表示整条因果链的实际源头实体。
- 表现层应监听结果信号播放 VFX、音效和飘字，不应反向修改效果运行时。
- `CombatAudioPresentationSystem` 统一消费实际值大于零的 `DamageApplied` 并在信号世界坐标播放命中音效；普通、周期和致命伤害都不依赖受击动画，受击 AnimationLine 不得重复绑定同一命中事件。
- `SignalProcessed` 的只读观察者异常会被逐个隔离并写入 Effect trace，表现故障不得中断战斗结算或阻止其他表现模块收到同一事实。
- `ShowInBuffList` 控制持续 Effect 是否允许进入 HUD Buff 快照；角色等级、天赋、装备和武器的内部永久 Effect 固定关闭该开关，并使用 `Growth` 标签供诊断与测试识别。

## 角色养成投影

角色养成系统不直接修改最终属性。`CharaLevelLogic`、`TalentLogic`、`EquipmentLogic` 与 `WeaponLogic` 都通过 Entity 独占的永久 Effect 投影动态结果：角色等级写入攻击力 `Offset`，装备和武器从随曲线等级成长的 `TierInstance` 按 `PropertyType + PropertyModifierMode` 汇总当前固定值与系数值，天赋使用 `TalentGainModifierOperation` 修改各技能 Component 自己持有的 `ModifiableProperty` 增益系数。完整配置、公式、Debug 数据和测试入口见 `Assets/Prometheus/Gameplay/EntitySystem/Character/Logic/Growth/README.md`。

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

在 Unity 菜单执行 `Prometheus/Effect System/Create Or Update Example Assets`。该测试工具会在 `Assets/BundleResources/Config/Effect` 中更新已有有效资产；只有检测到旧资产脚本绑定无效时才会重建该资产。

选中任意 `EffectDefinition` 资产后，可以在自定义 Inspector 当前有效的生命周期列表中点击 `Add Operation`，直接添加伤害、属性修改、控制状态修改、二次效果或发信号操作。Instant 只显示 On Apply；Duration 才显示 Duration；持续效果按 Tick Interval 和 Stack Policy 继续显示实际会执行的配置。On Stack 只在 AddStack 类策略且 Max Stacks 大于一时显示；On Refresh 只在有限 Duration 使用 RefreshDuration 类策略时显示。

重复施加持续效果时，层数变化与时长刷新分别产生 `EffectStacked` 和 `EffectRefreshed`。`RefreshDuration` 不执行 On Stack；`AddStackAndRefreshDuration` 未满层时同时执行 On Stack 与 On Refresh，满层后只执行 On Refresh；刷新 `ElapsedTime` 时保留 `TickElapsedTime`，避免改变周期效果的既有结算节奏。

Effect Id 和 Trigger Id 都提供 `Automatic` 与 `Custom` 两种模式；Automatic 分别使用资产名和 Signal Type，减少手写标识符错误。新增 `Property Modifier` 时会自动展开 `valuePerStack`，其默认公式为 `One × 0 + 0`。`Key Policy` 默认使用 `Automatic`，按 `PropertyType + PropertyModifierMode` 生成实例资源键；只有同一效果需要多条相同属性和相同模式的独立 Modifier 时才选择 `Custom`。在任意 `PropertyModifierOperation` 配置框内点击鼠标右键，可以复制完整配置并粘贴到其他 Property Modifier。

`EffectValueFormula` 的 Property 来源由 `EffectValueEntity + EffectPropertyValue` 正交组成，因此 Caster、Target、Source 都能自由读取任意运行时属性。`Hp/CoreEnergy/UltEnergy` 表示当前资源，`MaxHp/CoreEnergyLimit/UltEnergyLimit` 表示可被运行时 Modifier 或基础值入口更新的上限副本；公式只读取 `PropertyComponent`，不会在战斗过程中回读 `PropertyConfig`。旧版 `CasterAttack/TargetAttack/CasterMaxHp/TargetMaxHp/CasterCoreEnergy` 的序列化整数会在反序列化时自动迁移到新结构。

`CoreEnergyGainOperation` 接受有符号公式结果：正数增加核心能量，负数扣除核心能量，最终值统一限制在 `0～CoreEnergyLimit`。因此满能量后的清空可以直接配置为运行时能量（或等值上限）乘以 `-1`，无需额外清空操作。

正式资产集成测试必须从当前 `EffectDefinition`、`EffectTriggerSet` 或其他被测资产读取配置并独立计算预期结果，不得在断言中复制增量、阈值、持续时间等配置常量；只有使用测试内存对象验证纯算法边界时，才应显式写出输入和对应期望值。
