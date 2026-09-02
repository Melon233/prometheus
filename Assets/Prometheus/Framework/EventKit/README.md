# EventKit 全局事件规范

`IEventKit` 是 Framework、Gameplay 与 UI 之间广播全局玩法事实的唯一事件总线。正式入口为 `Core.Event`，具体 `EventKit` 由 `Core` 创建和释放；旧的静态事件总线已删除。

## 使用规则

- 发布者使用 `Core.Event.Invoke(Event.Xxx, payload)`，订阅者使用相同的 `Event` 键和载荷泛型。
- 载荷必须实现 `IEvent`，构造后保持不可变；事件表达已经发生的事实，不用于同步请求/响应。
- 每个 `Event` 枚举值只能对应一种载荷类型，禁止同一键混用无参和不同泛型载荷。
- 订阅者必须在自身释放或失活时用同一个委托对称调用 `RemoveListener`。
- 对象内部、生命周期明确的点对点通知仍可使用 C# `event`；它不能替代跨模块全局事件。

当前 POI 事件 `PoiUnlocked`、`PoiOpened`、`PoiCollected`、`PoiGathered` 和 `PoiDefeated` 已纳入 `Event` 枚举，载荷位于 `Gameplay/WorldSystem/PoiEvents.cs`。目前代码只在对应行为实际发生的位置发布已经接入的事件，未接入的采集/击败发布点不制造占位通知。

完整硬约束见项目级 `Docs/ArchSpec.md`。
