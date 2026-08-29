package room

import "testing"

// TestManagerCreatesOneDefaultRoomAndTracksPositions 验证所有玩家都进入唯一默认房间且坐标可更新。
func TestManagerCreatesOneDefaultRoomAndTracksPositions(t *testing.T) {
	manager := New()
	if roomID := manager.Join("player-a"); roomID != DefaultRoomID {
		t.Fatalf("room id = %s, want %s", roomID, DefaultRoomID)
	}
	manager.Join("player-b")
	position := manager.UpdatePosition("player-a", 1, 2, 3)
	if position.X != 1 || position.Y != 2 || position.Z != 3 {
		t.Fatalf("position = %#v, want 1,2,3", position)
	}
	if got := len(manager.Snapshot()); got != 2 {
		t.Fatalf("snapshot count = %d, want 2", got)
	}
}
