// Package netx 实现 POI 的 TCP 服务：4 字节大端长度前缀 + protobuf Packet 消息体，按类型分发到 service。
package netx

import (
	"bufio"
	"context"
	"encoding/binary"
	"io"
	"net"

	"google.golang.org/protobuf/proto"

	"prometheus/gen/protocol"
	"prometheus/internal/poi"
	"prometheus/internal/service"
)

// maxFrameBytes 单帧 protobuf 字节上限，防止异常长度导致无界分配。
const maxFrameBytes = 16 * 1024 * 1024

// Server 是 POI TCP 服务器。
type Server struct {
	ctx context.Context
	svc *service.Service
}

// New 创建 TCP 服务器。
func New(ctx context.Context, svc *service.Service) *Server {
	return &Server{ctx: ctx, svc: svc}
}

// ListenAndServe 在 addr 上监听并循环接受连接，每个连接由独立 goroutine 处理。
func (s *Server) ListenAndServe(addr string) error {
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		return err
	}
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
	defer conn.Close()
	r := bufio.NewReader(conn)
	for {
		req, err := readPacket(r)
		if err != nil {
			return
		}
		resp := s.dispatch(req)
		if resp == nil {
			continue
		}
		if err := writePacket(conn, resp); err != nil {
			return
		}
	}
}

// dispatch 按请求类型调用 service 并构造响应 Packet；未知请求返回 nil 忽略。
func (s *Server) dispatch(req *protocol.Packet) *protocol.Packet {
	switch body := req.Body.(type) {
	case *protocol.Packet_PullAll:
		resp := &protocol.PullAllResponse{}
		for _, p := range s.svc.PullAll() {
			resp.States = append(resp.States, toProtoState(p))
		}
		return &protocol.Packet{Body: &protocol.Packet_PullAllResp{PullAllResp: resp}}
	case *protocol.Packet_PullChunk:
		chunkID := body.PullChunk.ChunkId
		resp := &protocol.PullChunkResponse{ChunkId: chunkID}
		for _, p := range s.svc.PullChunk(chunkID) {
			resp.States = append(resp.States, toProtoState(p))
		}
		return &protocol.Packet{Body: &protocol.Packet_PullChunkResp{PullChunkResp: resp}}
	case *protocol.Packet_Interact:
		p, ok := s.svc.Interact(s.ctx, body.Interact.Id, int32(body.Interact.Op))
		resp := &protocol.InteractResponse{Success: ok}
		if p != nil {
			resp.State = toProtoState(p)
		}
		return &protocol.Packet{Body: &protocol.Packet_InteractResp{InteractResp: resp}}
	case *protocol.Packet_GetItems:
		resp := &protocol.GetItemsResponse{}
		for _, st := range s.svc.GetItems() {
			resp.Items = append(resp.Items, &protocol.Item{ItemId: st.ItemID, Quality: st.Quality, Quantity: st.Quantity})
		}
		return &protocol.Packet{Body: &protocol.Packet_GetItemsResp{GetItemsResp: resp}}
	default:
		return nil
	}
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

// readPacket 读取一帧：4 字节大端长度 + protobuf 字节。
func readPacket(r *bufio.Reader) (*protocol.Packet, error) {
	var lenBuf [4]byte
	if _, err := io.ReadFull(r, lenBuf[:]); err != nil {
		return nil, err
	}
	n := binary.BigEndian.Uint32(lenBuf[:])
	if n == 0 || n > maxFrameBytes {
		return nil, io.ErrUnexpectedEOF
	}
	body := make([]byte, n)
	if _, err := io.ReadFull(r, body); err != nil {
		return nil, err
	}
	pkt := &protocol.Packet{}
	if err := proto.Unmarshal(body, pkt); err != nil {
		return nil, err
	}
	return pkt, nil
}

// writePacket 写出一帧：4 字节大端长度 + protobuf 字节。
func writePacket(w io.Writer, pkt *protocol.Packet) error {
	body, err := proto.Marshal(pkt)
	if err != nil {
		return err
	}
	var lenBuf [4]byte
	binary.BigEndian.PutUint32(lenBuf[:], uint32(len(body)))
	if _, err := w.Write(lenBuf[:]); err != nil {
		return err
	}
	_, err = w.Write(body)
	return err
}
