package service

import (
	"context"
	"sync"
	"testing"

	"prometheus/internal/item"
)

// TestGachaConsumesOneTokenAndRewardsConfiguredNonTokenItem 验证抽卡扣除一个神瞳并发放非神瞳道具。
func TestGachaConsumesOneTokenAndRewardsConfiguredNonTokenItem(t *testing.T) {
	cfg := &item.Config{Items: []item.Def{{ID: item.IDAnemoculus, Quality: 1}, {ID: item.IDSword, Quality: 2}, {ID: item.IDApple, Quality: 1}}}
	store := newMockItemStore()
	inventory := NewInventory(store, cfg, "player-a")
	ctx := context.Background()
	if err := inventory.Grant(ctx, item.IDAnemoculus, 2); err != nil {
		t.Fatalf("grant token: %v", err)
	}
	svc := New(newMockPoiStore(), inventory, cfg)
	reward, stacks, err := svc.Gacha(ctx, "player-a")
	if err != nil {
		t.Fatalf("gacha: %v", err)
	}
	if reward == nil || reward.ItemID == item.IDAnemoculus || reward.Quantity != 1 {
		t.Fatalf("reward = %#v, want one configured non-token item", reward)
	}
	if inventory.Has(item.IDAnemoculus, 2) || !inventory.Has(item.IDAnemoculus, 1) {
		t.Fatalf("token quantity was not reduced by exactly one")
	}
	foundReward := false
	for _, stack := range stacks {
		if stack.ItemID == reward.ItemID {
			foundReward = true
		}
	}
	if !foundReward {
		t.Fatalf("reward %s missing from returned inventory", reward.ItemID)
	}
}

// TestGachaRejectsWithoutToken 验证没有神瞳时抽卡不会产生奖励。
func TestGachaRejectsWithoutToken(t *testing.T) {
	cfg := &item.Config{Items: []item.Def{{ID: item.IDAnemoculus, Quality: 1}, {ID: item.IDSword, Quality: 1}}}
	inventory := NewInventory(newMockItemStore(), cfg, "player-a")
	svc := New(newMockPoiStore(), inventory, cfg)
	if reward, _, err := svc.Gacha(context.Background(), "player-a"); err == nil || reward != nil {
		t.Fatalf("gacha without token = reward %#v, err %v; want failure", reward, err)
	}
}

// TestConcurrentGachaConsumesOnlyAvailableTokens 验证并发抽卡不会重复消费同一个神瞳，成功次数严格受库存限制。
func TestConcurrentGachaConsumesOnlyAvailableTokens(t *testing.T) {
	cfg := &item.Config{Items: []item.Def{{ID: item.IDAnemoculus, Quality: 1}, {ID: item.IDSword, Quality: 1}}}
	inventory := NewInventory(newMockItemStore(), cfg, "player-a")
	if err := inventory.Grant(context.Background(), item.IDAnemoculus, 8); err != nil {
		t.Fatalf("grant token: %v", err)
	}
	svc := New(newMockPoiStore(), inventory, cfg)
	var waitGroup sync.WaitGroup
	var mu sync.Mutex
	successCount := 0
	for i := 0; i < 16; i++ {
		waitGroup.Add(1)
		go func() {
			defer waitGroup.Done()
			if _, _, err := svc.Gacha(context.Background(), "player-a"); err == nil {
				mu.Lock()
				successCount++
				mu.Unlock()
			}
		}()
	}
	waitGroup.Wait()
	if successCount != 8 {
		t.Fatalf("successful gacha count = %d, want 8", successCount)
	}
	if inventory.Has(item.IDAnemoculus, 1) {
		t.Fatalf("token remains after consuming all available tokens")
	}
	if stacks := inventory.GetAll(); len(stacks) != 1 || stacks[0].ItemID != item.IDSword || stacks[0].Quantity != 8 {
		t.Fatalf("inventory after concurrent gacha = %#v, want 8 swords", stacks)
	}
}
