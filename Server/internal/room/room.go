// Package room 管理服务器中的房间与玩家空间状态。
package room

import (
	"sync"
	"time"
)

// DefaultRoomID 是服务器唯一默认房间的稳定标识。
const DefaultRoomID = "default"

// Position 表示玩家在房间中的服务器权威坐标。
type Position struct {
	PlayerID     string  `bson:"player_id"`
	X            float32 `bson:"x"`
	Y            float32 `bson:"y"`
	Z            float32 `bson:"z"`
	ServerTimeMs int64   `bson:"server_time_ms"`
}

// Manager 管理唯一默认房间中的在线玩家位置。
type Manager struct {
	mu        sync.RWMutex
	positions map[string]Position
	members   map[string]int // 同一玩家多条连接的引用计数，避免旧连接断开误删新连接状态。
}

// New 创建已经存在唯一默认房间的房间管理器。
func New() *Manager {
	return &Manager{positions: make(map[string]Position), members: make(map[string]int)}
}

// Join 将玩家加入默认房间，并返回稳定房间 ID。
func (m *Manager) Join(playerID string) string {
	return m.JoinWithPosition(playerID, nil)
}

// JoinWithPosition 将玩家加入默认房间，并在已有持久化坐标时恢复房间内位置。
func (m *Manager) JoinWithPosition(playerID string, initial *Position) string {
	m.mu.Lock()
	defer m.mu.Unlock()
	if _, ok := m.positions[playerID]; !ok {
		if initial != nil {
			position := *initial
			position.PlayerID = playerID
			m.positions[playerID] = position
		} else {
			m.positions[playerID] = Position{PlayerID: playerID, ServerTimeMs: time.Now().UnixMilli()}
		}
	}
	m.members[playerID]++
	return DefaultRoomID
}

// CurrentPosition 返回玩家当前在线坐标；玩家不在线时返回 false。
func (m *Manager) CurrentPosition(playerID string) (Position, bool) {
	m.mu.RLock()
	defer m.mu.RUnlock()
	position, ok := m.positions[playerID]
	return position, ok
}

// Leave 移除断开连接的在线玩家。
func (m *Manager) Leave(playerID string) {
	m.mu.Lock()
	m.members[playerID]--
	if m.members[playerID] <= 0 {
		delete(m.members, playerID)
		delete(m.positions, playerID)
	}
	m.mu.Unlock()
}

// UpdatePosition 写入玩家坐标并返回带服务器时间的广播数据。
func (m *Manager) UpdatePosition(playerID string, x, y, z float32) Position {
	m.mu.Lock()
	defer m.mu.Unlock()
	position := Position{PlayerID: playerID, X: x, Y: y, Z: z, ServerTimeMs: time.Now().UnixMilli()}
	m.positions[playerID] = position
	return position
}

// Snapshot 返回默认房间当前在线玩家位置快照。
func (m *Manager) Snapshot() []Position {
	m.mu.RLock()
	defer m.mu.RUnlock()
	positions := make([]Position, 0, len(m.positions))
	for _, position := range m.positions {
		positions = append(positions, position)
	}
	return positions
}
