package service

import (
	"context"
	"fmt"
	"strconv"
	"sync"

	"prometheus/internal/item"
)

// ItemStore 是背包存储抽象（便于单测用内存实现替换 MongoDB）。
type ItemStore interface {
	LoadAll(ctx context.Context, playerID string) ([]*item.Stack, error)
	Upsert(ctx context.Context, st *item.Stack) error
}

// Inventory 维护单玩家的背包（内存索引 + 持久化）。
type Inventory struct {
	mu     sync.RWMutex
	store  ItemStore
	config *item.Config
	player string
	stacks map[string]*item.Stack // key: itemID:quality
}

// NewInventory 创建背包实例。
func NewInventory(st ItemStore, cfg *item.Config, player string) *Inventory {
	return &Inventory{store: st, config: cfg, player: player, stacks: make(map[string]*item.Stack)}
}

// Load 从存储加载已有物品到内存。
func (inv *Inventory) Load(ctx context.Context) error {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	stacks, err := inv.store.LoadAll(ctx, inv.player)
	if err != nil {
		return err
	}
	for _, st := range stacks {
		if st == nil {
			continue
		}
		inv.stacks[stackKey(st.ItemID, st.Quality)] = st
	}
	return nil
}

// Grant 发放指定物品（品质取自配置），并立即入库。
func (inv *Inventory) Grant(ctx context.Context, itemID string, quantity int32) error {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	return inv.grantLocked(ctx, itemID, quantity)
}

// grantLocked 发放指定物品；调用方必须已经持有 inv.mu 写锁。
func (inv *Inventory) grantLocked(ctx context.Context, itemID string, quantity int32) error {
	if quantity <= 0 {
		return fmt.Errorf("quantity must be positive")
	}
	def, ok := inv.config.FindDef(itemID)
	if !ok {
		return fmt.Errorf("unknown item %s", itemID)
	}
	k := stackKey(itemID, def.Quality)
	st := inv.stacks[k]
	if st == nil {
		st = &item.Stack{PlayerID: inv.player, ItemID: itemID, Quality: def.Quality, Quantity: quantity}
		inv.stacks[k] = st
		if err := inv.store.Upsert(ctx, st); err != nil {
			delete(inv.stacks, k)
			return err
		}
		return nil
	}
	previousQuantity := st.Quantity
	st.Quantity += quantity
	if err := inv.store.Upsert(ctx, st); err != nil {
		st.Quantity = previousQuantity
		return err
	}
	return nil
}

// Consume 消耗指定数量的物品，并返回实际是否成功；当前抽卡等写操作使用该原子内存操作。
func (inv *Inventory) Consume(ctx context.Context, itemID string, quantity int32) (bool, error) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	if quantity <= 0 {
		return false, fmt.Errorf("quantity must be positive")
	}
	def, ok := inv.config.FindDef(itemID)
	if !ok {
		return false, fmt.Errorf("unknown item %s", itemID)
	}
	key := stackKey(itemID, def.Quality)
	stack := inv.stacks[key]
	if stack == nil || stack.Quantity < quantity {
		return false, nil
	}
	previousQuantity := stack.Quantity
	stack.Quantity -= quantity
	if err := inv.store.Upsert(ctx, stack); err != nil {
		stack.Quantity = previousQuantity
		return false, err
	}
	if stack.Quantity == 0 {
		delete(inv.stacks, key)
	}
	return true, nil
}

// ConsumeAll 消耗某物品的全部数量，返回实际消耗数量。
func (inv *Inventory) ConsumeAll(ctx context.Context, itemID string) (int32, error) {
	inv.mu.Lock()
	defer inv.mu.Unlock()
	return inv.consumeAllLocked(ctx, itemID)
}

// consumeAllLocked 消耗某物品的全部数量；调用方必须已经持有 inv.mu 写锁。
func (inv *Inventory) consumeAllLocked(ctx context.Context, itemID string) (int32, error) {
	var total int32
	for k, st := range inv.stacks {
		if st.ItemID != itemID {
			continue
		}
		total += st.Quantity
		st.Quantity = 0
		if err := inv.store.Upsert(ctx, st); err != nil {
			return 0, err
		}
		delete(inv.stacks, k)
	}
	return total, nil
}

// GetAll 返回全部物品（数量大于 0）。
func (inv *Inventory) GetAll() []*item.Stack {
	inv.mu.RLock()
	defer inv.mu.RUnlock()
	out := make([]*item.Stack, 0, len(inv.stacks))
	for _, st := range inv.stacks {
		if st.Quantity > 0 {
			copy := *st
			out = append(out, &copy)
		}
	}
	return out
}

// Has 判断当前背包是否至少拥有指定数量的物品。
func (inv *Inventory) Has(itemID string, quantity int32) bool {
	inv.mu.RLock()
	defer inv.mu.RUnlock()
	def, ok := inv.config.FindDef(itemID)
	if !ok {
		return false
	}
	stack := inv.stacks[stackKey(itemID, def.Quality)]
	return stack != nil && stack.Quantity >= quantity
}

// stackKey 组合 itemID 与 quality 作为内存索引键。
func stackKey(itemID string, quality int32) string {
	return itemID + ":" + strconv.Itoa(int(quality))
}
