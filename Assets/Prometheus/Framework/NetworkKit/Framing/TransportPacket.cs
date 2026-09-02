using System;

namespace Xuan.Prometheus.NetworkKit.Framing
{
    /// <summary>表示传输层完整 Packet，由一个定长 Head 和一个由 Head.BodyLength 描述的变长 Body 组成。</summary>
    internal readonly struct TransportPacket
    {
        /// <summary>使用业务序列化结果创建传输 Packet，并自动生成与 Body 长度一致的 Head。</summary>
        public TransportPacket(byte[] body)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            Head = new PacketHead(body.Length);
        }

        /// <summary>使用已解析的 Head 和 Body 创建接收 Packet，并验证两者长度严格一致。</summary>
        public TransportPacket(PacketHead head, byte[] body)
        {
            Body = body ?? throw new ArgumentNullException(nameof(body));
            if (head.BodyLength != body.Length) throw new ArgumentException($"Packet Head 声明的 Body 长度 {head.BodyLength} 与实际长度 {body.Length} 不一致。", nameof(body));
            Head = head;
        }

        /// <summary>获取定长传输头。</summary>
        public PacketHead Head { get; }

        /// <summary>获取变长业务字节体。</summary>
        public byte[] Body { get; }
    }
}
