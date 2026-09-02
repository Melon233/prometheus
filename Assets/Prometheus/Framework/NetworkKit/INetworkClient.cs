using System;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit
{
    /// <summary>定义业务无关的网络连接、请求关联和 Push 分发能力，不包含任何具体游戏请求。</summary>
    public interface INetworkClient : IDisposable
    {
        /// <summary>获取底层网络会话是否已经连接。</summary>
        bool IsConnected { get; }

        /// <summary>接收尚未匹配请求编号的通用 Push Packet；回调只在 PumpEvents 调用线程触发。</summary>
        event Action<Packet> PushReceived;

        /// <summary>通知上层当前连接因接收异常意外失效；主动断连与永久释放不会触发，回调只在 PumpEvents 调用线程执行。</summary>
        event Action Disconnected;

        /// <summary>连接指定网络端点并启动接收循环。</summary>
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>主动断开当前连接并结束全部等待中的请求，客户端实例仍可重新连接。</summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>断开现有连接后重新连接指定端点。</summary>
        Task ReconnectAsync(string host, int port, CancellationToken cancellationToken = default);

        /// <summary>发送通用 Packet 请求，并按请求编号等待对应响应。</summary>
        Task<Packet> RequestAsync(Packet request, CancellationToken cancellationToken = default);

        /// <summary>在调用线程分发已经收到的通用 Push Packet。</summary>
        void PumpEvents();
    }
}
