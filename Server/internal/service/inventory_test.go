package service

import (
	"context"
	"testing"

	"prometheus/internal/item"
	"prometheus/internal/poi"
)

// mockItemStore 内存背包存储，用于单测替换 MongoDB。
type mockItemStore struct {
	stacks map[string]*item.Stack
}

func newMockItemStore() *mockItemStore {
	return &mockItemStore{stacks: make(map[string]*item.Stack)}
}

func (m *mockItemStore) LoadAll(ctx context.Context, playerID string) ([]*item.Stack, error) {
	out := make([]*item.Stack, 0, len(m.stacks))
	for _, st := range m.stacks {
		out = append(out, st)
	}
	return out, nil
}

func (m *mockItemStore) Upsert(ctx context.Context, st *item.Stack) error {
	m.stacks[stackKey(st.ItemID, st.Quality)] = st
	return nil
}

// mockPoiStore 内存 POI 存储，用于单测替换 MongoDB。
type mockPoiStore struct {
	pois map[string]*poi.Poi
}

func newMockPoiStore() *mockPoiStore {
	return &mockPoiStore{pois: make(map[string]*poi.Poi)}
}

func (m *mockPoiStore) LoadAll(ctx context.Context) ([]*poi.Poi, error) {
	out := make([]*poi.Poi, 0, len(m.pois))
	for _, p := range m.pois {
		out = append(out, p)
	}
	return out, nil
}

func (m *mockPoiStore) Upsert(ctx context.Context, p *poi.Poi) error {
	m.pois[p.ID] = p
	return nil
}

// testConfig 构造测试物品配置（含神像供奉数值）。
func testConfig() *item.Config {
	return &item.Config{
		Items: []item.Def{
			{ID: item.IDSword, Name: "长剑", Category: "weapon", Quality: 1},
			{ID: item.IDAnemoculus, Name: "风神瞳", Category: "special", Quality: 1},
		},
		Statue: item.StatueConfig{ProgressPerOculus: 1, LevelThreshold: 10},
	}
}

// TestInventoryGrantAndConsume 验证物品发放、聚叠与全部消耗。
func TestInventoryGrantAndConsume(t *testing.T) {
	inv := NewInventory(newMockItemStore(), testConfig(), "default")
	ctx := context.Background()

	if err := inv.Grant(ctx, item.IDAnemoculus, 3); err != nil {
		t.Fatalf("grant: %v", err)
	}
	if err := inv.Grant(ctx, item.IDAnemoculus, 2); err != nil {
		t.Fatalf("grant: %v", err)
	}
	if got := len(inv.GetAll()); got != 1 {
		t.Fatalf("GetAll count = %d, want 1", got)
	}
	if qty := inv.GetAll()[0].Quantity; qty != 5 {
		t.Fatalf("quantity = %d, want 5", qty)
	}

	n, err := inv.ConsumeAll(ctx, item.IDAnemoculus)
	if err != nil || n != 5 {
		t.Fatalf("consume = %d, %v; want 5", n, err)
	}
	if got := len(inv.GetAll()); got != 0 {
		t.Fatalf("GetAll after consume = %d, want 0", got)
	}
}

// TestOfferStatueGrantsSwordOnLevelUp 验证供奉消耗风神瞳、升级发长剑（25 瞳升 2 级）。
func TestOfferStatueGrantsSwordOnLevelUp(t *testing.T) {
	cfg := testConfig()
	inv := NewInventory(newMockItemStore(), cfg, "default")
	svc := New(newMockPoiStore(), inv, cfg)
	ctx := context.Background()

	statue := &poi.Poi{ID: "Mond_Statue_1", PoiType: poi.PoiTypeStatue, Statue: &poi.StatueState{Level: 1}}
	svc.byID[statue.ID] = statue
	if err := inv.Grant(ctx, item.IDAnemoculus, 25); err != nil {
		t.Fatalf("grant: %v", err)
	}

	p, ok := svc.Interact(ctx, statue.ID, poi.PoiOpOfferStatue)
	if !ok || p == nil {
		t.Fatalf("interact ok=%v", ok)
	}
	if p.Statue.Level != 3 { // 25 瞳 = +25 进度，10/级 → 升 2 级 → level 1+2=3
		t.Fatalf("statue level = %d, want 3", p.Statue.Level)
	}
	if p.Statue.Progress != 5 {
		t.Fatalf("statue progress = %v, want 5", p.Statue.Progress)
	}

	// 验证背包：风神瞳清空，长剑 2 把
	for _, st := range inv.GetAll() {
		if st.ItemID == item.IDSword {
			if st.Quantity != 2 {
				t.Fatalf("sword quantity = %d, want 2", st.Quantity)
			}
			return
		}
	}
	t.Fatalf("sword not found in inventory")
}
