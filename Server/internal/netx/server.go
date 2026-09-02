// Package netx 实现 POI 的 TCP 服务：传输 Packet 由定长 Head 和变长 Protobuf Body 组成，并按类型分发到 service。
package netx

import (
	"bufio"
	"context"
	"encoding/binary"
	"fmt"
	"io"
	"log"
	"net"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"google.golang.org/protobuf/proto"

	"prometheus/gen/protocol"
	"prometheus/internal/poi"
	"prometheus/internal/room"
	"prometheus/internal/service"
)

const (
	// packetHeadBytes 定义传输 Packet 的固定 Head 长度；Head 首字段固定为 4 字节大端 BodyLength。
	packetHeadBytes = 4
	// maxPacketBodyBytes 定义单个变长 Body 的字节上限，防止异常 BodyLength 导致无界分配。
	maxPacketBodyBytes = 16 * 1024 * 1024
)

// packetHead 描述传输 Packet 的定长 Head；BodyLength 必须是 Head 的第一字段。
type packetHead struct {
	bodyLength uint32
}

// transportPacket 描述网络传输边界，由定长 Head 和 Head.BodyLength 指定的变长 Body 组成。
type transportPacket struct {
	head packetHead
	body []byte
}

// Server 是 POI TCP 服务器。
type Server struct {
	ctx          context.Context
	svc          *service.Service
	rooms        *room.Manager
	mu           sync.RWMutex
	sessions     map[*clientSession]struct{}
	nextPlayerID uint64
}

// clientSession 保存一次 TCP 连接的会话身份和串行写锁，避免响应与推送交错写入同一帧。
type clientSession struct {
	conn     net.Conn
	writeMu  sync.Mutex
	playerID string
	roomID   string
	joined   bool
}

// New 创建 TCP 服务器。
func New(ctx context.Context, svc *service.Service) *Server {
	return &Server{ctx: ctx, svc: svc, rooms: room.New(), sessions: make(map[*clientSession]struct{})}
}

// ListenAndServe 在 addr 上监听并循环接受连接，每个连接由独立 goroutine 处理。
func (s *Server) ListenAndServe(addr string) error {
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		return err
	}
	go s.persistPositionsLoop()
	for {
		conn, err := ln.Accept()
		if err != nil {
			return err
		}
		go s.handleConn(conn)
	}
}

// handleConn 处理单连接：循环读取帧、分发、写回响应；EOF 或错误则关闭连接。
func (s *Server) handleConn(conn net.Conn) {
	session := &clientSession{conn: conn}
	s.addSession(session)
	defer func() { s.removeSession(session); conn.Close() }()
	r := bufio.NewReader(conn)
	for {
		req, err := readPacket(r)
		if err != nil {
			return
		}
		responses := s.dispatch(session, req)
		if len(responses) == 0 {
			continue
		}
		for _, resp := range responses {
			if err := session.writePacket(resp); err != nil {
				return
			}
		}
	}
}

