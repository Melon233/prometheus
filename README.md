# prometheus

Unity 6（6000.3.10f1）开放世界动作游戏项目。

## 从哪里开始读

| 你想了解 | 读这里 |
| --- | --- |
| **架构硬约束**（必读） | [`Docs/ArchSpec.md`](Docs/ArchSpec.md) |
| 启动链路、分层与组合根 | [`Assets/Prometheus/Framework/CoreKit/README.md`](Assets/Prometheus/Framework/CoreKit/README.md) |
| Entity / Logic / Component 模型 | [`Assets/Prometheus/Framework/GameplayKit/README.md`](Assets/Prometheus/Framework/GameplayKit/README.md) |
| Kit 与 System 的服务边界 | [`Assets/Prometheus/Framework/GameplayKit/SystemBoundaryDesign.md`](Assets/Prometheus/Framework/GameplayKit/SystemBoundaryDesign.md) |
| 全局事件总线 | [`Assets/Prometheus/Framework/EventKit/README.md`](Assets/Prometheus/Framework/EventKit/README.md) |
| 网络边界 | [`Assets/Prometheus/Framework/NetworkKit/README.md`](Assets/Prometheus/Framework/NetworkKit/README.md) |
| 各玩法系统 | 对应 `Assets/Prometheus/Gameplay/<System>/README.md` |
| 协作与代码风格约定 | [`AGENTS.md`](AGENTS.md) |

## 运行时分层

运行时代码划分为四个装配，依赖方向由 asmdef 强制，编译器不允许反向引用：

```text
Runtime（Bootstrap，唯一组合根：Entry + PrometheusSystemInstaller）
  └─ Prometheus.UI          面板与 HUD 命令
       └─ Prometheus.Gameplay   全部玩法 System、Entity、Logic、Component
            └─ Prometheus.Framework   Core / Kit / 事件 / 资源 / UI 框架 / ELC 基类
```

`Prometheus.NetworkKit` 与 `Prometheus.Rendering.*` 是独立的边界装配。

框架层不认识任何玩法领域概念；"这一局由哪些 System 组成"由唯一组合根 [`Assets/Prometheus/Bootstrap/PrometheusSystemInstaller.cs`](Assets/Prometheus/Bootstrap/PrometheusSystemInstaller.cs) 回答。

## 入口

`Assets/Resources/Entry.unity` 是 Player Build 的唯一入口场景，其 `Entry` 组件没有任何序列化字段。

启动链路不传递参数：资源包名由 `AssetKit` 固定，每个资源地址由使用它的系统以常量持有，跨场景运行时对象统一挂在 `PersistentRoot.Shared` 下。

## 构建与验证

```bash
# 全量编译
dotnet build prometheus.slnx --no-restore

# 架构约束（Unity Test Runner → EditMode）
#   Prometheus.Architecture.EditorTests
```

架构约束以可执行测试的形式存在于 [`Assets/Prometheus/Tests/Architecture/`](Assets/Prometheus/Tests/Architecture/)。修改成型系统的链路时，必须同步更新对应中文文档与 `Docs/ArchSpec.md`（见 `ARCH-DOC-001`）。
