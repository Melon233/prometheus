// Package service 实现 POI 权威逻辑：启动播种、全量/按区块同步与交互校验。
// 服务器是权威：客户端仅在 Interact 返回成功后（success=true）才做表现。
package service

import (
	"context"
	"fmt"
	"math/rand"
	"strings"
	"sync"
	"time"

	"prometheus/internal/item"
	"prometheus/internal/poi"
	"prometheus/internal/room"
	"prometheus/internal/store"
)

// Service 维护 POI 记录的内存索引（byID / byChunk），并由 store 持久化。
type Service struct {
	mu          sync.RWMutex
	store       store.Store
	inventory   *Inventory
	inventories map[string]*Inventory
	itemConfig  *item.Config
	byID        map[string]*poi.Poi
	byChunk     map[int32][]*poi.Poi
	playerPois  map[string]map[string]*poi.Poi
	randomMu    sync.Mutex
	random      *rand.Rand
}

// PlayerPoiStore 是可选的玩家作用域 POI 存储扩展；旧的 Store 实现仍可用于兼容内存测试。
type PlayerPoiStore interface {
	LoadAllForPlayer(ctx context.Context, playerID string) ([]*poi.Poi, error)
	UpsertForPlayer(ctx context.Context, playerID string, target *poi.Poi) error
}

// PlayerPositionStore 是可选的玩家坐标持久化扩展，由网络层的定时保存任务调用。
type PlayerPositionStore interface {
	LoadPlayerPosition(ctx context.Context, playerID string) (room.Position, bool, error)
	UpsertPlayerPosition(ctx context.Context, playerID string, position room.Position) error
}

// New 创建 POI 权威服务。
func New(st store.Store, inv *Inventory, itemConfig *item.Config) *Service {
	inventories := make(map[string]*Inventory)
	if inv != nil {
		inventories[inv.player] = inv
	}
	return &Service{store: st, inventory: inv, inventories: inventories, itemConfig: itemConfig, byID: make(map[string]*poi.Poi), byChunk: make(map[int32][]*poi.Poi), playerPois: make(map[string]map[string]*poi.Poi), random: rand.New(rand.NewSource(time.Now().UnixNano()))}
}

