using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Xuan.Prometheus.NetworkKit.Transport
{
    /// <summary>基于 TCP 的字节传输实现；不负责长度帧和 Protobuf 编解码。</summary>
    public sealed class TcpByteTransport : IByteTransport
    {
        private TcpClient client;
        private NetworkStream stream;

        /// <summary>获取底层 TCP 连接状态。</summary>
        public bool IsConnected => client != null && client.Connected && stream != null;

        /// <summary>建立 TCP 连接并缓存 NetworkStream。</summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            if (IsConnected) return;
            client = new TcpClient();
            cancellationToken.ThrowIfCancellationRequested();
            await client.ConnectAsync(host, port);
            cancellationToken.ThrowIfCancellationRequested();
            stream = client.GetStream();
        }

        /// <summary>将字节完整写入 TCP 流。</summary>
        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) { return stream.WriteAsync(data, cancellationToken).AsTask(); }

        /// <summary>从 TCP 流读取一段字节。</summary>
        public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) { return stream.ReadAsync(buffer, cancellationToken).AsTask(); }

        /// <summary>关闭并释放 TCP 资源。</summary>
        public void Close() { stream?.Dispose(); client?.Dispose(); stream = null; client = null; }

        /// <summary>释放传输资源。</summary>
        public void Dispose() { Close(); }
    }
}
