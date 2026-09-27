using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 世界领域的权威数据适配器：PoiSystem 只通过它读写 POI 状态与玩家位置，不关心数据来自哪里。
    ///
    /// 当前单机版由 <see cref="LocalPoiGateway"/> 在进程内执行权威规则，状态只保存在内存中；
    /// 联网版实现（Prometheus.Gameplay.Online 中的 NetworkPoiGateway）把同一组调用翻译成协议 Packet。
    /// 契约沿用协议消息作为数据载体，因此两种实现可以直接互换，PoiSystem 无需改动。
    /// Gateway 不保存 PoiSystem 的领域状态：响应的解释与场景表现仍归 PoiSystem。
    /// </summary>
    public interface IPoiGateway : ISystemContract
    {
        /// <summary>获取权威数据源当前是否可用；单机实现恒为 true，联网实现在断线后变为 false。</summary>
        bool IsAvailable { get; }

        /// <summary>权威数据源在进入世界后意外失效时触发；首次进入失败不会触发。单机实现永不触发。</summary>
        event Action Unavailable;

        /// <summary>接收权威数据源下发的玩家位置 Push；回调保证在 Unity 主线程触发。单机实现没有推送。</summary>
        event Action<PlayerPositionPush> PositionReceived;

        /// <summary>进入默认世界并返回上次保存的会话数据（主要是玩家位置）；首次进入时位置为空。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<JoinRoomResponse> EnterWorldAsync(CancellationToken cancellationToken = default);

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

        /// <summary>上传当前玩家世界坐标，供下次进入世界时恢复。</summary>
        /// <param name="position">当前上场成员的世界坐标。</param>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position, CancellationToken cancellationToken = default);
    }
}
