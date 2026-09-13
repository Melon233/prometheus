# 承压接缝台账

> 状态：生效
> 用途：记录**已经知道会在某个具体时刻变成问题**的结构，以及那个时刻是什么。
> 与 [ArchSpec](ArchSpec.md) 的区别：ArchSpec 管「现在必须遵守什么」，本文管「什么时候必须改」。

## 这份文档存在的理由

「以后再重构」是一句没有触发条件的话，因此永远不会发生。本文每一条都必须写明**可判定的触发条件**——不是「等有空」「等代码变乱」，而是「下一次有人做 X 的时候」。

条目只在两种情况下删除：改完了，或者确认它不会发生。**不得因为「暂时没人碰」而删除。**

---

## S-01 `EffectRuntime` 正在积累会话级 System

**现状**：`EffectRuntime` 构造函数已注入 2 个 System（`IElementSystem`、`IShieldSystem`），都是「伤害落地前要插一脚」的东西。注入本身是对的——允许它们缺席会让元素反应或护盾吸收**在没有任何报错的情况下**整体失效。

**问题不在参数多，在于这是拦截器模式被写成了固定参数列表。** 已经能看见的下一批：

| 需求 | 来源 | 需要插在哪一步 |
| --- | --- | --- |
| 场外角色完全不受伤害 | [06](../Design/Combat/06_队伍与角色切换.md) 第 4.1 节 | 落地之前，整笔取消 |
| 破盾硬直期间受伤提高 | [07](../Design/Combat/07_敌人与战斗AI.md) 第 4.1 节 | 护盾吸收之后 |
| 承伤转移、吸血、免伤 | 角色技能 | 各自不同 |

**触发条件**：**下一次有人要往 `EffectRuntime` 构造函数加第三个 System 时。**

**改法**：把 `DamageSettlement.Settle` 内部的固定步骤换成有序的 `IDamageInterceptor` 链，由组合根按顺序注册（`护盾吸收 → 免伤 → 场外过滤 → 承伤转移`）。`EffectRuntime` 只持有这条链，不再认识具体是谁。

**为什么现在不改**：只有两个拦截点时，链式抽象比直接调用更难读。第三个进来之前，直写是对的。

---

## S-02 敌人元素护盾与玩家吸收护盾是两套机制

**现状**：`Gameplay/ShieldSystem/` 实现的是**玩家侧吸收护盾**——扣的是 HP 伤害，元素亲和按固定 2.5 倍效率（[05](../Design/Combat/05_伤害结算公式.md) 第 6 节）。

[07](../Design/Combat/07_敌人与战斗AI.md) 第 4.2 节定义的敌人元素护盾是**另一套**：

- 扣的是**耐久**而不是 HP 伤害；
- 效率是一张表（物理 0.5×、同元素 0.5×、克制元素 2.0×、剧变 2.0×、结晶 1.0×），不是一个常量；
- 破盾后进入破盾硬直，期间受伤提高。

`ShieldSystem.MatchingElementEfficiency` 的注释写的是「机制规则，不随某张表变化」——**这句话对玩家护盾成立，对敌人护盾不成立**。

**触发条件**：**开始做排期第 7 步（敌人）时。**

**改法**：两条路二选一，做之前先定。

1. 吸收效率改成 `ShieldLayer` 的每层数据，敌人护盾作为另一种层接入同一系统；
2. 承认它们是两套机制，敌人护盾独立建模，`ShieldSystem` 改名为 `AbsorptionShieldSystem` 以免继续占用通用名字。

倾向 2：耐久与 HP 伤害是不同的量纲，硬塞进一个系统会让两边都变形。

---

## S-03 战斗链路的 PlayMode 覆盖 —— **已建立，需随功能扩充**

**现状**：`Prometheus.Combat.PlayModeTests` 已就位（10 条），走真实 Core 启动、真实组合根、真实实体预制体，覆盖会话启动顺序、预制体装配、一次命中的完整链路、分级硬直两侧、冻结禁止行动、护盾先于生命值吸收、致死不播受击。

