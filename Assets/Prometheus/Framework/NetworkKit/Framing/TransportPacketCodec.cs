using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Transport;

namespace Xuan.Prometheus.NetworkKit.Framing
{
    /// <summary>负责传输 Packet 的 Head + Body 编解码，解决 TCP 粘包与半包但不解析业务 Body。</summary>
    internal static class TransportPacketCodec
    {
        /// <summary>按定长 Head 在前、变长 Body 在后的布局生成完整发送字节。</summary>
        public static byte[] Encode(TransportPacket packet)
        {
            byte[] bytes = new byte[PacketHead.ByteCount + packet.Head.BodyLength];
            packet.Head.WriteTo(bytes.AsSpan(0, PacketHead.ByteCount));
            packet.Body.CopyTo(bytes, PacketHead.ByteCount);
            return bytes;
        }

        /// <summary>先精确读取固定 Head，再依据其首字段 BodyLength 精确读取变长 Body。</summary>
        public static async Task<TransportPacket> ReadAsync(IByteTransport transport, CancellationToken cancellationToken)
        {
            byte[] headBytes = new byte[PacketHead.ByteCount];
            await ReadExactlyAsync(transport, headBytes, cancellationToken);
            PacketHead head = PacketHead.Parse(headBytes);
            byte[] body = new byte[head.BodyLength];
            await ReadExactlyAsync(transport, body, cancellationToken);
            return new TransportPacket(head, body);
        }

        /// <summary>循环读取直到填满目标缓冲区，确保 TCP 半包不会越过 Packet 的 Head 或 Body 边界。</summary>
        private static async Task ReadExactlyAsync(IByteTransport transport, byte[] buffer, CancellationToken cancellationToken)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await transport.ReceiveAsync(buffer.AsMemory(offset), cancellationToken);
                if (count <= 0) throw new EndOfStreamException("服务器已关闭连接");
                offset += count;
            }
        }
    }
}
