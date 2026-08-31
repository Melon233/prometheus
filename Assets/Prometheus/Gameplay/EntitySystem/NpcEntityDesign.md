# NpcEntity 设计说明

## 组成

建议使用 `NpcEntity : PoiEntity`，并组合以下运行时对象：

- `NpcComponent`：NPC ID、定义引用和当前运行时字段。
- `NpcLogic`：行为、交互入口和状态转换。
- `NpcRuntimeState`：解锁、离场、当前阶段等可持久化状态。

## NpcLogic 职责

NpcLogic 只处理 NPC 领域逻辑：是否允许交互、选择哪个对话入口、发布 NPC 状态事件、请求 InteractionCoordinator 开始会话。它不直接调用 CameraSystem、FilmSystem 或具体 UI。

## 交互流程

```text
玩家进入范围 -> NpcLogic.TryBeginInteraction
-> InteractionCoordinator 创建会话
-> FilmSystem 播放演出
-> DialogueSystem 显示对话
-> 返回 DialogueResult
-> NpcLogic 处理结果并发布领域事件
```

## 取消条件

玩家离开范围、受击、死亡、场景卸载、NPC 回收和任务切换都可以取消会话。取消必须通过会话 CancellationToken 传递，不能只关闭 UI 而留下 Film 实例。

## 任务连接

NpcLogic 可以读取任务系统提供的只读任务视图，用于选择可用入口；任务推进通过 `DialogueCompleted`、`NpcStateChanged` 等事件完成，避免任务系统反向调用具体 NpcLogic。
