using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 世界领域的网络适配器：把世界玩法语义翻译成协议 Packet，再把响应翻译回世界语义。
    /// 它是本领域唯一被允许调用 <see cref="Service.IServiceSystem.RequestAsync"/> 的位置；
    /// 会话、并发与断线语义仍然由 ServiceSystem 独占，Gateway 不持有任何连接状态。
    /// Gateway 同样不保存领域状态：响应的解释与缓存归 PoiSystem。
    /// </summary>
    public interface IPoiGateway : ISystemContract
    {
        /// <summary>接收默认房间下发的玩家位置 Push；回调已由 ServiceSystem 泵送到 Unity 主线程。</summary>
        event Action<PlayerPositionPush> PositionReceived;

        /// <summary>拉取指定世界区块的 POI 状态。</summary>
        /// <param name="chunkId">目标区块编号。</param>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<PullChunkResponse> PullChunkAsync(int chunkId, CancellationToken cancellationToken = default);

        /// <summary>拉取当前世界全部 POI 状态。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<PullAllResponse> PullAllAsync(CancellationToken cancellationToken = default);

        /// <summary>提交一次 POI 权威交互请求；参数使用世界领域枚举，协议枚举的转换由 Gateway 内部完成。</summary>
        /// <param name="poiId">目标 POI 的稳定 Id。</param>
        /// <param name="op">世界领域的交互类型。</param>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<InteractResponse> InteractAsync(string poiId, PoiOp op, CancellationToken cancellationToken = default);

        /// <summary>上传当前玩家世界坐标。</summary>
        /// <param name="position">当前上场成员的世界坐标。</param>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position, CancellationToken cancellationToken = default);
    }
}
