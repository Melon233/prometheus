# 玩家聚合数据库

当前服务端使用 MongoDB 的 `players` 集合保存玩家聚合数据。每个玩家对应一个文档，文档主键为 `playerId`，账号信息、背包物品和个人 POI 状态都位于该文档内。

## 文档结构

```json
{
  "_id": "player-001",
  "schema_version": 1,
  "account": {
    "platform": "android",
    "uid": "player-001",
    "created_at": "2026-08-30T00:00:00Z",
    "last_login_at": "2026-08-30T12:00:00Z"
  },
  "inventory": [
    {
      "player_id": "player-001",
      "item_id": "Anemoculus",
      "quality": 1,
      "quantity": 3
    }
  ],
  "pois": [
    {
      "_id": "Mond_Chest_1",
      "poi_type": 2,
      "chest": { "opened": true }
    }
  ],
  "position": {
    "player_id": "player-001",
    "x": 12.5,
    "y": 0.0,
    "z": 8.25,
    "server_time_ms": 1788029000000
  },
  "revision": 12,
  "created_at": "2026-08-30T00:00:00Z",
  "updated_at": "2026-08-30T12:00:00Z"
}
```

## 读写规则

- `MongoPlayerStore` 是唯一的 Mongo 聚合存储实现，负责加载和替换完整玩家文档。
- `PlayerItemStore` 和 `PlayerPoiStore` 只是业务接口适配器，不会创建额外的背包或 POI 集合。
- 新玩家首次访问时自动创建空聚合文档，并使用策划导出模板初始化个人 POI 状态。
- 背包数量为零的物品栈会从 `inventory` 数组移除，避免无效数据长期增长。
- POI 状态按玩家缓存和持久化，玩家之间不会共享宝箱、神瞳或神像进度。
- `revision` 在每次聚合替换时递增，后续接入并发写保护时可作为乐观锁版本号。
- 客户端将稳定 `player_id` 保存到 Unity `PlayerPrefs`，重新启动或重连时才能命中同一个玩家文档并恢复位置。

## 规模边界

该方案适用于当前数据规模：玩家文档字段数量和背包规模均受控，读取玩家状态时可以一次加载。MongoDB 单文档上限为 16 MB；如果未来装备实例、任务历史或日志导致玩家文档接近上限，应将对应高增长数据迁移到独立集合，再保留摘要字段在玩家文档中。

## 启动参数

服务端使用 `-players` 指定玩家集合名，默认值为 `players`。`-coll` 和 `-backpack` 不再参与默认启动链路。

首次启动新版本时，会检查旧的 `poi_states` 和 `backpack` 集合；默认玩家的 POI 状态和所有旧背包玩家数据会自动导入 `players`。迁移成功后旧集合会被删除，迁移失败则不会执行删除。
