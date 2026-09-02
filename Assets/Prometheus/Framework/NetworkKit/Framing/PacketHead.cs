using System;
using System.IO;

namespace Xuan.Prometheus.NetworkKit.Framing
{
    /// <summary>描述传输 Packet 的定长 Head；首字段固定为 4 字节大端 BodyLength，后续扩展字段必须追加在该字段之后。</summary>
    internal readonly struct PacketHead
    {
        /// <summary>Head 当前固定占用 4 字节，收包方必须先完整读取该长度才能解析 BodyLength。</summary>
        public const int ByteCount = 4;

        /// <summary>单个变长 Body 的最大字节数，与服务器限制保持一致。</summary>
        public const int MaxBodyBytes = 16 * 1024 * 1024;

        /// <summary>创建包含 Body 字节数的定长 Head。</summary>
        public PacketHead(int bodyLength)
        {
            if (bodyLength <= 0 || bodyLength > MaxBodyBytes) throw new InvalidDataException($"非法 Packet Body 长度：{bodyLength}");
            BodyLength = bodyLength;
        }

        /// <summary>获取当前 Packet 变长 Body 的字节数。</summary>
        public int BodyLength { get; }

        /// <summary>把定长 Head 写入目标缓冲区，BodyLength 始终位于第 0 字节并使用网络大端序。</summary>
        public void WriteTo(Span<byte> destination)
        {
            if (destination.Length < ByteCount) throw new ArgumentException($"Packet Head 缓冲区至少需要 {ByteCount} 字节。", nameof(destination));
            destination[0] = (byte)(BodyLength >> 24);
            destination[1] = (byte)(BodyLength >> 16);
            destination[2] = (byte)(BodyLength >> 8);
            destination[3] = (byte)BodyLength;
        }

        /// <summary>从完整定长字节区解析 Head，并立即校验首字段 BodyLength。</summary>
        public static PacketHead Parse(ReadOnlySpan<byte> source)
        {
            if (source.Length < ByteCount) throw new InvalidDataException($"Packet Head 不完整：{source.Length}/{ByteCount}");
            int bodyLength = (source[0] << 24) | (source[1] << 16) | (source[2] << 8) | source[3];
            return new PacketHead(bodyLength);
        }
    }
}
