using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Xuan.Prometheus.NetworkKit.Framing;
using Xuan.Prometheus.NetworkKit.Transport;

namespace Xuan.Prometheus.NetworkKit.Tests
{
    /// <summary>验证客户端传输 Packet 的固定 Head、变长 Body 和 TCP 半包读取行为。</summary>
    public sealed class TransportPacketCodecTests
    {
        /// <summary>确认编码后的首字段是大端 BodyLength，并确认分段传输不会截断变长 Body。</summary>
        [Test]
        public async Task EncodeAndReadAsync_PreservesFixedHeadAndVariableBodyAcrossPartialReads()
        {
            byte[] expectedBody = { 10, 20, 30, 40, 50, 60, 70 };
            byte[] encoded = TransportPacketCodec.Encode(new TransportPacket(expectedBody));
            Assert.That(encoded.Length, Is.EqualTo(PacketHead.ByteCount + expectedBody.Length));
            Assert.That(encoded[0], Is.EqualTo(0));
            Assert.That(encoded[1], Is.EqualTo(0));
            Assert.That(encoded[2], Is.EqualTo(0));
            Assert.That(encoded[3], Is.EqualTo(expectedBody.Length));
            using PartialReadTransport transport = new PartialReadTransport(encoded, 2);
            TransportPacket decoded = await TransportPacketCodec.ReadAsync(transport, CancellationToken.None);
            Assert.That(decoded.Head.BodyLength, Is.EqualTo(expectedBody.Length));
            Assert.That(decoded.Body, Is.EqualTo(expectedBody));
        }

        /// <summary>以每次最多固定字节数的方式模拟 TCP 半包，仅实现当前测试所需的接收能力。</summary>
        private sealed class PartialReadTransport : IByteTransport
        {
            private readonly byte[] source;
            private readonly int maxReadBytes;
            private int offset;

            /// <summary>创建基于内存字节流的半包传输。</summary>
            public PartialReadTransport(byte[] source, int maxReadBytes)
            {
                this.source = source;
                this.maxReadBytes = maxReadBytes;
            }

            /// <summary>测试传输始终视为已连接。</summary>
            public bool IsConnected => true;

            /// <summary>测试不执行真实连接。</summary>
            public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) { return Task.CompletedTask; }

            /// <summary>测试不执行发送。</summary>
            public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) { return Task.CompletedTask; }

            /// <summary>每次最多复制 maxReadBytes，强制分帧器经历 Head 与 Body 半包。</summary>
            public Task<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            {
                int count = Math.Min(Math.Min(buffer.Length, maxReadBytes), source.Length - offset);
                source.AsMemory(offset, count).CopyTo(buffer);
                offset += count;
                return Task.FromResult(count);
            }

            /// <summary>内存测试传输无需关闭资源。</summary>
            public void Close() { }

            /// <summary>内存测试传输无需释放资源。</summary>
            public void Dispose() { }
        }
    }
}
