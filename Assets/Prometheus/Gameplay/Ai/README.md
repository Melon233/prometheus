# 敌人 AI 状态机库

## 定位

本目录是一个**纯 C# 状态机库**：不引用 `Entity`、`Component`、`Logic`，也不依赖 Unity 生命周期函数，因此可以完全脱离实体模型单独测试。

它与 NarrativeSystem 的 `Core/` + `Ports/` 采用同一种结构：

| 角色 | 类型 | 位置 |
| --- | --- | --- |
| 状态机内核 | `EnemyAiBrain`、`EnemyAiBlackboard` | 本目录 |
| 配置资产 | `EnemyAiDefinition`（状态 / 迁移 / 条件 / 动作定义） | 本目录 |
| 端口 | `IEnemyAiAgent` | 本目录（声明于 `EnemyAiBrain.cs`） |
| 端口实现（适配器） | `EnemyAiLogic` | `EntitySystem/Monster/Logic/` |
| 实体侧数据 | `EnemyAiComponent` | `EntitySystem/Monster/Component/` |

## 为什么 Component 和 Logic 不在这里

`EnemyAiComponent` 和 `EnemyAiLogic` 是 Entity 的组成部分——它们没有独立生命周期，由 `SlimeEntity` 在构造阶段注册，随实体回收而销毁。按项目的 ELC 所有权模型，这类类型必须与 Entity 同处 `EntitySystem`。

留在本目录的是**能脱离 Entity 独立存在**的部分：状态机本身只认识 `IEnemyAiAgent`，不关心驱动它的是一只史莱姆、一个测试替身还是一个编辑器预览器。

## 依赖方向

```text
EntitySystem/Monster/Logic/EnemyAiLogic  ──实现──▶  IEnemyAiAgent
                    │                                    ▲
                    └────────持有并驱动────────▶  EnemyAiBrain
```

本目录不反向引用 `EntitySystem`。新增 AI 能力时，若需要读写实体状态，应扩展 `IEnemyAiAgent` 端口，由 `EnemyAiLogic` 实现，而不是让 Brain 直接认识 Component。

项目级硬约束见 `Docs/ArchSpec.md`。