// Seed 从存储加载已有记录，并按 UUID 导出配置执行整批同步；缺失于本次导出的合法 UUID 会标记为退役并保留历史状态。
func (s *Service) Seed(ctx context.Context, exported []poi.ExportItem) error {
	// 先完整校验导出批次，任何错误都不得造成部分播种或部分更新。
	exportedIDs := make(map[string]struct{}, len(exported))
	for index, item := range exported {
		if !poi.IsUUID(item.ID) {
			return fmt.Errorf("poi export item %d has invalid UUID %q", index, item.ID)
		}
		if _, exists := exportedIDs[item.ID]; exists {
			return fmt.Errorf("poi export contains duplicate UUID %q", item.ID)
		}
		exportedIDs[item.ID] = struct{}{}
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	pois, err := s.store.LoadAll(ctx)
	if err != nil {
		return err
	}
	// 重建索引，避免重复 Seed 后旧 chunk 索引和玩家模板残留。
	s.byID = make(map[string]*poi.Poi, len(pois)+len(exported))
	s.byChunk = make(map[int32][]*poi.Poi)
	s.playerPois = make(map[string]map[string]*poi.Poi)
	for _, p := range pois {
		if p == nil {
			continue
		}
		// 当前数据已切换到 UUID，旧临时主键不再参与运行时索引，也不迁移。
		if !poi.IsUUID(p.ID) {
			continue
		}
		s.byID[p.ID] = p
		if !p.Retired {
			s.byChunk[p.ChunkID] = append(s.byChunk[p.ChunkID], p)
		}
	}
	for _, item := range exported {
		if existing, ok := s.byID[item.ID]; ok {
			// 已存在：更新静态定义字段，保留状态 bool，避免移动/改配置丢失玩家进度。
			oldChunk := existing.ChunkID
			wasRetired := existing.Retired
			existing.Region = item.Region
			existing.PoiType = item.PoiType
			existing.Position = item.Position
			existing.Rotation = item.Rotation
			existing.ChunkID = item.ChunkID
			existing.Retired = false
			if wasRetired || oldChunk != existing.ChunkID { // 退役恢复或 chunk 变化时同步调整 chunk 索引
				s.byChunk[oldChunk] = removePoiFromChunk(s.byChunk[oldChunk], existing)
				s.byChunk[existing.ChunkID] = append(s.byChunk[existing.ChunkID], existing)
			}
			if err := s.store.Upsert(ctx, existing); err != nil {
				return err
			}
			continue
		}
		p := &poi.Poi{
			ID:       item.ID,
			Region:   item.Region,
			PoiType:  item.PoiType,
			Position: item.Position,
			Rotation: item.Rotation,
			ChunkID:  item.ChunkID,
			Retired:  false,
		}
		initState(p)
		s.byID[p.ID] = p
		s.byChunk[p.ChunkID] = append(s.byChunk[p.ChunkID], p)
		if err := s.store.Upsert(ctx, p); err != nil {
			return err
		}
	}
	// 导出中消失的 POI 进入退役状态，保留数据库记录和历史状态但不再进入活动 chunk。
	for id, existing := range s.byID {
		if _, exists := exportedIDs[id]; exists {
			continue
		}
		if existing.Retired {
			continue
		}
		existing.Retired = true
		s.byChunk[existing.ChunkID] = removePoiFromChunk(s.byChunk[existing.ChunkID], existing)
		if err := s.store.Upsert(ctx, existing); err != nil {
			return err
		}
	}
	return nil
}

// removePoiFromChunk 从 chunk 的 POI 切片中移除目标记录（POI 移动到其它 chunk 时调用）。
func removePoiFromChunk(list []*poi.Poi, target *poi.Poi) []*poi.Poi {
	for i, p := range list {
		if p == target {
			return append(list[:i], list[i+1:]...)
		}
	}
	return list
}

// PullAll 返回全部 POI 状态快照（全量同步 / 调试用）。
func (s *Service) PullAll() []*poi.Poi {
	s.mu.RLock()
	defer s.mu.RUnlock()
	pois := s.byID
	out := make([]*poi.Poi, 0, len(pois))
	for _, p := range pois {
		if p == nil || p.Retired {
			continue
		}
		out = append(out, p.Clone())
	}
	return out
}

// PullAllForPlayer 返回指定玩家的全部个人 POI 状态；玩家首次请求时从聚合文档加载并补齐静态模板。
func (s *Service) PullAllForPlayer(ctx context.Context, playerID string) ([]*poi.Poi, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	pois, err := s.playerPoisLocked(ctx, playerID)
	if err != nil {
		return nil, err
	}
	out := make([]*poi.Poi, 0, len(pois))
	for _, p := range pois {
		if p == nil || p.Retired {
			continue
		}
		out = append(out, p.Clone())
	}
	return out, nil
}

// PullChunk 返回指定 chunk 内的 POI 状态；chunk 无数据时返回空切片。
func (s *Service) PullChunk(chunkID int32) []*poi.Poi {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if list, ok := s.byChunk[chunkID]; ok {
		out := make([]*poi.Poi, 0, len(list))
		for _, p := range list {
			if !p.Retired {
				out = append(out, p.Clone())
			}
		}
		return out
	}
	return nil
}

// PullChunkForPlayer 返回指定玩家在目标区块内的个人 POI 状态，确保同步与后续交互读取同一玩家快照。
func (s *Service) PullChunkForPlayer(ctx context.Context, playerID string, chunkID int32) ([]*poi.Poi, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	pois, err := s.playerPoisLocked(ctx, playerID)
	if err != nil {
		return nil, err
	}
	out := make([]*poi.Poi, 0)
	for _, p := range pois {
		if p != nil && !p.Retired && p.ChunkID == chunkID {
			out = append(out, p.Clone())
		}
	}
	return out, nil
}

// GetItems 返回当前玩家全部物品（数量大于 0）。
func (s *Service) GetItems() []*item.Stack {
	if s.inventory == nil {
		return nil
	}
	return s.inventory.GetAll()
}

// GetItemsForPlayer 返回指定玩家背包；玩家首次出现时从持久化层按玩家 ID 懒加载。
func (s *Service) GetItemsForPlayer(ctx context.Context, playerID string) ([]*item.Stack, error) {
	inventory, err := s.inventoryForPlayer(ctx, playerID)
	if err != nil {
		return nil, err
	}
	return inventory.GetAll(), nil
}

// LoadPlayerPosition 读取玩家最近一次持久化坐标；未找到时返回 false。
func (s *Service) LoadPlayerPosition(ctx context.Context, playerID string) (room.Position, bool, error) {
	positionStore, ok := s.store.(PlayerPositionStore)
	if !ok {
		return room.Position{}, false, nil
	}
	return positionStore.LoadPlayerPosition(ctx, playerID)
}

// SavePlayerPosition 持久化玩家当前坐标；旧存储实现不支持坐标时保持兼容并忽略保存。
func (s *Service) SavePlayerPosition(ctx context.Context, playerID string, position room.Position) error {
	positionStore, ok := s.store.(PlayerPositionStore)
	if !ok {
		return nil
	}
	return positionStore.UpsertPlayerPosition(ctx, playerID, position)
}

// Interact 校验并应用一次交互：返回变更后的记录与是否成功。
// 一次性操作（解锁/开箱/收集）重复请求返回 false；可刷新操作（采集/击败）总是成功；供奉总是成功。
func (s *Service) Interact(ctx context.Context, id string, op int32) (*poi.Poi, bool) {
	return s.InteractForPlayer(ctx, "default", id, op)
}

// InteractForPlayer 以指定玩家身份执行 POI 交互，使未来多玩家背包和权限校验不会依赖全局默认玩家。
func (s *Service) InteractForPlayer(ctx context.Context, playerID, id string, op int32) (*poi.Poi, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	// UUID 是大小写无关的表示；仅对合法 UUID 规范化小写，保留临时语义 ID 的原始大小写。
	id = strings.TrimSpace(id)
	if poi.IsUUID(id) {
		id = strings.ToLower(id)
	}
	pois, err := s.playerPoisLocked(ctx, playerID)
	if err != nil {
		return nil, false
	}
	p, ok := pois[id]
	if !ok || p.Retired {
		return nil, false
	}
	if op == poi.PoiOpOfferStatue {
		inventory, err := s.inventoryForPlayerLocked(ctx, playerID)
		if err != nil {
			return p.Clone(), false
		}
		s.offerStatue(ctx, playerID, p, inventory) // 供奉：消耗风神瞳推进进度，升级发长剑
		return p.Clone(), true
	}
	if !applyToState(p, op) {
		return p.Clone(), false
	}
	_ = s.upsertPlayerPoi(ctx, playerID, p) // 权威变更后立即入库
	if inventory, err := s.inventoryForPlayerLocked(ctx, playerID); err == nil {
		s.grantItems(ctx, op, inventory) // 按操作发放掉落物品
	}
	return p.Clone(), true
}

// Gacha 消耗指定玩家一个 Anemoculus，并从物品配置中随机发放一个非 Anemoculus 道具。
func (s *Service) Gacha(ctx context.Context, playerID string) (*item.Stack, []*item.Stack, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	inventory, err := s.inventoryForPlayerLocked(ctx, playerID)
	if err != nil {
		return nil, nil, err
	}
	candidates := make([]item.Def, 0, len(s.itemConfig.Items))
	for _, definition := range s.itemConfig.Items {
		if definition.ID != item.IDAnemoculus {
			candidates = append(candidates, definition)
		}
	}
	if len(candidates) == 0 {
		return nil, inventory.GetAll(), fmt.Errorf("no gacha reward item configured")
	}
	consumed, err := inventory.Consume(ctx, item.IDAnemoculus, 1)
	if err != nil {
		return nil, inventory.GetAll(), err
	}
	if !consumed {
		return nil, inventory.GetAll(), fmt.Errorf("insufficient Anemoculus")
	}
	s.randomMu.Lock()
	rewardDefinition := candidates[s.random.Intn(len(candidates))]
	s.randomMu.Unlock()
	if err := inventory.Grant(ctx, rewardDefinition.ID, 1); err != nil {
		return nil, inventory.GetAll(), err
	}
	return &item.Stack{PlayerID: playerID, ItemID: rewardDefinition.ID, Quality: rewardDefinition.Quality, Quantity: 1}, inventory.GetAll(), nil
}

// grantItems 按交互操作发放掉落物品（品质取自配置）。
func (s *Service) grantItems(ctx context.Context, op int32, inventory *Inventory) {
	if inventory == nil {
		return
	}
	switch op {
	case poi.PoiOpOpenChest:
		_ = inventory.Grant(ctx, item.IDArmor, 1)
		_ = inventory.Grant(ctx, item.IDExpBook, 1)
	case poi.PoiOpCollectCore:
		_ = inventory.Grant(ctx, item.IDAnemoculus, 1)
	case poi.PoiOpGather:
		_ = inventory.Grant(ctx, item.IDApple, 1)
	}
}

// offerStatue 神像供奉：消耗全部风神瞳推进进度，每升一级发放一把长剑。
func (s *Service) offerStatue(ctx context.Context, playerID string, p *poi.Poi, inventory *Inventory) {
	if inventory == nil || s.itemConfig == nil {
		return
	}
	count, err := inventory.ConsumeAll(ctx, item.IDAnemoculus)
	if err != nil || count == 0 {
		return
	}
	if p.Statue == nil {
		p.Statue = &poi.StatueState{Level: 1}
	}
	threshold := float32(s.itemConfig.Statue.LevelThreshold)
	if threshold <= 0 {
		threshold = 10
	}
	p.Statue.Progress += float32(count * s.itemConfig.Statue.ProgressPerOculus)
	for p.Statue.Progress >= threshold {
		p.Statue.Progress -= threshold
		p.Statue.Level++
		_ = inventory.Grant(ctx, item.IDSword, 1)
	}
	_ = s.upsertPlayerPoi(ctx, playerID, p)
}

// playerPoisLocked 加载玩家个人 POI 状态；没有持久化记录的新玩家从策划导出的静态模板初始化。
// 调用方必须已经持有 s.mu 写锁，避免同一玩家被并发创建多个内存快照。
func (s *Service) playerPoisLocked(ctx context.Context, playerID string) (map[string]*poi.Poi, error) {
	if playerID == "" {
		playerID = "default"
	}
	if pois, ok := s.playerPois[playerID]; ok {
		return pois, nil
	}
	pois := make(map[string]*poi.Poi, len(s.byID))
	if scoped, ok := s.store.(PlayerPoiStore); ok {
		stored, err := scoped.LoadAllForPlayer(ctx, playerID)
		if err != nil {
			return nil, err
		}
		for _, target := range stored {
			if target != nil && poi.IsUUID(target.ID) && !target.Retired {
				if template, exists := s.byID[target.ID]; !exists || template.Retired {
					continue
				}
				pois[target.ID] = target
			}
		}
	}
	for id, template := range s.byID {
		if template.Retired {
			continue
		}
		if _, exists := pois[id]; exists {
			continue
		}
		copy := template.Clone()
		resetPoiState(copy)
		pois[id] = copy
	}
	s.playerPois[playerID] = pois
	return pois, nil
}

// upsertPlayerPoi 按玩家作用域持久化 POI；旧存储只支持默认玩家，因此回退到原 Store 接口。
func (s *Service) upsertPlayerPoi(ctx context.Context, playerID string, target *poi.Poi) error {
	if scoped, ok := s.store.(PlayerPoiStore); ok {
		return scoped.UpsertForPlayer(ctx, playerID, target)
	}
	return s.store.Upsert(ctx, target)
}

// resetPoiState 清除模板中可能携带的其它玩家状态，再按 POI 类型建立全新的初始状态。
func resetPoiState(target *poi.Poi) {
	target.Statue = nil
	target.Anchor = nil
	target.Chest = nil
	target.SpiritCore = nil
	target.Gathering = nil
	target.Dungeon = nil
	target.MapBoss = nil
	initState(target)
}

// inventoryForPlayer 在读场景中加载指定玩家背包；新玩家会复用同一个 ItemStore 创建独立内存索引。
func (s *Service) inventoryForPlayer(ctx context.Context, playerID string) (*Inventory, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.inventoryForPlayerLocked(ctx, playerID)
}

// inventoryForPlayerLocked 返回玩家背包；调用方必须已经持有 s.mu 写锁。
func (s *Service) inventoryForPlayerLocked(ctx context.Context, playerID string) (*Inventory, error) {
	if playerID == "" {
		playerID = "default"
	}
	if inventory, ok := s.inventories[playerID]; ok {
		return inventory, nil
	}
	if s.inventory == nil || s.inventory.store == nil || s.itemConfig == nil {
		return nil, fmt.Errorf("inventory service is not configured")
	}
	inventory := NewInventory(s.inventory.store, s.itemConfig, playerID)
	if err := inventory.Load(ctx); err != nil {
		return nil, err
	}
	s.inventories[playerID] = inventory
	return inventory, nil
}

// applyToState 把操作落到记录对应类型的状态子文档；返回是否产生了一次有效变更。
func applyToState(p *poi.Poi, op int32) bool {
	switch op {
	case poi.PoiOpUnlock:
		switch p.PoiType {
		case poi.PoiTypeStatue:
			if p.Statue == nil {
				p.Statue = &poi.StatueState{}
			}
			if p.Statue.Unlocked {
				return false
			}
			p.Statue.Unlocked = true
			return true
		case poi.PoiTypeTeleAnchor:
			if p.Anchor == nil {
				p.Anchor = &poi.AnchorState{}
			}
			if p.Anchor.Unlocked {
				return false
			}
			p.Anchor.Unlocked = true
			return true
		case poi.PoiTypeDungeon:
			if p.Dungeon == nil {
				p.Dungeon = &poi.DungeonState{}
			}
			if p.Dungeon.Unlocked {
				return false
			}
			p.Dungeon.Unlocked = true
			return true
		default:
			return false
		}
	case poi.PoiOpOpenChest:
		if p.Chest == nil {
			p.Chest = &poi.ChestState{}
		}
		if p.Chest.Opened {
			return false
		}
		p.Chest.Opened = true
		return true
	case poi.PoiOpCollectCore:
		if p.SpiritCore == nil {
			p.SpiritCore = &poi.SpiritCoreState{}
		}
		if p.SpiritCore.Collected {
			return false
		}
		p.SpiritCore.Collected = true
		return true
	case poi.PoiOpGather:
		if p.Gathering == nil {
			p.Gathering = &poi.GatheringState{}
		}
		p.Gathering.RespawnAt = time.Now().UnixMilli() + poi.GatheringRespawnSeconds*1000
		return true
	case poi.PoiOpDefeat:
		if p.MapBoss == nil {
			p.MapBoss = &poi.MapBossState{}
		}
		p.MapBoss.RespawnAt = time.Now().UnixMilli() + poi.MapBossRespawnSeconds*1000
		return true
	default:
		return false
	}
}

// initState 按类型初始化新 POI 的状态子文档，使对应字段可见（避免 omitempty 吞掉 false 值）。
func initState(p *poi.Poi) {
	switch p.PoiType {
	case poi.PoiTypeStatue:
		p.Statue = &poi.StatueState{Level: 1}
	case poi.PoiTypeTeleAnchor:
		p.Anchor = &poi.AnchorState{}
	case poi.PoiTypeChest:
		p.Chest = &poi.ChestState{}
	case poi.PoiTypeSpiritCore:
		p.SpiritCore = &poi.SpiritCoreState{}
	case poi.PoiTypeGathering:
		p.Gathering = &poi.GatheringState{}
	case poi.PoiTypeDungeon:
		p.Dungeon = &poi.DungeonState{}
	case poi.PoiTypeMapBoss:
		p.MapBoss = &poi.MapBossState{}
	}
}
