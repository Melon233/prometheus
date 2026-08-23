package service

import (
	"context"
	"fmt"
	"strconv"

	"prometheus/internal/item"
)

// ItemStore 是背包存储抽象（便于单测用内存实现替换 MongoDB）。
type ItemStore interface {
	LoadAll(ctx context.Context, playerID string) ([]*item.Stack, error)
	Upsert(ctx context.Context, st *item.Stack) error
}

// Inventory 维护单玩家的背包（内存索引 + 持久化）。
type Inventory struct {
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
	def, ok := inv.config.FindDef(itemID)
	if !ok {
		return fmt.Errorf("unknown item %s", itemID)
	}
	k := stackKey(itemID, def.Quality)
	st := inv.stacks[k]
	if st == nil {
		st = &item.Stack{PlayerID: inv.player, ItemID: itemID, Quality: def.Quality, Quantity: quantity}
		inv.stacks[k] = st
	} else {
		st.Quantity += quantity
	}
	return inv.store.Upsert(ctx, st)
}

// ConsumeAll 消耗某物品的全部数量，返回实际消耗数量。
func (inv *Inventory) ConsumeAll(ctx context.Context, itemID string) (int32, error) {
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
	out := make([]*item.Stack, 0, len(inv.stacks))
	for _, st := range inv.stacks {
		if st.Quantity > 0 {
			out = append(out, st)
		}
	}
	return out
}

// stackKey 组合 itemID 与 quality 作为内存索引键。
func stackKey(itemID string, quality int32) string {
	return itemID + ":" + strconv.Itoa(int(quality))
}