// dispatch 按请求类型调用业务服务；响应携带原请求 request_id，服务器主动推送使用 request_id=0。
func (s *Server) dispatch(session *clientSession, req *protocol.Packet) []*protocol.Packet {
	switch body := req.Body.(type) {
	case *protocol.Packet_PullAll:
		resp := &protocol.PullAllResponse{}
		playerID, _, _ := s.sessionState(session)
		states, err := s.svc.PullAllForPlayer(s.ctx, playerID)
		if err != nil {
			return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_PullAllResp{PullAllResp: resp}}}
		}
		for _, p := range states {
			resp.States = append(resp.States, toProtoState(p))
		}
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_PullAllResp{PullAllResp: resp}}}
	case *protocol.Packet_PullChunk:
		chunkID := body.PullChunk.ChunkId
		resp := &protocol.PullChunkResponse{ChunkId: chunkID}
		playerID, _, _ := s.sessionState(session)
		states, err := s.svc.PullChunkForPlayer(s.ctx, playerID, chunkID)
		if err != nil {
			return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_PullChunkResp{PullChunkResp: resp}}}
		}
		for _, p := range states {
			resp.States = append(resp.States, toProtoState(p))
		}
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_PullChunkResp{PullChunkResp: resp}}}
	case *protocol.Packet_Interact:
		playerID, _, _ := s.sessionState(session)
		p, ok := s.svc.InteractForPlayer(s.ctx, playerID, body.Interact.Id, int32(body.Interact.Op))
		resp := &protocol.InteractResponse{Success: ok}
		if p != nil {
			resp.State = toProtoState(p)
		}
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_InteractResp{InteractResp: resp}}}
	case *protocol.Packet_GetItems:
		playerID, _, _ := s.sessionState(session)
		resp := &protocol.GetItemsResponse{}
		items, err := s.svc.GetItemsForPlayer(s.ctx, playerID)
		if err != nil {
			return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_GetItemsResp{GetItemsResp: resp}}}
		}
		for _, st := range items {
			resp.Items = append(resp.Items, &protocol.Item{ItemId: st.ItemID, Quality: st.Quality, Quantity: st.Quantity})
		}
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_GetItemsResp{GetItemsResp: resp}}}
	case *protocol.Packet_JoinRoom:
		playerID := strings.TrimSpace(body.JoinRoom.PlayerId)
		if playerID == "" {
			currentPlayerID, _, joined := s.sessionState(session)
			if joined {
				playerID = currentPlayerID
			} else {
				playerID = s.newPlayerID()
			}
		}
		position, hasPosition, err := s.svc.LoadPlayerPosition(s.ctx, playerID)
		if err != nil {
			return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_JoinRoomResp{JoinRoomResp: &protocol.JoinRoomResponse{Success: false, PlayerId: playerID, Error: err.Error()}}}}
		}
		s.mu.Lock()
		if session.joined {
			if session.playerID == playerID {
				if current, ok := s.rooms.CurrentPosition(playerID); ok {
					position = current
					hasPosition = true
				}
			}
			if session.playerID != playerID {
				s.rooms.Leave(session.playerID)
				session.playerID = playerID
				if hasPosition {
					session.roomID = s.rooms.JoinWithPosition(playerID, &position)
				} else {
					session.roomID = s.rooms.Join(playerID)
				}
			}
			roomID := session.roomID
			currentPlayerID := session.playerID
			s.mu.Unlock()
			return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_JoinRoomResp{JoinRoomResp: &protocol.JoinRoomResponse{Success: true, RoomId: roomID, PlayerId: currentPlayerID, Position: positionPush(roomID, position, hasPosition)}}}}
		}
		session.playerID = playerID
		if hasPosition {
			session.roomID = s.rooms.JoinWithPosition(playerID, &position)
		} else {
			session.roomID = s.rooms.Join(playerID)
		}
		session.joined = true
		roomID := session.roomID
		s.mu.Unlock()
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_JoinRoomResp{JoinRoomResp: &protocol.JoinRoomResponse{Success: true, RoomId: roomID, PlayerId: playerID, Position: positionPush(roomID, position, hasPosition)}}}}
	case *protocol.Packet_UpdatePosition:
		playerID, roomID, joined := s.sessionState(session)
		if !joined {
			return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_UpdatePositionResp{UpdatePositionResp: &protocol.UpdatePositionResponse{Success: false, Error: "join room first"}}}}
		}
		position := s.rooms.UpdatePosition(playerID, body.UpdatePosition.X, body.UpdatePosition.Y, body.UpdatePosition.Z)
		push := &protocol.Packet{Body: &protocol.Packet_PlayerPosition{PlayerPosition: &protocol.PlayerPositionPush{RoomId: roomID, PlayerId: position.PlayerID, X: position.X, Y: position.Y, Z: position.Z, ServerTimeMs: position.ServerTimeMs}}}
		s.broadcast(roomID, push)
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_UpdatePositionResp{UpdatePositionResp: &protocol.UpdatePositionResponse{Success: true}}}}
	case *protocol.Packet_Gacha:
		playerID, _, _ := s.sessionState(session)
		reward, items, err := s.svc.Gacha(s.ctx, playerID)
		resp := &protocol.GachaResponse{Success: err == nil, Error: errorText(err)}
		if reward != nil {
			resp.Reward = &protocol.Item{ItemId: reward.ItemID, Quality: reward.Quality, Quantity: reward.Quantity}
		}
		for _, st := range items {
			resp.Items = append(resp.Items, &protocol.Item{ItemId: st.ItemID, Quality: st.Quality, Quantity: st.Quantity})
		}
		return []*protocol.Packet{{RequestId: req.RequestId, Body: &protocol.Packet_GachaResp{GachaResp: resp}}}
	default:
		return nil
	}
}

