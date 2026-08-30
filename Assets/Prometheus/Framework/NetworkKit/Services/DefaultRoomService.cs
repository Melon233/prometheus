using System;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Session;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit.Services
{
    /// <summary>默认房间业务服务；负责加入唯一房间、上传自身坐标和接收坐标推送。</summary>
    public sealed class DefaultRoomService : IDisposable
    {
        private readonly NetworkSession session;

        /// <summary>创建默认房间服务并绑定会话推送分发。</summary>
        public DefaultRoomService(NetworkSession session, string playerId = null) { this.session = session; session.PushReceived += HandlePush; PlayerId = string.IsNullOrWhiteSpace(playerId) ? Guid.NewGuid().ToString("N") : playerId; }

        /// <summary>客户端本次身份标识；服务器会在 JoinRoom 中绑定该身份。</summary>
        public string PlayerId { get; }

        /// <summary>加入服务器唯一默认房间。</summary>
        public async Task<JoinRoomResponse> JoinAsync(CancellationToken cancellationToken = default)
        {
            Packet response = await session.RequestAsync(new Packet { JoinRoom = new JoinRoomRequest { PlayerId = PlayerId } }, cancellationToken);
            return response.JoinRoomResp;
        }

        /// <summary>上传当前玩家坐标；服务器会把坐标推送给默认房间全部在线玩家并回推发送者。</summary>
        public async Task<UpdatePositionResponse> UploadPositionAsync(float x, float y, float z, CancellationToken cancellationToken = default)
        {
            Packet response = await session.RequestAsync(new Packet { UpdatePosition = new UpdatePositionRequest { X = x, Y = y, Z = z } }, cancellationToken);
            return response.UpdatePositionResp;
        }

        /// <summary>收到默认房间坐标推送时触发；事件由 NetworkSession.PumpEvents 在主线程分发。</summary>
        public event Action<PlayerPositionPush> PositionReceived;

        /// <summary>解除会话推送订阅。</summary>
        public void Dispose() { session.PushReceived -= HandlePush; }

        /// <summary>筛选坐标推送，其他业务推送交回会话队列由其他服务处理。</summary>
        private void HandlePush(Packet packet) { if (packet.PlayerPosition != null) PositionReceived?.Invoke(packet.PlayerPosition); }
    }
}
