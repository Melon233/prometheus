using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Transport;

namespace Xuan.Prometheus.NetworkKit.Framing
{
    /// <summary>处理 4 字节大端长度前缀，解决 TCP 粘包和半包，不解析帧体内容。</summary>
    internal static class LengthPrefixedFrameCodec
    {
        /// <summary>单帧最大字节数，与服务器限制保持一致。</summary>
        public const int MaxFrameBytes = 16 * 1024 * 1024;

        /// <summary>为帧体添加 4 字节大端长度前缀。</summary>
        public static byte[] Encode(ReadOnlyMemory<byte> body)
        {
            if (body.Length <= 0 || body.Length > MaxFrameBytes) throw new InvalidDataException($"非法帧长度：{body.Length}");
            byte[] frame = new byte[body.Length + 4];
            frame[0] = (byte)(body.Length >> 24);
            frame[1] = (byte)(body.Length >> 16);
            frame[2] = (byte)(body.Length >> 8);
            frame[3] = (byte)body.Length;
            body.CopyTo(frame.AsMemory(4));
            return frame;
        }

        /// <summary>从传输层精确读取一个完整长度帧。</summary>
        public static async Task<byte[]> ReadAsync(IByteTransport transport, CancellationToken cancellationToken)
        {
            byte[] prefix = new byte[4];
            await ReadExactlyAsync(transport, prefix, cancellationToken);
            int length = (prefix[0] << 24) | (prefix[1] << 16) | (prefix[2] << 8) | prefix[3];
            if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException($"非法帧长度：{length}");
            byte[] body = new byte[length];
            await ReadExactlyAsync(transport, body, cancellationToken);
            return body;
        }

        /// <summary>循环读取直到填满目标缓冲区，避免网络半包被上层误认为完整消息。</summary>
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
