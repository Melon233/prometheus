using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xuan.Prometheus.NetworkKit.Transport
{
    /// <summary>网络传输层抽象；上层只接收和发送原始字节，不感知 TCP 或其他具体实现。</summary>
    public interface IByteTransport : IDisposable
    {
        /// <summary>传输层是否已经建立连接。</summary>
        bool IsConnected { get; }

        /// <summary>连接到指定远端地址。</summary>
        Task ConnectAsync(string host, int port, CancellationToken cancellationToken);

        /// <summary>发送完整字节序列。</summary>
        Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

        /// <summary>接收一段字节；返回零表示远端关闭连接。</summary>
        Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);

        /// <summary>主动关闭传输连接。</summary>
        void Close();
    }
}
