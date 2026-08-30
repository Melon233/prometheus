using System;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Services;
using Xuan.Prometheus.NetworkKit.Session;
using Xuan.Prometheus.NetworkKit.Transport;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit
{
    /// <summary>客户端网络门面；组合会话、默认房间、POI 和抽卡服务，业务代码不接触传输层。</summary>
    public sealed class NetworkClient : IDisposable
    {
        /// <summary>使用 TCP 创建客户端网络门面。</summary>
        public NetworkClient() : this(new TcpByteTransport(), null) { }

        /// <summary>使用默认 TCP 传输和指定稳定玩家 ID 创建网络门面。</summary>
        public NetworkClient(string playerId) : this(new TcpByteTransport(), playerId) { }

        /// <summary>使用指定传输实现创建客户端网络门面，便于替换 Mock 传输测试。</summary>
        public NetworkClient(IByteTransport transport) : this(transport, null) { }

        /// <summary>使用指定传输实现和稳定玩家 ID 创建网络门面；玩家 ID 由上层平台存储提供。</summary>
        public NetworkClient(IByteTransport transport, string playerId)
        {
            Session = new NetworkSession(transport);
            Room = new DefaultRoomService(Session, playerId);
            Poi = new PoiService(Session);
            Gacha = new GachaService(Session);
        }

        /// <summary>底层会话；主循环应定期调用 PumpEvents。</summary>
        public NetworkSession Session { get; }

        /// <summary>默认房间业务服务。</summary>
        public DefaultRoomService Room { get; }

        /// <summary>POI 业务服务。</summary>
        public PoiService Poi { get; }

        /// <summary>抽卡业务服务。</summary>
        public GachaService Gacha { get; }

        /// <summary>连接服务器并加入唯一默认房间。</summary>
        public async Task<JoinRoomResponse> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            await Session.ConnectAsync(host, port, cancellationToken);
            return await Room.JoinAsync(cancellationToken);
        }

        /// <summary>在 Unity 主线程分发坐标等服务器推送。</summary>
        public void PumpEvents() { Session.PumpEvents(); }

        /// <summary>释放网络门面及其所有子服务。</summary>
        public void Dispose() { Room.Dispose(); Session.Dispose(); }
    }
}
