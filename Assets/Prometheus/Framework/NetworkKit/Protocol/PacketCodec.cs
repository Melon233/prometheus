using Google.Protobuf;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.NetworkKit.Framing;

namespace Xuan.Prometheus.NetworkKit.Protocol
{
    /// <summary>业务 Protobuf Packet 编解码器；协议层只负责变长 Body 与传输 Packet 之间的转换。</summary>
    internal static class PacketCodec
    {
        /// <summary>把业务 Packet 序列化为 Body，并与包含 BodyLength 首字段的定长 Head 组合成完整传输 Packet。</summary>
        public static byte[] Encode(Packet packet) { return TransportPacketCodec.Encode(new TransportPacket(packet.ToByteArray())); }

        /// <summary>从完整传输 Packet 的变长 Body 反序列化业务 Packet。</summary>
        public static Packet Decode(TransportPacket packet) { return Packet.Parser.ParseFrom(packet.Body); }
    }
}