// addSession 注册在线连接；房间加入完成后才会参与坐标广播。
func (s *Server) addSession(session *clientSession) {
	s.mu.Lock()
	s.sessions[session] = struct{}{}
	s.mu.Unlock()
}

// removeSession 清理连接，并移除默认房间中的在线坐标。
func (s *Server) removeSession(session *clientSession) {
	s.mu.Lock()
	delete(s.sessions, session)
	joined := session.joined
	playerID := session.playerID
	session.joined = false
	s.mu.Unlock()
	if joined {
		if position, ok := s.rooms.CurrentPosition(playerID); ok {
			_ = s.svc.SavePlayerPosition(s.ctx, playerID, position)
		}
		s.rooms.Leave(playerID)
	}
}

// persistPositionsLoop 每 3 秒把在线玩家的最新坐标写入其 players 聚合文档。
func (s *Server) persistPositionsLoop() {
	ticker := time.NewTicker(3 * time.Second)
	defer ticker.Stop()
	for {
		select {
		case <-ticker.C:
			for _, position := range s.rooms.Snapshot() {
				if err := s.svc.SavePlayerPosition(s.ctx, position.PlayerID, position); err != nil {
					log.Printf("persist player position %s: %v", position.PlayerID, err)
				}
			}
		case <-s.ctx.Done():
			return
		}
	}
}

// sessionState 在服务器会话锁下读取玩家身份，避免广播协程与加入请求并发读写会话字段。
func (s *Server) sessionState(session *clientSession) (string, string, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return session.playerID, session.roomID, session.joined
}

// newPlayerID 生成服务器侧唯一玩家 ID，客户端提供的稳定 ID 仍优先使用。
func (s *Server) newPlayerID() string {
	return fmt.Sprintf("player-%d", atomic.AddUint64(&s.nextPlayerID, 1))
}

// broadcast 将服务器主动推送写给同房间全部在线会话，包括发送坐标的客户端。
func (s *Server) broadcast(roomID string, packet *protocol.Packet) {
	s.mu.RLock()
	sessions := make([]*clientSession, 0, len(s.sessions))
	for session := range s.sessions {
		if session.joined && session.roomID == roomID {
			sessions = append(sessions, session)
		}
	}
	s.mu.RUnlock()
	for _, session := range sessions {
		_ = session.writePacket(packet)
	}
}

// writePacket 以连接级互斥锁串行写出一帧，保证响应和推送不会互相穿插。
func (session *clientSession) writePacket(packet *protocol.Packet) error {
	session.writeMu.Lock()
	defer session.writeMu.Unlock()
	return writePacket(session.conn, packet)
}

// errorText 将可选错误转换为协议中的稳定文本。
func errorText(err error) string {
	if err == nil {
		return ""
	}
	return err.Error()
}

