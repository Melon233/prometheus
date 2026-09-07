# EventKit 全局事件规范

`IEventKit` 是 Framework、Gameplay 与 UI 之间广播全局玩法事实的唯一事件总线。正式入口为 `Core.Event`，具体 `EventKit` 由 `Core` 创建、发布静态入口并释放；不存在任何与之平行的静态事件通道。

## 路由键就是载荷类型

总线以**事件载荷的具体类型**作为唯一路由键，不存在集中枚举：

```csharp
Core.Event.AddListener<PoiOpenedEvent>(OnPoiOpened);
Core.Event.Invoke(new PoiOpenedEvent(poiId));
Core.Event.RemoveListener<PoiOpenedEvent>(OnPoiOpened);
```

这样做的直接结果是：**新增一个全局事件不需要修改本模块**。载荷类型定义在它所属的领域目录里（例如 POI 事件在 `Gameplay/PoiSystem/PoiEvents.cs`，小队事件在 `Gameplay/TeamSystem/TeamEvents.cs`），框架层因此不认识任何玩法领域概念。

方法名与实体内 `EventComponent` 保持一致，使"全局事实"和"实体内事实"的调用形状完全相同：

```csharp
entity.Get<EventComponent>().Invoke(new DieEvent());   // 只通知该实体自己的 Logic
Core.Event.Invoke(new EntityDiedEvent(entityId));      // 通知不持有该实体的其他 System
```

## 使用规则

- 载荷必须实现 `IEvent`，构造时即形成完整快照并保持不可变；事件表达已经发生的事实，不用于同步请求/响应。
- 订阅者必须在自身释放或失活边界，用**同一个委托实例**对称调用 `RemoveListener`。`Dispose` 只负责最终清空，不能替代订阅者自己的生命周期管理。
- 对象内部、生命周期明确的点对点通知仍可使用 C# `event`（网络推送、Film 会话回调、Component 变更通知）；它不能承担跨模块全局广播职责。
- 发布一个当前没有任何监听者的事件是合法的空操作。POI 的 `PoiGatheredEvent` 与 `PoiDefeatedEvent` 目前没有发布者，因为对应 POI 类型的逻辑尚未实现；它们与已接入的三个 POI 事件构成同一套分类，保留在 PoiSystem 中。

## 投递语义

- **同步**：`Invoke` 在调用线程上按订阅顺序依次通知，返回时全部监听器已执行完毕。
- **快照**：发布前捕获当前监听器数组。监听器在回调中订阅或退订不会影响本次投递——新订阅从下一次发布开始生效，本次退订仍会收到通知。
- **零分配**：监听器数组采用写时复制，订阅/退订各分配一次（只发生在生命周期边界），逐帧发布不产生任何分配。
- **异常隔离**：单个监听器抛出的异常会被记录（`Debug.LogException`）并隔离，同一事件的其余监听器仍然完成投递。全局事实的订阅者互不相识，让其中一个的失败静默吞掉其余订阅者的投递，会产生比异常本身更难定位的状态不一致。

完整硬约束见项目级 `Docs/ArchSpec.md`。
