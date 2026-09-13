# ReactionProductSystem

把 `IElementSystem` 广播的伪元素状态变化，落实为玩家看得见的后果。

## 为什么需要单独一层

元素系统只知道「目标身上有一层冻结，还剩 3.2 秒」。让目标真的动不了、让草原核到期时炸出草伤，都要读属性面板、施加 Effect、结算伤害——这些一旦塞进元素系统，它就变成了第二个伤害计算器。

因此三条职责线在这里汇合，各自仍然只做自己那一件事：

| 层 | 负责 | 不负责 |
| --- | --- | --- |
| `IElementSystem` | 状态与它的时钟，只广播事实 | 属性面板、Effect、伤害 |
| `EffectDefinition` 资产 | 表现：Buff 图标、控制状态 | 时长 |
| `DamagePipeline` / `DamageSettlement` | 数值与落地 | 状态生命周期 |

## 产物 Effect 一律是 Permanent

产物的存活时间由元素系统的状态决定，由本系统在状态消失时撤下。在资产上再写一份时长会造成两个时钟，一旦不同步就会出现「状态没了但还冻着」——这类偏差在实机里表现为偶发的操作失灵，极难复现。

| 状态 | 产物 | 承载 |
| --- | --- | --- |
| `Frozen` | `Eff_Frozen` | `ControlStateModifierOperation(Stun)` |
| `Quickened` | `Eff_Quicken` | 仅 Buff 标记 |
| `DendroCore` | `Eff_Bloom_Core` | 仅 Buff 标记 |

产物是**可选**的：原激化即使没有配 Effect 也照常提供激化反应，因为激化的数值全部来自反应矩阵。

## 到期与被消耗必须分开

草原核自然到期要引爆；被超绽放或烈绽放消耗时，那一笔伤害由**那次反应自己**作为剧变反应结算。两者合并会让同一个核打出两笔伤害。

`PseudoStateChange` 因此有三个取值而不是两个：`Applied` / `Expired` / `Consumed`。实体死亡或回收触发的清场按 `Consumed` 处理——清场不是引爆。

## 引爆走的是同一条剧变公式

`DamagePipeline.ResolveTransformative` 被抽成公开入口，正是为了让引爆复用它。引爆发生在命中之后很久，届时已经没有攻击上下文，因此状态必须记住创建它的施加者编号（`ElementAura.SourceEntityId`）。

引爆自成一条因果链，不挂在当初那次命中下面，否则因果深度会无限增长。

「哪些状态会引爆」由配表决定而不是按状态键写死：只有反应行的 `damageElement` 不为 `None` 且 `multiplier` 为正时才引爆。冻结与原激化的对应行 `damageElement` 是 `None`，到期时静静消失。

## 尚未实现

- **结晶护盾**。`Eff_Crystallize_*` 需要一套护盾吸收机制（伤害先扣盾再扣血），项目里还没有。`ReactionFormula.CrystallizeShield` 已就位，缺的是承载它的运行时。
- **表现**。三个产物都没有配 Buff 图标与特效。
- **草原核实体化**。详见 [04A 第 5 节](../../../../Docs/Design/Combat/04A_反应矩阵配表.md)。