**它第一次运行就抓到了一个只有真实资产才暴露的缺陷**，见下方「首次运行的发现」。这条记录保留下来，是为了说明这类测试的价值不在回归而在**首次接触真实数据**。

**仍未覆盖**：碰撞体驱动的命中窗口（现在直接发布 `HitConfirmed` 信号，跳过了 `OnTriggerEnter`）、连续受击的动画会话替换、场外成员的伤害处理。

**触发条件**：**每次新增战斗系统时同步补一条链路用例**；做排期第 9 步（表现层）之前补齐命中窗口那一段。

### 首次运行的发现：正式资产从未开启元素附着

`DirectAttackDamage.asset` 里**根本没有 `gaugeStrength` 字段**——它是在元素系统接线时新增的，而资产早于它存在，因此反序列化成默认值 `None`（不附着）。

后果是**正式游戏里没有任何一次攻击会产生元素附着**：元素系统、反应矩阵、伪元素状态、反应产物、结晶护盾全部够不着。343 条 EditMode 用例一条都没发现，因为它们每次都显式传入 `Strength`。

已把该资产改为 `Weak`（1U，对应 04 的单手剑普攻）。**这类「新增字段在旧资产上默认为关闭」的缺陷是本项目的结构性风险**：`DamageOperation` 后续新增的任何行为开关都会重复它一次，而 EditMode 结构上看不见。

---

## S-04 天赋是 Logic 类而不是数据，命之座无处落脚

**现状**：四个动作是四个 Logic 类 + 配置（`NormalAttackLogic` / `SkillLogic` / `SpecialAttackLogic` / `UltimateLogic`）。命之座要做的是「E 技能多出一段」「Q 的倍率按条件变化」「普攻第三段附加效果」这类**行为改写**。

**这是整个项目里最硬的结构问题，而且与战斗结算完全正交**——上面几轮的工作一点没碰到它，也因此一点没有减轻它。

**触发条件**：~~开始做排期第 5 步之前必须先定方案~~ —— **方案已定，见下**。剩余风险转为：第 5 步必须**按下方顺序**做，先段落表再补动作；反过来会把重击、下落、充能层数写进现有 Logic，随后的数据化等于重做。

### 方案已定（2026-09）：段落数据化，动作流程仍是 Logic

**问题先被缩小了。** 命座效果分三类，只有一类逼着回答「天赋是不是数据」：

| 类别 | 例子 | 现有结构 |
| --- | --- | --- |
| 数值改写 | 天赋 +3 级、冷却 −X、持续 +X | 已支持，对已有配置项加减 |
| Effect 投影 | 减防、附魔、队伍增益、命中时触发 | 已支持；`Constellation` 表已有 `effectIds` 列 |
| **结构改写** | 普攻多一段、E 多一次充能 | **不支持，只有这一类构成决策压力** |

**选段落数据化（方案 A）而不是动作图完全数据化（方案 B）**，三条理由：

1. 项目自己的策划案早已选了它——[02 第 6 节](../Design/Combat/02_攻击与连段.md) 已定义 `AttackSegment`，第 7 节写明「策划不接触代码」。这是执行既有决定，不是新提案。
2. 它同时解开三个**与命座无关、但现在已经卡住**的缺口：每段攻击的打断等级、每段的元素附着档位（PlayMode 首次运行发现的那个 bug 正是它的症状）、每段的命中上限。
3. 方案 B 的抽象应当由重复驱动。现在只有三份角色配置，共性尚未显形，此时做动作图大概率抽错——这正是 `ARCH-META-001` 的精神。

**四条落地决定**：

| 项 | 决定 |
| --- | --- |
| 时序 | 归动画事件，策划在 `AnimationLine` 编辑器配；`AttackSegment` 的 `ActiveStart/ActiveEnd` **废弃** |
| 段落数值 | Luban 配表，键 `(talentId, stageIndex, windowIndex)` |
| 事件↔段落绑定 | `EnableHitbox.intValue` = 该动画内的命中窗口序号（语义键，非表下标） |
| 天赋结构 | 4 个合并为 3 主动 + 3 固有，与段落表同批做——「重击并入普攻天赋」本质上就是「重击是普攻段落表的另一组段落」，分两次做要改两遍同一批代码 |

