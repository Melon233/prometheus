# PrometheusCS 分层架构 Demo

该目录实现一个可运行的 WASD 方块移动 Demo，并用程序集引用约束四个职责层。

## 运行方式

1. 在 Unity 菜单执行 `PrometheusCS/Create WASD Cube Demo Scene`。
2. 打开 `Assets/PrometheusCS/Demo/PrometheusCSDemo.unity`。
3. 进入 Play Mode。
4. 使用 `WASD` 控制方块在 XZ 平面移动。

## 数据流

```text
Unity Keyboard Input
        ↓ MovePlayerCommand
PlayerMovementSimulation
        ↓ PlayerMovementSnapshot
CubePlayerPresenter
        ↓
CubePlayerView / GameObject
```

## 分层职责

- `Simulate`：纯 C# 权威状态与移动规则，不引用 Unity API，程序集启用 `noEngineReferences`。
- `Engine`：封装 `Input` 和 `Time`，把 Unity 数据转换为模拟层 Command 或普通数值。
- `Presentation`：负责 GameObject、Transform 和 HUD，只消费模拟层快照。
- `Bootstrap`：组合各层并驱动单向数据流，不承载移动业务规则。
- `Editor`：生成包含方块、地面、相机、灯光和 HUD 的独立 Demo 场景。
- `Tests`：在 EditMode 中验证移动规则、对角速度和纯程序集边界。

## 依赖规则

```text
PrometheusCS.Bootstrap -> PrometheusCS.Engine
PrometheusCS.Bootstrap -> PrometheusCS.Presentation
PrometheusCS.Bootstrap -> PrometheusCS.Simulation
PrometheusCS.Engine -> PrometheusCS.Simulation
PrometheusCS.Presentation -> PrometheusCS.Simulation
PrometheusCS.Simulation -> System only
```

模拟层不会调用资产、UI、音频或 GameObject。需要一次性表现时，模拟层应发布领域事件；需要持续表现时，模拟层应发布不可变快照。外层只能通过 Command 请求状态变化，不能直接修改模拟层状态。
