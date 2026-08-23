using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;
using Google.Protobuf;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// POI 客户端网络层：通过 TCP + 4 字节大端长度前缀 + protobuf Packet 与 Go 服务器通信。
    /// 提供按区块拉取（PullChunk）、全量拉取（PullAll，调试用）与交互请求（Interact）；服务器为权威。
    /// </summary>
    public sealed class PoiNetworkClient : IDisposable
    {
        /// <summary>单帧 protobuf 字节上限，与服务器保持一致，防止异常长度导致无界分配。</summary>
        private const int MaxFrameBytes = 16 * 1024 * 1024;

        private readonly string host;
        private readonly int port;
        private readonly SemaphoreSlim requestLock = new SemaphoreSlim(1, 1); // 串行化请求-响应，避免并发写冲突
        private TcpClient client;
        private NetworkStream stream;

        public PoiNetworkClient(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        /// <summary>当前是否已建立连接。</summary>
        public bool IsConnected => client != null && client.Connected;

        /// <summary>按区块拉取：拉取指定 chunkId 内的 POI 状态。</summary>
        public async UniTask<PullChunkResponse> PullChunkAsync(int chunkId)
        {
            Packet response = await RequestAsync(new Packet { PullChunk = new PullChunkRequest { ChunkId = chunkId } });
            return response.PullChunkResp;
        }

        /// <summary>全量拉取（调试用）。</summary>
        public async UniTask<PullAllResponse> PullAllAsync()
        {
            Packet response = await RequestAsync(new Packet { PullAll = new PullAllRequest() });
            return response.PullAllResp;
        }

        /// <summary>交互请求：按 id + 操作提交，服务器返回确认结果。</summary>
        public async UniTask<InteractResponse> InteractAsync(string id, PoiOp op)
        {
            Packet response = await RequestAsync(new Packet { Interact = new InteractRequest { Id = id, Op = ToProtoOp(op) } });
            return response.InteractResp;
        }

        /// <summary>获取当前玩家全部背包物品。</summary>
        public async UniTask<GetItemsResponse> GetItemsAsync()
        {
            Packet response = await RequestAsync(new Packet { GetItems = new GetItemsRequest() });
            return response.GetItemsResp;
        }

        /// <summary>发送一帧请求并等待一帧响应（当前协议为同步请求-响应）。</summary>
        private async UniTask<Packet> RequestAsync(Packet request)
        {
            await requestLock.WaitAsync().AsUniTask();
            try
            {
                NetworkStream s = await EnsureConnectedAsync();
                byte[] body = request.ToByteArray();
                byte[] prefix = EncodeLength(body.Length);
                await s.WriteAsync(prefix, 0, prefix.Length).AsUniTask();
                await s.WriteAsync(body, 0, body.Length).AsUniTask();

                byte[] lengthBuffer = new byte[4];
                await ReadExactlyAsync(s, lengthBuffer, 4);
                int length = DecodeLength(lengthBuffer);
                if (length <= 0 || length > MaxFrameBytes) throw new InvalidDataException($"非法帧长度：{length}");
                byte[] responseBody = new byte[length];
                await ReadExactlyAsync(s, responseBody, length);
                return Packet.Parser.ParseFrom(responseBody);
            }
            finally
            {
                requestLock.Release();
            }
        }

        /// <summary>确保连接已建立（幂等）。</summary>
        private async UniTask<NetworkStream> EnsureConnectedAsync()
        {
            if (IsConnected) return stream;
            client = new TcpClient();
            await client.ConnectAsync(host, port).AsUniTask();
            stream = client.GetStream();
            return stream;
        }

        /// <summary>从流中精确读取 count 字节。</summary>
        private static async UniTask ReadExactlyAsync(Stream s, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = await s.ReadAsync(buffer, read, count - read).AsUniTask();
                if (n <= 0) throw new EndOfStreamException("连接已关闭");
                read += n;
            }
        }

        /// <summary>int 长度编码为 4 字节大端。</summary>
        private static byte[] EncodeLength(int length)
        {
            return new byte[] { (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length };
        }

        /// <summary>4 字节大端解码为 int 长度。</summary>
        private static int DecodeLength(byte[] prefix)
        {
            return (prefix[0] << 24) | (prefix[1] << 16) | (prefix[2] << 8) | prefix[3];
        }

        /// <summary>客户端 PoiOp 与协议 PoiOp 数值一一对应，直接按数值转换。</summary>
        private static Protocol.PoiOp ToProtoOp(PoiOp op) => (Protocol.PoiOp)(int)op;

        public void Dispose()
        {
            requestLock.Dispose();
            stream?.Dispose();
            client?.Dispose();
            stream = null;
            client = null;
        }
    }
}
