using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.Service
{
    /// <summary>定义玩法系统可使用的统一网络请求和服务器 Push 入口，调用方不接触 NetworkKit 客户端与会话。</summary>
    public interface IServiceSystem : ISystemContract
    {
        /// <summary>获取当前是否已经进入世界且底层网络连接仍然有效。</summary>
        bool IsWorldAvailable { get; }

        /// <summary>通知业务系统当前世界服务已经不可用；首次进入失败不会触发，仅用于已进入世界后的意外断线。</summary>
        event Action WorldUnavailable;

        /// <summary>接收默认房间下发的玩家位置 Push；回调由 ServiceSystem 在 Unity 主线程触发。</summary>
        event Action<PlayerPositionPush> PositionReceived;

        /// <summary>进入默认世界房间并返回服务器保存的玩家位置；底层连接过程不对业务调用方暴露。</summary>
        UniTask<JoinRoomResponse> EnterWorldAsync(CancellationToken cancellationToken = default);

        /// <summary>拉取指定世界区块的 POI 状态。</summary>
        UniTask<PullChunkResponse> PullChunkAsync(int chunkId, CancellationToken cancellationToken = default);

        /// <summary>拉取当前世界全部 POI 状态。</summary>
        UniTask<PullAllResponse> PullAllAsync(CancellationToken cancellationToken = default);

        /// <summary>提交一次 POI 权威交互请求。</summary>
        UniTask<InteractResponse> InteractAsync(string id, PoiOp op, CancellationToken cancellationToken = default);

        /// <summary>获取当前会话玩家的权威背包快照。</summary>
        UniTask<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default);

        /// <summary>执行一次抽卡并返回奖励与最新背包快照。</summary>
        UniTask<GachaResponse> DrawGachaAsync(CancellationToken cancellationToken = default);

        /// <summary>上传当前玩家世界坐标。</summary>
        UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position, CancellationToken cancellationToken = default);
    }
}
