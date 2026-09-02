package netx

import (
	"bytes"
	"context"
	"encoding/binary"
	"net"
	"testing"

	"google.golang.org/protobuf/proto"
	"prometheus/gen/protocol"
	"prometheus/internal/item"
	"prometheus/internal/poi"
	"prometheus/internal/service"
)

// TestTransportPacketContainsFixedHeadAndVariableBody 验证传输 Packet 以定长 Head 开始、首字段声明 BodyLength，并能连续解析不同长度的 Body。
func TestTransportPacketContainsFixedHeadAndVariableBody(t *testing.T) {
	firstBody := []byte{1, 2, 3}
	secondBody := []byte{4, 5, 6, 7, 8}
	var stream bytes.Buffer
	if err := writeTransportPacket(&stream, transportPacket{head: packetHead{bodyLength: uint32(len(firstBody))}, body: firstBody}); err != nil {
		t.Fatalf("write first transport packet: %v", err)
	}
	if err := writeTransportPacket(&stream, transportPacket{head: packetHead{bodyLength: uint32(len(secondBody))}, body: secondBody}); err != nil {
		t.Fatalf("write second transport packet: %v", err)
	}
	encoded := stream.Bytes()
	if got := binary.BigEndian.Uint32(encoded[:packetHeadBytes]); got != uint32(len(firstBody)) {
		t.Fatalf("first head body length = %d, want %d", got, len(firstBody))
	}
	first, err := readTransportPacket(&stream)
	if err != nil {
		t.Fatalf("read first transport packet: %v", err)
	}
	second, err := readTransportPacket(&stream)
	if err != nil {
		t.Fatalf("read second transport packet: %v", err)
	}
	if first.head.bodyLength != uint32(len(firstBody)) || !bytes.Equal(first.body, firstBody) {
		t.Fatalf("first transport packet = head:%d body:%v", first.head.bodyLength, first.body)
	}
	if second.head.bodyLength != uint32(len(secondBody)) || !bytes.Equal(second.body, secondBody) {
		t.Fatalf("second transport packet = head:%d body:%v", second.head.bodyLength, second.body)
	}
}

// TestSingleClientPositionPushIncludesSender 验证单客户端上传坐标后能收到服务器回推的自身坐标。
func TestSingleClientPositionPushIncludesSender(t *testing.T) {
	cfg := &item.Config{Items: []item.Def{{ID: item.IDAnemoculus, Quality: 1}, {ID: item.IDSword, Quality: 1}}}
	inventory := service.NewInventory(newNetxItemStore(), cfg, "default")
	svc := service.New(newNetxPoiStore(), inventory, cfg)
	serverConn, clientConn := net.Pipe()
	server := New(context.Background(), svc)
	go server.handleConn(serverConn)
	defer clientConn.Close()

	writeTestPacket(t, clientConn, &protocol.Packet{RequestId: 1, Body: &protocol.Packet_JoinRoom{JoinRoom: &protocol.JoinRoomRequest{PlayerId: "player-a"}}})
	joinResponse := readTestPacket(t, clientConn)
	if !joinResponse.GetJoinRoomResp().GetSuccess() || joinResponse.GetJoinRoomResp().GetRoomId() != "default" {
		t.Fatalf("join response = %s", joinResponse.String())
	}
	writeTestPacket(t, clientConn, &protocol.Packet{RequestId: 2, Body: &protocol.Packet_UpdatePosition{UpdatePosition: &protocol.UpdatePositionRequest{X: 4, Y: 5, Z: 6}}})
	push := readTestPacket(t, clientConn)
	if push.RequestId != 0 || push.GetPlayerPosition() == nil || push.GetPlayerPosition().GetPlayerId() != "player-a" {
		t.Fatalf("position push = %s", push.String())
	}
	if push.GetPlayerPosition().GetX() != 4 || push.GetPlayerPosition().GetY() != 5 || push.GetPlayerPosition().GetZ() != 6 {
		t.Fatalf("position push coordinates = %v,%v,%v", push.GetPlayerPosition().GetX(), push.GetPlayerPosition().GetY(), push.GetPlayerPosition().GetZ())
	}
	ack := readTestPacket(t, clientConn)
	if !ack.GetUpdatePositionResp().GetSuccess() || ack.RequestId != 2 {
		t.Fatalf("position ack = %s", ack.String())
	}
}

