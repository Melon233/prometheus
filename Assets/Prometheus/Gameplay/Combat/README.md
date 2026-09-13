# Combat

战斗结算的**纯函数库**。这里没有 System、没有状态、不读全局：输入完全由 Context 给出，相同输入恒得相同输出。

按 `ARCH-NAME-001`，本目录只含库与计算器、不含 System，因此不使用 `System` 后缀。

## 组成

| 文件 | 职责 |
| --- | --- |
| `DamageContext.cs` | 四类输入结构：`DamageContext` / `TransformativeDamageContext` / `HealingContext` / `ShieldContext` |
| `DamageCalculator.cs` | 主公式、剧变公式、治疗、护盾，以及暴击区、防御区、抗性区三个独立可用的乘区函数 |
| `ReactionFormula.cs` | 三类反应的元素精通项，以及增幅倍率、激化加算、结晶护盾 |

## 为什么是纯函数

扣血、施加效果、发布表现信号全部由调用方在拿到结果之后执行。这样做换来两件事：

1. **数值验收可以自动化**。21 条单测直接对着 `Docs/Design/Combat/05` 的验收用例写，不需要构造 Entity、加载配表或跑起一局。
2. **公式与流程解耦**。伤害管线的顺序调整不会改变公式，公式的数值调整不会影响管线。

因此两条约束不可放松：

- **计算器内不得有随机**。暴击由调用方掷骰后以 `IsCritical` 传入。
- **Context 只携带已解析的数值**。「按哪个属性缩放」「抗性从哪些 Modifier 汇总」都是接线层的职责，计算器只做算术。

## 接线

`DamagePipeline` 是从「倍率 × 属性」到「目标实际承受的数字」的唯一路径，由 `EffectSystem` 的 `DamageOperation.Execute` 调用。

三层职责互不重叠：

| 层 | 负责 | 不负责 |
| --- | --- | --- |
| `IElementSystem` | 附着与反应**身份** | 任何数值 |
| `ReactionFormula` / `DamageCalculator` | 纯算术 | 实体、配表 |
| `DamagePipeline` | 取数与选槽，串起前两者 | 写目标生命值 |

管线自己没有副作用（不扣血、不发信号），因此整条链路可以脱离 Effect 资产单测，见 `Tests/Editor/DamagePipelineTests.cs`。

### 按元素选槽

伤害加成、抗性与抗性削减在 05A 里是三十余个独立属性位，读哪一个只能在运行时按本次伤害的元素决定。这个映射集中在 `ElementPropertySlots`，避免每个消费方各写一份 switch。

### 等级

敌人目前没有等级组件，敌我一律按 `DamagePipeline.DefaultLevel`（1 级）参与防御区，使防御区退化为与双方等级无关的常数 0.5，而不是悄悄给出错误的等级压制。敌人等级表属于 `Docs/Design/Combat/90_现有实现差异台账.md` 排期第 7 步。
