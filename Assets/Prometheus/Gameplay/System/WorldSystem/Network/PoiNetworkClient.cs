using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.NetworkKit;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>POI 网络兼容外观；WorldSystem 保持原有 UniTask API，底层统一使用 Framework/NetworkKit 会话和 RPC。</summary>
    public sealed class PoiNetworkClient : IDisposable
    {
        private readonly string host;
        private readonly int port;
        private readonly NetworkClient networkClient = new NetworkClient();
        private readonly SemaphoreSlim connectLock = new SemaphoreSlim(1, 1);

        /// <summary>创建指向指定 POI 服务器的客户端外观。</summary>
        public PoiNetworkClient(string host, int port) { this.host = host; this.port = port; }

        /// <summary>当前是否已建立 NetworkKit 会话连接。</summary>
        public bool IsConnected => networkClient.Session.IsConnected;

        /// <summary>按区块拉取 POI 状态。</summary>
        public async UniTask<PullChunkResponse> PullChunkAsync(int chunkId)
        {
            await EnsureConnectedAsync();
            return await networkClient.Poi.PullChunkAsync(chunkId);
        }

        /// <summary>全量拉取 POI 状态。</summary>
        public async UniTask<PullAllResponse> PullAllAsync()
        {
            await EnsureConnectedAsync();
            return await networkClient.Poi.PullAllAsync();
        }

        /// <summary>提交 POI 交互并返回服务器权威结果。</summary>
        public async UniTask<InteractResponse> InteractAsync(string id, PoiOp op)
        {
            await EnsureConnectedAsync();
            return await networkClient.Poi.InteractAsync(id, (Protocol.PoiOp)(int)op);
        }

        /// <summary>获取当前会话玩家背包。</summary>
        public async UniTask<GetItemsResponse> GetItemsAsync()
        {
            await EnsureConnectedAsync();
            return await networkClient.Poi.GetItemsAsync();
        }

        /// <summary>请求一次抽卡；服务器扣除一个神瞳并返回非神瞳奖励及最新背包快照。</summary>
        public async UniTask<GachaResponse> DrawGachaAsync()
        {
            await EnsureConnectedAsync();
            return await networkClient.Gacha.DrawAsync();
        }

        /// <summary>上传当前玩家坐标；服务器会把坐标推送给默认房间全部在线玩家并回推发送者。</summary>
        public async UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position)
        {
            await EnsureConnectedAsync();
            return await networkClient.Room.UploadPositionAsync(position.x, position.y, position.z);
        }

        /// <summary>收到默认房间坐标推送；调用方应在生命周期结束时解除订阅。</summary>
        public event Action<PlayerPositionPush> PositionReceived
        {
            add { networkClient.Room.PositionReceived += value; }
            remove { networkClient.Room.PositionReceived -= value; }
        }

        /// <summary>在 Unity 主线程分发 NetworkKit 收到的服务器推送。</summary>
        public void PumpEvents() { networkClient.PumpEvents(); }

        /// <summary>释放底层 NetworkKit 会话。</summary>
        public void Dispose() { networkClient.Dispose(); connectLock.Dispose(); }

        /// <summary>并发请求共享连接闸门，避免多个 AOI 请求同时创建 TCP 连接。</summary>
        private async UniTask EnsureConnectedAsync()
        {
            if (IsConnected) return;
            await connectLock.WaitAsync().AsUniTask();
            try
            {
                if (IsConnected) return;
                await networkClient.ConnectAsync(host, port);
            }
            finally { connectLock.Release(); }
        }
    }
}
