using System;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Session;
using Xuan.Prometheus.NetworkKit.Transport;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit
{
    /// <summary>业务无关的网络门面；只组合会话并公开连接、请求关联和 Push 分发能力。</summary>
    internal sealed class NetworkClient : INetworkClient
    {
        /// <summary>使用 TCP 创建客户端网络门面。</summary>
        public NetworkClient() : this(new TcpByteTransport()) { }

        /// <summary>使用指定传输实现创建客户端网络门面，便于替换 Mock 传输测试。</summary>
        public NetworkClient(IByteTransport transport) { Session = new NetworkSession(transport); }

        /// <summary>底层会话；主循环应定期调用 PumpEvents。</summary>
        private NetworkSession Session { get; }

        /// <inheritdoc />
        public bool IsConnected => Session.IsConnected;

        /// <inheritdoc />
        public event Action<Packet> PushReceived
        {
            add { Session.PushReceived += value; }
            remove { Session.PushReceived -= value; }
        }

        /// <inheritdoc />
        public event Action Disconnected
        {
            add { Session.Disconnected += value; }
            remove { Session.Disconnected -= value; }
        }

        /// <inheritdoc />
        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default) { return Session.ConnectAsync(host, port, cancellationToken); }

        /// <inheritdoc />
        public Task DisconnectAsync(CancellationToken cancellationToken = default) { return Session.DisconnectAsync(cancellationToken); }

        /// <inheritdoc />
        public Task ReconnectAsync(string host, int port, CancellationToken cancellationToken = default) { return Session.ReconnectAsync(host, port, cancellationToken); }

        /// <inheritdoc />
        public Task<Packet> RequestAsync(Packet request, CancellationToken cancellationToken = default) { return Session.RequestAsync(request, cancellationToken); }

        /// <summary>在调用线程分发通用服务器 Push。</summary>
        public void PumpEvents() { Session.PumpEvents(); }

        /// <summary>释放网络门面及其唯一会话。</summary>
        public void Dispose() { Session.Dispose(); }
    }
}