// TestGachaRequestConsumesTokenAndReturnsReward 验证 TCP 抽卡请求经过会话、协议和业务层后返回非神瞳奖励。
func TestGachaRequestConsumesTokenAndReturnsReward(t *testing.T) {
	cfg := &item.Config{Items: []item.Def{{ID: item.IDAnemoculus, Quality: 1}, {ID: item.IDSword, Quality: 1}}}
	inventory := service.NewInventory(newNetxItemStore(), cfg, "default")
	if err := inventory.Grant(context.Background(), item.IDAnemoculus, 1); err != nil {
		t.Fatalf("grant token: %v", err)
	}
	svc := service.New(newNetxPoiStore(), inventory, cfg)
	serverConn, clientConn := net.Pipe()
	server := New(context.Background(), svc)
	go server.handleConn(serverConn)
	defer clientConn.Close()

	writeTestPacket(t, clientConn, &protocol.Packet{RequestId: 1, Body: &protocol.Packet_JoinRoom{JoinRoom: &protocol.JoinRoomRequest{PlayerId: "default"}}})
	readTestPacket(t, clientConn)
	writeTestPacket(t, clientConn, &protocol.Packet{RequestId: 2, Body: &protocol.Packet_Gacha{Gacha: &protocol.GachaRequest{}}})
	response := readTestPacket(t, clientConn)
	gacha := response.GetGachaResp()
	if response.RequestId != 2 || !gacha.GetSuccess() || gacha.GetReward() == nil || gacha.GetReward().GetItemId() == item.IDAnemoculus {
		t.Fatalf("gacha response = %s", response.String())
	}
	if len(gacha.GetItems()) != 1 || gacha.GetItems()[0].GetItemId() != item.IDSword || gacha.GetItems()[0].GetQuantity() != 1 {
		t.Fatalf("gacha inventory = %s", gacha.String())
	}
}

// TestJoinRoomIsIdempotentPerConnection 验证同一连接重复加入不会增加房间引用计数，断开一次即可清理在线状态。
func TestJoinRoomIsIdempotentPerConnection(t *testing.T) {
	cfg := &item.Config{Items: []item.Def{{ID: item.IDAnemoculus, Quality: 1}, {ID: item.IDSword, Quality: 1}}}
	inventory := service.NewInventory(newNetxItemStore(), cfg, "default")
	server := New(context.Background(), service.New(newNetxPoiStore(), inventory, cfg))
	session := &clientSession{}
	join := func(requestID uint64) *protocol.JoinRoomResponse {
		responses := server.dispatch(session, &protocol.Packet{RequestId: requestID, Body: &protocol.Packet_JoinRoom{JoinRoom: &protocol.JoinRoomRequest{PlayerId: "player-a"}}})
		if len(responses) != 1 || responses[0].GetJoinRoomResp() == nil {
			t.Fatalf("join responses = %#v", responses)
		}
		return responses[0].GetJoinRoomResp()
	}
	if response := join(1); !response.GetSuccess() || response.GetRoomId() != "default" {
		t.Fatalf("first join response = %s", response.String())
	}
	if response := join(2); !response.GetSuccess() || response.GetPlayerId() != "player-a" {
		t.Fatalf("repeat join response = %s", response.String())
	}
	if len(server.rooms.Snapshot()) != 1 {
		t.Fatalf("room snapshot after repeat join = %d, want 1", len(server.rooms.Snapshot()))
	}
	server.removeSession(session)
	if len(server.rooms.Snapshot()) != 0 {
		t.Fatalf("room snapshot after leave = %d, want 0", len(server.rooms.Snapshot()))
	}
}

// writeTestPacket 通过正式编码链路写出测试用 Head + Body 传输 Packet。
func writeTestPacket(t *testing.T, conn net.Conn, packet *protocol.Packet) {
	t.Helper()
	if err := writePacket(conn, packet); err != nil {
		t.Fatalf("write packet: %v", err)
	}
}

// readTestPacket 通过正式分帧链路读取测试用 Head + Body 传输 Packet，并解析业务 Protobuf。
func readTestPacket(t *testing.T, conn net.Conn) *protocol.Packet {
	t.Helper()
	transport, err := readTransportPacket(conn)
	if err != nil {
		t.Fatalf("read transport packet: %v", err)
	}
	packet := &protocol.Packet{}
	if err := proto.Unmarshal(transport.body, packet); err != nil {
		t.Fatalf("unmarshal packet: %v", err)
	}
	return packet
}

// newNetxItemStore 提供 netx 集成测试所需的内存背包存储。
func newNetxItemStore() *netxItemStore { return &netxItemStore{stacks: make(map[string]*item.Stack)} }

type netxItemStore struct{ stacks map[string]*item.Stack }

func (s *netxItemStore) LoadAll(context.Context, string) ([]*item.Stack, error) { return nil, nil }
func (s *netxItemStore) Upsert(_ context.Context, stack *item.Stack) error {
	s.stacks[stack.PlayerID+":"+stack.ItemID] = stack
	return nil
}

type netxPoiStore struct{}

func newNetxPoiStore() *netxPoiStore                              { return &netxPoiStore{} }
func (*netxPoiStore) LoadAll(context.Context) ([]*poi.Poi, error) { return nil, nil }
func (*netxPoiStore) Upsert(context.Context, *poi.Poi) error      { return nil }
