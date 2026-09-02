using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.NetworkKit;
using Xuan.Prometheus.NetworkKit.Rpc;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.Service
{
    /// <summary>封装单局唯一网络客户端，在内部管理连接，并向其他玩法系统提供业务请求、响应解析和业务 Push 分发。</summary>
    internal sealed class ServiceSystem : XSystem, IServiceSystem
    {
        /// <summary>本地开发服务器的默认回环地址。</summary>
        private const string DefaultHost = "127.0.0.1";

        /// <summary>玩法服务器的默认 TCP 监听端口。</summary>
        private const int DefaultPort = 9000;

        /// <summary>保存稳定本地玩家编号的 PlayerPrefs 键。</summary>
        private const string PlayerIdPrefsKey = "Prometheus.Network.PlayerId";

        /// <summary>当前服务系统连接的服务器地址。</summary>
        private readonly string host;

        /// <summary>当前服务系统连接的服务器端口。</summary>
        private readonly int port;

        /// <summary>由 ServiceSystem 独占并对其他 System 隐藏的底层 NetworkKit 客户端。</summary>
        private readonly INetworkClient networkClient;

        /// <summary>当前客户端加入默认房间时使用的稳定玩家编号。</summary>
        private readonly string playerId;

        /// <summary>串行化进入世界流程，保证并发业务请求不会创建多条会话或重复加入房间。</summary>
        private readonly SemaphoreSlim enterWorldLock = new SemaphoreSlim(1, 1);

        /// <summary>保护释放标记、活动异步操作计数和底层资源最终释放，避免同步 Dispose 与异步 continuation 竞态。</summary>
        private readonly object lifecycleGate = new object();

        /// <summary>在 ServiceSystem 释放时取消全部正在连接、等待响应或等待进入世界锁的业务操作。</summary>
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();

        /// <summary>记录尚未退出的公开异步业务调用；计数归零后才允许释放客户端和信号量。</summary>
        private int activeOperationCount;

        /// <summary>记录底层客户端、信号量和取消源是否已经完成最终释放。</summary>
        private bool resourcesReleased;

        /// <summary>记录 Dispose 已完成退订和生命期取消，防止最后一个异步调用抢先释放取消源。</summary>
        private bool disposalReadyForResourceRelease;

        /// <summary>记录本局是否已经尝试进入世界，失败后禁止重复访问服务器。</summary>
        private bool enterWorldAttempted;

        /// <summary>记录当前网络会话是否已经成功进入默认世界房间。</summary>
        private bool hasEnteredWorld;

        /// <summary>保存首次进入世界失败原因，使后续业务调用获得稳定错误而不重新连接。</summary>
        private Exception enterWorldFailure;

        /// <summary>保存首次连接成功的房间和持久化位置结果。</summary>
        private JoinRoomResponse joinResponse;

        /// <summary>记录系统是否已经释放，阻止生命周期结束后继续收发网络消息。</summary>
        private bool isDisposed;

        /// <summary>使用项目默认服务器地址和稳定玩家编号创建正式服务系统。</summary>
        internal ServiceSystem() : this(DefaultHost, DefaultPort, LoadOrCreatePlayerId(), NetworkClientFactory.Create()) { }

        /// <summary>使用指定连接信息、玩家编号和网络客户端创建服务系统，供程序集内部验证替换底层实现。</summary>
        internal ServiceSystem(string host, int port, string playerId, INetworkClient networkClient)
        {
            this.host = host;
            this.port = port;
            this.playerId = string.IsNullOrWhiteSpace(playerId) ? throw new ArgumentException("Player ID cannot be empty.", nameof(playerId)) : playerId;
            this.networkClient = networkClient ?? throw new ArgumentNullException(nameof(networkClient));
            this.networkClient.PushReceived += OnPushReceived;
            this.networkClient.Disconnected += OnDisconnected;
        }

        /// <inheritdoc />
        public bool IsWorldAvailable => !isDisposed && hasEnteredWorld && networkClient.IsConnected;

        /// <inheritdoc />
        public event Action WorldUnavailable;

        /// <inheritdoc />
        public event Action<PlayerPositionPush> PositionReceived;

        /// <inheritdoc />
        public async UniTask<JoinRoomResponse> EnterWorldAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return await EnterWorldCoreAsync(operationCancellation.Token); }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <inheritdoc />
        public async UniTask<PullChunkResponse> PullChunkAsync(int chunkId, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return (await RequestCoreAsync(new Packet { PullChunk = new PullChunkRequest { ChunkId = chunkId } }, operationCancellation.Token)).PullChunkResp; }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <inheritdoc />
        public async UniTask<PullAllResponse> PullAllAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return (await RequestCoreAsync(new Packet { PullAll = new PullAllRequest() }, operationCancellation.Token)).PullAllResp; }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <inheritdoc />
        public async UniTask<InteractResponse> InteractAsync(string id, PoiOp op, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return (await RequestCoreAsync(new Packet { Interact = new InteractRequest { Id = id, Op = op } }, operationCancellation.Token)).InteractResp; }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <inheritdoc />
        public async UniTask<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return (await RequestCoreAsync(new Packet { GetItems = new GetItemsRequest() }, operationCancellation.Token)).GetItemsResp; }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <inheritdoc />
        public async UniTask<GachaResponse> DrawGachaAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return (await RequestCoreAsync(new Packet { Gacha = new GachaRequest() }, operationCancellation.Token)).GachaResp; }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <inheritdoc />
        public async UniTask<UpdatePositionResponse> UploadPositionAsync(Vector3 position, CancellationToken cancellationToken = default)
        {
            CancellationTokenSource operationCancellation = BeginOperation(cancellationToken);
            try { return (await RequestCoreAsync(new Packet { UpdatePosition = new UpdatePositionRequest { X = position.x, Y = position.y, Z = position.z } }, operationCancellation.Token)).UpdatePositionResp; }
            finally { CompleteOperation(operationCancellation); }
        }

        /// <summary>在 ServiceSystem 更新阶段统一把 NetworkKit Push 分发到 Unity 主线程订阅者。</summary>
        public override void OnUpdate(float dt)
        {
            if (!isDisposed) networkClient.PumpEvents();
        }

        /// <summary>解除底层 Push 监听并释放本局唯一网络客户端。</summary>
        public override void Dispose()
        {
            lock (lifecycleGate)
            {
                if (isDisposed) return;
                isDisposed = true;
            }
            networkClient.PushReceived -= OnPushReceived;
            networkClient.Disconnected -= OnDisconnected;
            WorldUnavailable = null;
            PositionReceived = null;
            lifetimeCancellation.Cancel();
            bool releaseResources;
            lock (lifecycleGate)
            {
                disposalReadyForResourceRelease = true;
                releaseResources = activeOperationCount == 0;
                if (releaseResources) resourcesReleased = true;
            }
            if (releaseResources) ReleaseResources();
        }

        /// <summary>在调用方取消和系统生命期任一结束时取消当前业务操作，并登记活动操作以延迟底层资源释放。</summary>
        private CancellationTokenSource BeginOperation(CancellationToken cancellationToken)
        {
            lock (lifecycleGate)
            {
                ThrowIfDisposed();
                activeOperationCount++;
            }
            try { return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token); }
            catch
            {
                CompleteOperation(null);
                throw;
            }
        }

        /// <summary>结束一次公开异步调用；系统已经释放时，由最后退出的调用负责安全释放客户端和同步原语。</summary>
        private void CompleteOperation(CancellationTokenSource operationCancellation)
        {
            operationCancellation?.Dispose();
            bool releaseResources = false;
            lock (lifecycleGate)
            {
                activeOperationCount--;
                if (isDisposed && disposalReadyForResourceRelease && activeOperationCount == 0 && !resourcesReleased)
                {
                    resourcesReleased = true;
                    releaseResources = true;
                }
            }
            if (releaseResources) ReleaseResources();
        }

        /// <summary>在已经登记活动操作后串行进入世界；调用取消不会被缓存成不可重试的网络失败。</summary>
        private async UniTask<JoinRoomResponse> EnterWorldCoreAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasEnteredWorld) return joinResponse;
            await enterWorldLock.WaitAsync(cancellationToken).AsUniTask();
            try
            {
                if (hasEnteredWorld) return joinResponse;
                if (enterWorldAttempted) throw new InvalidOperationException("ServiceSystem failed to enter the world and cannot retry in the current session.", enterWorldFailure);
                enterWorldAttempted = true;
                try
                {
                    await networkClient.ConnectAsync(host, port, cancellationToken);
                    Packet response = await networkClient.RequestAsync(new Packet { JoinRoom = new JoinRoomRequest { PlayerId = playerId } }, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    joinResponse = response.JoinRoomResp;
                    hasEnteredWorld = true;
                    return joinResponse;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    enterWorldAttempted = false;
                    throw;
                }
                catch (Exception exception)
                {
                    enterWorldFailure = exception;
                    throw;
                }
            }
            finally { enterWorldLock.Release(); }
        }

        /// <summary>等待进入世界后发送业务 Packet；检测到断线错误时立即切换世界服务状态并禁止本局继续请求。</summary>
        private async UniTask<Packet> RequestCoreAsync(Packet request, CancellationToken cancellationToken)
        {
            await EnterWorldCoreAsync(cancellationToken);
            try { return await networkClient.RequestAsync(request, cancellationToken); }
            catch (NetworkError exception) when (exception.Kind == NetworkErrorKind.Disconnected)
            {
                MarkWorldUnavailable(exception);
                throw;
            }
        }

        /// <summary>按业务 Push 类型分类底层通用 Packet；当前仅公开默认房间玩家位置。</summary>
        private void OnPushReceived(Packet packet)
        {
            if (packet.PlayerPosition != null) PositionReceived?.Invoke(packet.PlayerPosition);
        }

        /// <summary>接收 NetworkKit 在主线程上报的意外断线，并转换为 Gameplay 层的世界不可用状态。</summary>
        private void OnDisconnected() { MarkWorldUnavailable(new InvalidOperationException("The world network connection was interrupted.")); }

        /// <summary>只在已成功进入世界后发布一次不可用通知，并缓存失败原因以阻止本局隐式重连。</summary>
        private void MarkWorldUnavailable(Exception exception)
        {
            if (!hasEnteredWorld) return;
            hasEnteredWorld = false;
            joinResponse = null;
            enterWorldAttempted = true;
            enterWorldFailure = exception;
            WorldUnavailable?.Invoke();
        }

        /// <summary>在不存在活动异步调用时最终释放网络客户端与同步原语，保证 continuation 不访问已释放对象。</summary>
        private void ReleaseResources()
        {
            networkClient.Dispose();
            enterWorldLock.Dispose();
            lifetimeCancellation.Dispose();
            joinResponse = null;
            enterWorldFailure = null;
            hasEnteredWorld = false;
        }

        /// <summary>阻止已经释放的 ServiceSystem 继续接收请求或建立连接。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(ServiceSystem));
        }

        /// <summary>读取并持久化本机玩家 ID，使重新启动游戏后仍能定位同一个服务器玩家文档。</summary>
        private static string LoadOrCreatePlayerId()
        {
            string playerId = PlayerPrefs.GetString(PlayerIdPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(playerId)) return playerId;
            playerId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(PlayerIdPrefsKey, playerId);
            PlayerPrefs.Save();
            return playerId;
        }
    }
}
