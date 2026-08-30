// Package poi 定义大世界 POI 的服务器领域模型：完整 POI 记录（定义 + 按类型的状态子文档）、类型/操作常量与策划导出读取。
// 领域模型与线上协议（gen/protocol）解耦，netx 层负责两者互转，存储层负责持久化。
package poi

// Vec3 三维坐标，与 Unity Vector3 序列化格式一致。
type Vec3 struct {
	X float32 `bson:"x" json:"x"`
	Y float32 `bson:"y" json:"y"`
	Z float32 `bson:"z" json:"z"`
}

// Quat 四元数旋转，与 Unity Quaternion 序列化格式一致。
type Quat struct {
	X float32 `bson:"x" json:"x"`
	Y float32 `bson:"y" json:"y"`
	Z float32 `bson:"z" json:"z"`
	W float32 `bson:"w" json:"w"`
}

// StatueState 七天神像的状态。
type StatueState struct {
	Unlocked bool    `bson:"unlocked"`
	Level    int32   `bson:"level"`
	Progress float32 `bson:"progress"`
}

// AnchorState 传送锚点的状态。
type AnchorState struct {
	Unlocked bool `bson:"unlocked"`
}

// ChestState 宝箱的状态。
type ChestState struct {
	Opened bool `bson:"opened"`
}

// SpiritCoreState 神瞳的状态。
type SpiritCoreState struct {
	Collected bool `bson:"collected"`
}

// GatheringState 采集物的状态。
type GatheringState struct {
	RespawnAt int64 `bson:"respawn_at"` // 下次重生时间戳（Unix 毫秒，0=可用）
}

// DungeonState 副本的状态。
type DungeonState struct {
	Unlocked bool  `bson:"unlocked"`
	Progress int32 `bson:"progress"`
}

// MapBossState 地图 Boss 的状态。
type MapBossState struct {
	RespawnAt int64 `bson:"respawn_at"` // 下次重生时间戳（Unix 毫秒，0=可用）
}

// Poi 是服务器数据库中一个 POI 的完整记录（静态定义 + 按类型的状态子文档）。
// 状态按类型建模：仅该类型对应的状态指针非 nil 才写入，避免无关字段冗余，同时保留字段可见性。
type Poi struct {
	ID       string `bson:"_id" json:"Id"`
	Retired  bool   `bson:"retired" json:"-"` // 当前导出已删除的 POI；保留历史记录但不再同步或允许交互
	Region   string `bson:"region" json:"Region"`
	PoiType  int32  `bson:"poi_type" json:"PoiType"` // 0..7，见下方 PoiType* 常量
	Position Vec3   `bson:"position" json:"Position"`
	Rotation Quat   `bson:"rotation" json:"Rotation"`
	ChunkID  int32  `bson:"chunk_id" json:"ChunkId"` // 空间分区，用于按区块查询

	Statue     *StatueState     `bson:"statue,omitempty"`
	Anchor     *AnchorState     `bson:"anchor,omitempty"`
	Chest      *ChestState      `bson:"chest,omitempty"`
	SpiritCore *SpiritCoreState `bson:"spirit_core,omitempty"`
	Gathering  *GatheringState  `bson:"gathering,omitempty"`
	Dungeon    *DungeonState    `bson:"dungeon,omitempty"`
	MapBoss    *MapBossState    `bson:"map_boss,omitempty"`
}

// Clone 返回完整 POI 的独立快照，避免网络序列化或调用方读取时与权威状态写入产生数据竞争。
func (p *Poi) Clone() *Poi {
	if p == nil {
		return nil
	}
	clone := *p
	if p.Statue != nil {
		state := *p.Statue
		clone.Statue = &state
	}
	if p.Anchor != nil {
		state := *p.Anchor
		clone.Anchor = &state
	}
	if p.Chest != nil {
		state := *p.Chest
		clone.Chest = &state
	}
	if p.SpiritCore != nil {
		state := *p.SpiritCore
		clone.SpiritCore = &state
	}
	if p.Gathering != nil {
		state := *p.Gathering
		clone.Gathering = &state
	}
	if p.Dungeon != nil {
		state := *p.Dungeon
		clone.Dungeon = &state
	}
	if p.MapBoss != nil {
		state := *p.MapBoss
		clone.MapBoss = &state
	}
	return &clone
}

// POI 类型常量，数值与客户端 PoiType 及 proto PoiType 一一对应。
const (
	PoiTypeTeleAnchor  int32 = 0 // 传送锚点
	PoiTypeStatue      int32 = 1 // 七天神像
	PoiTypeChest       int32 = 2 // 宝箱
	PoiTypeSpiritCore  int32 = 3 // 神瞳
	PoiTypeGathering   int32 = 4 // 采集物
	PoiTypeDungeon     int32 = 5 // 副本
	PoiTypeMapBoss     int32 = 6 // 地图 Boss
	PoiTypeMonsterCamp int32 = 7 // 怪物营地
)

// POI 交互操作常量，数值与客户端 PoiOp 及 proto PoiOp 一一对应。
const (
	PoiOpUnlock      int32 = 0 // 解锁类：传送锚点 / 七天神像 / 副本
	PoiOpOpenChest   int32 = 1 // 开启宝箱
	PoiOpCollectCore int32 = 2 // 收集神瞳
	PoiOpGather      int32 = 3 // 采集（可刷新，重复成功）
	PoiOpDefeat      int32 = 4 // 击败地图 Boss（可刷新，重复成功）
	PoiOpOfferStatue int32 = 5 // 七天神像供奉：消耗风神瞳推进进度，升级发长剑
)

// 可刷新类型的重生周期（秒），与客户端默认配置一致。
const (
	GatheringRespawnSeconds int64 = 30  // 采集物
	MapBossRespawnSeconds   int64 = 300 // 地图 Boss
)
