// Package service 实现 POI 权威逻辑：启动播种、全量/按区块同步与交互校验。
// 服务器是权威：客户端仅在 Interact 返回成功后（success=true）才做表现。
package service

import (
	"context"
	"fmt"
	"math/rand"
	"sync"
	"time"

	"prometheus/internal/item"
	"prometheus/internal/poi"
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
	randomMu    sync.Mutex
	random      *rand.Rand
}

// New 创建 POI 权威服务。
func New(st store.Store, inv *Inventory, itemConfig *item.Config) *Service {
	inventories := make(map[string]*Inventory)
	if inv != nil {
		inventories[inv.player] = inv
	}
	return &Service{store: st, inventory: inv, inventories: inventories, itemConfig: itemConfig, byID: make(map[string]*poi.Poi), byChunk: make(map[int32][]*poi.Poi), random: rand.New(rand.NewSource(time.Now().UnixNano()))}
}

// Seed 从存储加载已有记录，并按导出配置 upsert：新 POI 插入，已有 POI 更新静态定义（region/类型/位置/旋转/chunkId），状态 bool 保持不变。
func (s *Service) Seed(ctx context.Context, exported []poi.ExportItem) error {
	s.mu.Lock()
	defer s.mu.Unlock()
	pois, err := s.store.LoadAll(ctx)
	if err != nil {
		return err
	}
	for _, p := range pois {
		if p == nil {
			continue
		}
		s.byID[p.ID] = p
		s.byChunk[p.ChunkID] = append(s.byChunk[p.ChunkID], p)
	}
	for _, item := range exported {
		if item.ID == "" {
			continue
		}
		if existing, ok := s.byID[item.ID]; ok {
			// 已存在：更新静态定义字段，保留状态 bool，避免移动/改配置丢失玩家进度。
			oldChunk := existing.ChunkID
			existing.Region = item.Region
			existing.PoiType = item.PoiType
			existing.Position = item.Position
			existing.Rotation = item.Rotation
			existing.ChunkID = item.ChunkID
			if oldChunk != existing.ChunkID { // chunk 变化时同步调整 chunk 索引
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
		}
		initState(p)
		s.byID[p.ID] = p
		s.byChunk[p.ChunkID] = append(s.byChunk[p.ChunkID], p)
		if err := s.store.Upsert(ctx, p); err != nil {
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
	out := make([]*poi.Poi, 0, len(s.byID))
	for _, p := range s.byID {
		out = append(out, p.Clone())
	}
	return out
}

// PullChunk 返回指定 chunk 内的 POI 状态；chunk 无数据时返回空切片。
func (s *Service) PullChunk(chunkID int32) []*poi.Poi {
	s.mu.RLock()
	defer s.mu.RUnlock()
	if list, ok := s.byChunk[chunkID]; ok {
		out := make([]*poi.Poi, 0, len(list))
		for _, p := range list {
			out = append(out, p.Clone())
		}
		return out
	}
	return nil
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

// Interact 校验并应用一次交互：返回变更后的记录与是否成功。
// 一次性操作（解锁/开箱/收集）重复请求返回 false；可刷新操作（采集/击败）总是成功；供奉总是成功。
func (s *Service) Interact(ctx context.Context, id string, op int32) (*poi.Poi, bool) {
	return s.InteractForPlayer(ctx, "default", id, op)
}

// InteractForPlayer 以指定玩家身份执行 POI 交互，使未来多玩家背包和权限校验不会依赖全局默认玩家。
func (s *Service) InteractForPlayer(ctx context.Context, playerID, id string, op int32) (*poi.Poi, bool) {
	s.mu.Lock()
	defer s.mu.Unlock()
	p, ok := s.byID[id]
	if !ok {
		return nil, false
	}
	if op == poi.PoiOpOfferStatue {
		inventory, err := s.inventoryForPlayerLocked(ctx, playerID)
		if err != nil {
			return p.Clone(), false
		}
		s.offerStatue(ctx, p, inventory) // 供奉：消耗风神瞳推进进度，升级发长剑
		return p.Clone(), true
	}
	if !applyToState(p, op) {
		return p.Clone(), false
	}
	_ = s.store.Upsert(ctx, p) // 权威变更后立即入库
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
func (s *Service) offerStatue(ctx context.Context, p *poi.Poi, inventory *Inventory) {
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
	_ = s.store.Upsert(ctx, p)
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
