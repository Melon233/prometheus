# 其他文档

不属于架构规范（`Docs/Arch`）、工作流（`Docs/Workflow`）或策划案（`Docs/Design`）的文档放在这里。

当前为空。

## 放置原则

先判断能否归入既有三类，不能再放本目录：

| 目录 | 收什么 |
| --- | --- |
| `Docs/Arch` | 架构硬约束、分层与依赖规则、游戏流程编排等**约束性**文档 |
| `Docs/Workflow` | 配表管线、工具链、环境搭建等**操作性**文档 |
| `Docs/Design` | 玩法策划案，按系统模块分子目录 |
| `Docs/Other` | 以上都不属于的：调研记录、会议纪要、一次性方案比选等 |

**单个系统的设计与实现文档不放在 `Docs/` 下**，而是与代码同目录（例如 `Assets/Prometheus/Gameplay/PoiSystem/PoiSystem.md`），这样改代码时文档就在手边，符合 `ARCH-DOC-001` 的同步要求。
