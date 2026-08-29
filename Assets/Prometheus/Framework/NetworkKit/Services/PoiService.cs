using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Session;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit.Services
{
    /// <summary>POI 业务服务；把协议请求封装为不会暴露 Socket 和帧细节的业务接口。</summary>
    public sealed class PoiService
    {
        private readonly NetworkSession session;

        /// <summary>创建 POI 服务。</summary>
        public PoiService(NetworkSession session) { this.session = session; }

        /// <summary>全量拉取 POI 状态。</summary>
        public async Task<PullAllResponse> PullAllAsync(CancellationToken cancellationToken = default) { Packet response = await session.RequestAsync(new Packet { PullAll = new PullAllRequest() }, cancellationToken); return response.PullAllResp; }

        /// <summary>按区块拉取 POI 状态。</summary>
        public async Task<PullChunkResponse> PullChunkAsync(int chunkId, CancellationToken cancellationToken = default) { Packet response = await session.RequestAsync(new Packet { PullChunk = new PullChunkRequest { ChunkId = chunkId } }, cancellationToken); return response.PullChunkResp; }

        /// <summary>提交 POI 交互。</summary>
        public async Task<InteractResponse> InteractAsync(string id, PoiOp op, CancellationToken cancellationToken = default) { Packet response = await session.RequestAsync(new Packet { Interact = new InteractRequest { Id = id, Op = op } }, cancellationToken); return response.InteractResp; }

        /// <summary>获取当前会话玩家背包。</summary>
        public async Task<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default) { Packet response = await session.RequestAsync(new Packet { GetItems = new GetItemsRequest() }, cancellationToken); return response.GetItemsResp; }
    }
}
