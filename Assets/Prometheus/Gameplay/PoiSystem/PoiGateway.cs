using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.Service;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 世界领域网络适配器的正式实现。
    /// 每个方法只做三件事：组装 Packet、经由通用通道发出、取出本领域关心的响应字段。
    /// 因此新增一个世界请求只需要改动本文件，不再需要扩展任何跨领域的服务接口。
    /// </summary>
    internal sealed class PoiGateway : XSystem, IPoiGateway
    {
        /// <summary>构造注入的会话通道；依赖写在签名上，注册顺序因此由编译器强制。</summary>
        private readonly IServiceSystem Service;

        /// <summary>创建 POI 领域网关。</summary>
        /// <param name="serviceSystem">承载本领域请求的唯一会话通道。</param>
        public PoiGateway(IServiceSystem serviceSystem)
        {
            Service = serviceSystem ?? throw new System.ArgumentNullException(nameof(serviceSystem));
        }

        /// <summary>记录是否已经订阅通用 Push 流，保证订阅与退订严格对称。</summary>
        private bool isPushBound;

        /// <inheritdoc />
        public event Action<PlayerPositionPush> PositionReceived;

        /// <summary>在全部 System 注册完成后订阅通用 Push 流，开始分类本领域关心的推送。</summary>
        public override void AfterNew()
        {
            Service.PushReceived += OnPushReceived;
            isPushBound = true;
        }

        /// <inheritdoc />
        public async UniTask<PullChunkResponse> PullChunkAsync(int chunkId, CancellationToken cancellationToken = default)
        {
            return (await Service.RequestAsync(new Packet { PullChunk = new PullChunkRequest { ChunkId = chunkId } }, cancellationToken)).PullChunkResp;
        }

        /// <inheritdoc />
        public async UniTask<PullAllResponse> PullAllAsync(CancellationToken cancellationToken = default)
        {
            return (await Service.RequestAsync(new Packet { PullAll = new PullAllRequest() }, cancellationToken)).PullAllResp;
        }

        /// <inheritdoc />
        public async UniTask<InteractResponse> InteractAsync(string poiId, PoiOp op, CancellationToken cancellationToken = default)
        {
            // 领域枚举与协议枚举按序号一一对应；这层翻译属于适配器职责，不应外泄到 PoiSystem 的调用点。
            Packet request = new Packet { Interact = new InteractRequest { Id = poiId, Op = (Protocol.PoiOp)(int)op } };
            return (await Service.RequestAsync(request, cancellationToken)).InteractResp;
        }

        /// <inheritdoc />
        public async UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position, CancellationToken cancellationToken = default)
        {
            Packet request = new Packet { UpdatePosition = new UpdatePositionRequest { X = position.x, Y = position.y, Z = position.z } };
            return (await Service.RequestAsync(request, cancellationToken)).UpdatePositionResp;
        }

        /// <summary>退订通用 Push 流并清空本领域订阅者；Gateway 早于 ServiceSystem 释放，因此此时通道仍然可用。</summary>
        public override void Dispose()
        {
            if (isPushBound)
            {
                Service.PushReceived -= OnPushReceived;
                isPushBound = false;
            }

            PositionReceived = null;
        }

        /// <summary>从通用 Packet 中挑出世界领域关心的推送并以强类型转发。</summary>
        private void OnPushReceived(Packet packet)
        {
            if (packet.PlayerPosition != null) PositionReceived?.Invoke(packet.PlayerPosition);
        }
    }
}
