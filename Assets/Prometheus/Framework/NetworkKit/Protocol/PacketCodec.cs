using System;
using Google.Protobuf;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.NetworkKit.Framing;

namespace Xuan.Prometheus.NetworkKit.Protocol
{
    /// <summary>Packet Protobuf 编解码器；协议层不直接操作 Socket 或请求状态。</summary>
    public static class PacketCodec
    {
        /// <summary>把 Packet 序列化成带长度前缀的完整发送帧。</summary>
        public static byte[] Encode(Packet packet) { return LengthPrefixedFrameCodec.Encode(packet.ToByteArray()); }

        /// <summary>从完整帧体反序列化 Packet。</summary>
        public static Packet Decode(ReadOnlyMemory<byte> body) { return Packet.Parser.ParseFrom(body.ToArray()); }
    }
}