理由与细节见 [02 第 6.1.1 / 6.1.2 节](../Design/Combat/02_攻击与连段.md)。

**落地顺序与进度**：

1. ~~段落表 + 四个 Logic 改为段落驱动~~ —— **已完成**，见 [02 第 6.1.3 节](../Design/Combat/02_攻击与连段.md)
2. 接上 `TalentMultiplier` 逐级表（Growth 排期第 4 步）——段落表现在暂存天赋 1 级倍率，等级缩放仍走统一系数
3. ~~体力系统~~ **已完成** → 下落攻击、充能层数、点按/长按、重击更名 `ChargedAttack`
4. 天赋合并为 3 主动 + 3 固有
5. 命座：C3/C5 走等级加算，其余走 `effectIds`，需改流程的逐个当 Logic 的可选能力

**遗留的清理**：`SkillComponent` / `UltimateComponent` / `SpecialAttackComponent` 与 `PlayerBinder` 上的 `abilityId` 序列化字段在玩家路径上已经没有读取方（天赋标识改由「角色 + 动作」推导）。留着的风险是有人在 Inspector 里改它然后发现没有任何反应——与本文 S-03 记录的那类缺陷同形。删除需要动预制体，触发条件：**下一次编辑这几个预制体时**。

---

## S-05 击退等位移事实没有传递通道

**现状**：伤害落地只发两条事实：`StaggeredEvent`（打断成立）与 `StaggerResistedEvent`（未成立）。两者都**不携带方向与力度**。

[08](../Design/Combat/08_受击反馈与硬直.md) 第 4 节要的击退、击飞、吸附都需要：方向（攻击者指向目标，或段落指定）、距离、曲线、以及「抗打断等级高于阈值免疫」。

**触发条件**：**做位移效果时**（08 第 4 节，与排期第 5 步的段落表同期）。超载的强击退是它区别于其它剧变反应的关键特征，做元素表现时会立刻撞上。

**改法**：位移参数属于攻击段落配置，与「每段攻击的打断等级」是同一批数据，应当一起进段落表；事实事件相应扩展或新增 `KnockbackEvent`。

---

## S-06 双能量模型尚未替换为单一元素能量

**现状**：`PropertyConfig` 仍是 `coreEnergyLimit` + `ultEnergyLimit` 双能量，对应 `EffectSignalType.CoreEnergyGain` / `UltEnergyGain` 等一整套信号与标签。原神只有一种能量（元素能量），由元素微粒补充，受元素充能效率影响。

`PropertyType.ElementalEnergyLimit` 属性位已在 05A 中建好，但没有消费方。

**触发条件**：**做排期第 6 步（能量与队伍）时。**

**改法**：见 [90 差异台账](../Design/Combat/90_现有实现差异台账.md) 第 1.2 节。注意 `EffectSignalType` 与 `EffectTag` 的取值已被 [SerializedEnumLayoutTests](../../Assets/Prometheus/Tests/Architecture/SerializedEnumLayoutTests.cs) 钉住，旧成员只能保留占位，不能删除。

---

## S-10 `SpecialAttack` 应更名为 `ChargedAttack`

**现状**：重击在代码里叫 `SpecialAttack`（组件、Logic、动画执行器、`EffectTag`、`DamageActionType`、`AnimationOwner`），而策划案叫**重击 / ChargedAttack**，05A 的属性位也已经叫 `ChargedAttackBonus`。每次读代码都要做一次名字换算。

**为什么现在不改**：改动面是 24 个 C# 文件 + 10 个资产，其中 `PlayerBinder`、`AnimationLibrary`、`TalentConfig` 的序列化字段名写在**玩家预制体与资产里**。而当前 PlayMode 冒烟测试生成的是史莱姆，**没有任何自动化覆盖能验证玩家预制体的绑定完整性**——改错了不会有人报错，只会在实机里发现某个角色的重击没反应。

