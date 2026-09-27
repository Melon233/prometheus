# BagSystem

背包由**网关权威**：`Item { itemId, quality, quantity }` 由 `IBagGateway` 返回，BagSystem 只持有最近一次同步的快照，不拥有权威数量。

- **单机版（当前）**：`LocalBagGateway` 直接读写内存中的背包 `LocalAuthority/LocalInventory`（背包规则与数据都在这里，**不做存档**，每次进入游戏背包从空开始），物品定义在 `LocalAuthority/LocalItemCatalog`（与 `Server/config/items.json` 一致）。POI 交互掉落经同一个 `LocalInventory` 写入，因此背包读到的就是交互发放的物品。
- **联网版（保留未接入）**：`NetworkBagGateway`（`Gameplay/ServiceSystem/`，装配 `Prometheus.Gameplay.Online`）经 `IServiceSystem` 组包收发，数据持久化在服务器 MongoDB 的 `backpack` 集合。

## 组成

| 文件 | 职责 |
| --- | --- |
| `IBagSystem` / `BagSystem` | 背包快照缓存、修订通知、刷新入口、持有量查询与消耗校验 |
| `IBagGateway` | 本领域权威数据契约：取背包快照、抽卡 |
| `LocalBagGateway` | 单机实现：读写本地存档；抽卡消耗 1 个风神瞳，从其余物品中等概率发放 1 个（移植自服务器 `Service.Gacha`） |
| `MaterialCost.cs` | 材料消耗的数据类型与纯函数校验器 |

## 持有量查询

`GetQuantity(materialId)` 按物品标识返回持有总量，**同一标识的不同品质合并计数**。索引在每次刷新快照后重建一次，查询不遍历背包。

未持有返回 0——「没有这件材料」与「持有 0 个」在背包语义上是同一件事，不区分。

## 消耗校验

`CheckCost(costs)` 校验一组材料消耗能否支付，返回 `MaterialCostCheck`。

**它只回答「够不够」，不扣除任何东西。**

- 扣减只能由网关执行；本地校验的作用是在发请求前挡掉明显不成立的操作，并给出缺口明细供界面展示。
- 校验基于最近一次同步的快照，因此结果是**建议而非承诺**：最终成败以网关响应为准。界面不得因为校验通过就提前播放成功表现。
- 不满足时返回**全部**缺口而不是第一条，使界面能一次性展示「还缺什么、差多少」。
- 同一材料在消耗列表中出现多次时按**累计需求**判定。「分两条各自够、合起来不够」是最容易漏判的一种情况。

校验逻辑放在 `MaterialCostEvaluator` 这个纯静态类里，只依赖一个 `Func<string,int>` 持有量查询委托，因此可以脱离背包、网络与配表单独验证。测试见 `Tests/Editor/MaterialCostEvaluatorTests.cs`。

`MaterialCost` 的数量必须为正：零消耗条目是配表错误，应当从消耗列表中省略，而不是以 0 参与计算，因此构造时直接抛出。

## 尚未实现：权威扣减

养成操作（突破、天赋升级、武器突破、圣遗物强化）的材料扣减**尚未接通**。它需要：

1. `IBagGateway` 增加对应的扣减方法，由养成 System 调用；
2. 单机版在 `LocalBagGateway` 中经 `LocalInventory.Consume` 实现，保证「校验—扣除—生效」在一次调用内完成；
3. 恢复联网时再在 `Server/proto/game.proto` 与 Go 服务端补齐同一接口，并由 `NetworkBagGateway` 组包。

在此之前，养成侧只能做到「校验通过则允许提交」，不能真正消耗材料。这一段属于 [Growth 差异台账](../../../../Docs/Design/Growth/90_现有实现差异台账.md) 排期的第 3 步后半。

## 材料定义

材料的静态定义在配表 `TbMaterial`（`Data/Excel/Datas/Growth/material.xlsx`），包含分类、星级、品阶、合成链与经验值。背包本身不认识这些语义，它只管数量；需要分类或合成信息的模块自行读表。