// toProtoState 把领域记录转换为 proto 状态（仅同步可变状态 + id，不含静态定义）；各类型状态子文档为 nil 时对应字段取零值。
func toProtoState(p *poi.Poi) *protocol.PoiState {
	s := &protocol.PoiState{
		Id:      p.ID,
		PoiType: protocol.PoiType(p.PoiType),
	}
	if p.Statue != nil {
		s.StatueUnlocked = p.Statue.Unlocked
		s.StatueLevel = p.Statue.Level
		s.StatueProgress = p.Statue.Progress
	}
	if p.Anchor != nil {
		s.AnchorUnlocked = p.Anchor.Unlocked
	}
	if p.Chest != nil {
		s.ChestOpened = p.Chest.Opened
	}
	if p.SpiritCore != nil {
		s.SpiritCoreCollected = p.SpiritCore.Collected
	}
	if p.Gathering != nil {
		s.GatheringRespawnAt = p.Gathering.RespawnAt
	}
	if p.Dungeon != nil {
		s.DungeonUnlocked = p.Dungeon.Unlocked
	}
	if p.MapBoss != nil {
		s.MapBossRespawnAt = p.MapBoss.RespawnAt
	}
	return s
}

// positionPush 将领域坐标转换为加入响应中的恢复坐标；没有历史坐标时返回 nil。
func positionPush(roomID string, position room.Position, ok bool) *protocol.PlayerPositionPush {
	if !ok {
		return nil
	}
	return &protocol.PlayerPositionPush{RoomId: roomID, PlayerId: position.PlayerID, X: position.X, Y: position.Y, Z: position.Z, ServerTimeMs: position.ServerTimeMs}
}

// readPacket 读取一个 Head + Body 传输 Packet，并把变长 Body 反序列化为业务 Packet。
func readPacket(r *bufio.Reader) (*protocol.Packet, error) {
	packet, err := readTransportPacket(r)
	if err != nil {
		return nil, err
	}
	pkt := &protocol.Packet{}
	if err := proto.Unmarshal(packet.body, pkt); err != nil {
		return nil, err
	}
	return pkt, nil
}

// writePacket 把业务 Packet 序列化为变长 Body，并写出包含 BodyLength 首字段的完整传输 Packet。
func writePacket(w io.Writer, pkt *protocol.Packet) error {
	body, err := proto.Marshal(pkt)
	if err != nil {
		return err
	}
	return writeTransportPacket(w, transportPacket{head: packetHead{bodyLength: uint32(len(body))}, body: body})
}

// readTransportPacket 先精确读取定长 Head，再根据首字段 BodyLength 精确读取变长 Body。
func readTransportPacket(r io.Reader) (transportPacket, error) {
	var headBytes [packetHeadBytes]byte
	if _, err := io.ReadFull(r, headBytes[:]); err != nil {
		return transportPacket{}, err
	}
	head := packetHead{bodyLength: binary.BigEndian.Uint32(headBytes[:])}
	if head.bodyLength == 0 || head.bodyLength > maxPacketBodyBytes {
		return transportPacket{}, fmt.Errorf("invalid packet body length: %d", head.bodyLength)
	}
	body := make([]byte, head.bodyLength)
	if _, err := io.ReadFull(r, body); err != nil {
		return transportPacket{}, err
	}
	return transportPacket{head: head, body: body}, nil
}

// writeTransportPacket 按定长 Head 在前、变长 Body 在后的布局写出完整 Packet。
func writeTransportPacket(w io.Writer, packet transportPacket) error {
	if packet.head.bodyLength == 0 || packet.head.bodyLength > maxPacketBodyBytes {
		return fmt.Errorf("invalid packet body length: %d", packet.head.bodyLength)
	}
	if uint32(len(packet.body)) != packet.head.bodyLength {
		return fmt.Errorf("packet body length mismatch: head=%d body=%d", packet.head.bodyLength, len(packet.body))
	}
	var headBytes [packetHeadBytes]byte
	binary.BigEndian.PutUint32(headBytes[:], packet.head.bodyLength)
	if err := writeFull(w, headBytes[:]); err != nil {
		return err
	}
	return writeFull(w, packet.body)
}

// writeFull 循环写出全部字节，避免合法 Writer 返回短写导致客户端收到截断帧。
func writeFull(w io.Writer, data []byte) error {
	for len(data) > 0 {
		count, err := w.Write(data)
		if err != nil {
			return err
		}
		if count <= 0 {
			return io.ErrShortWrite
		}
		data = data[count:]
	}
	return nil
}