**触发条件**：**玩家角色进入 PlayMode 冒烟覆盖之后**（见 S-03），或有人本来就要编辑这几个预制体时。

**改法**：每个被改名的序列化字段加 `FormerlySerializedAs` 以保住资产数据；枚举成员改名不影响取值，安全。

---

## S-07 反应产物资产的生成工具位于编辑器测试程序集

**现状**：`ReactionProductAssetCreator` 与 `EffectExampleAssetCreator` 都在 `Prometheus.Effects.EditorTests` 里，因为写入 `EffectDefinition` 私有序列化字段的反射入口（`ConfigureForTests`）只在那里可见。

它生成的却是**正式内容**（`Eff_Frozen`、`Eff_Crystallize` 等）。资产本身落盘后不依赖工具，但「生产内容的编辑器工具住在测试程序集」是个会让人困惑的位置，且 `ConfigureForTests` 这个名字在生产路径上读起来是错的。

**触发条件**：**策划需要自己在 Inspector 里编辑这些资产时**，或第二个人要新增产物资产时。

**改法**：把私有字段写入能力提取成 `Gameplay/EffectSystem/Editor/` 下的 `EffectDefinitionAuthoring`，两个生成器都改用它；`ConfigureForTests` 保留给测试。

---

## S-08 配表占位数据清单

下列数据会**系统性**影响一整类数值，且当前是插值或估算值，不是实机反解结果：

| 表 | 行数 | 状态 | 影响面 |
| --- | --- | --- | --- |
| `ReactionLevelCoefficient` | 90 | 6 个实机锚点 + 84 行单调三次插值 | **全部**剧变、激化、结晶伤害的公共因子 |
| `PseudoElementState.Quickened` | 1 | 区间 5～30 秒，插值方式自定 | 原激化时长 → 激化触发窗口 |
| `PseudoElementState.DendroCore` | 1 | 固定 6 秒，策划案未给数值 | 绽放引爆时机 |
| `ReactionMatrix` | 19/31 行 | `NeedsReview` | 剧变与草系全部倍率 |
| `CharacterLevelCurve` | 90 | Placeholder | 全部角色成长数值 |

**触发条件**：**开始实机校准时**——这是接下来的主要工作。`ReactionLevelCoefficient` 优先级最高，它填错会让三类反应数值同时系统性偏移，而偏移是等比例的、不会被任何测试抓住。

**已就绪的工具**：`EffectRuntime.TraceDamage` 会把每次命中的乘区拆解发到诊断通道（`EffectSystem(traceEnabled: true)` 时转发到 Unity Console），用于回答「这一刀为什么是这个数」。

---

## S-09 敌人没有等级

**现状**：敌我一律按 `DamagePipeline.DefaultLevel`（1 级）参与防御区，防御区因此恒等于 0.5。这是一个**影响全部伤害数值**的占位。

**触发条件**：**做排期第 7 步（敌人）时**，或实机校准需要对齐绝对数值时。

**改法**：敌人等级表 + `CharaLevelComponent` 之外的等级来源；`DamagePipeline.ResolveLevel` 的调用方已经集中在 `DamageOperation.ResolveLevel` 一处。

---

## 本轮已关闭

| 条目 | 关闭方式 |
| --- | --- |
| `PropertyType` 等序列化枚举可被静默重排 | [SerializedEnumLayoutTests](../../Assets/Prometheus/Tests/Architecture/SerializedEnumLayoutTests.cs) 钉住 4 个枚举的声明顺序，插入/删除/重排会指名报错 |
| 暴击算完即丢，「暴击时触发」无法实现 | `DamageFacts.IsCritical` 随 `DamageApplied` 发布，新增 `EffectConditionType.DamageWasCritical` 条件 |
| `EffectSignal` 构造函数 17 个参数且每加一维就长一格 | 伤害相关六项收进 `DamageFacts`，构造函数降至 13 个参数、`CreateChild` 降至 11 个 |
| 一次命中经 8 跳而无拆解，实机校准无从下手 | `DamageBreakdown.Describe` 复用 `DamageCalculator` 的公开函数复算每个乘区，中性乘区不输出 |
