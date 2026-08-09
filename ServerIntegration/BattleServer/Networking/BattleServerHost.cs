using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PromeArchTrial.Core.Networking;
using PromeArchTrial.Game.Character;
using PromeArchTrial.Game.Networking;
using PromeArchTrial.Game.World;

namespace PromeArchTrial.BattleServer.Networking
{
    /// <summary>
    /// 仅监听 IPv4 回环地址，由唯一宿主循环以 30 Hz 推进一个全局 AuthoritativeBattleWorld，并把同一 Tick 结果分发给全部会话。
    /// </summary>
    public sealed class BattleServerHost : IDisposable
    {
        private const int MaximumClientCount = 32;
        private readonly int requestedPort;
        private readonly int characterId;
        private readonly CharacterRuntimeConfig characterConfig;
        private readonly AuthoritativeBattleWorld world;
        private readonly object worldGate = new object();
        private readonly ConcurrentDictionary<int, BattleClientSession> sessions = new ConcurrentDictionary<int, BattleClientSession>();
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private TcpListener listener;
        private int nextPlayerId;
        private int nextEntityId;
        private int boundPort;
        private int started;
        private int disposed;

        /// <summary>使用指定端口、Character 主键和不可变 Luban 编译配置创建全局权威世界宿主。</summary>
        public BattleServerHost(int port, int characterId, CharacterRuntimeConfig characterConfig)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (characterId <= 0) throw new ArgumentOutOfRangeException(nameof(characterId), "Character ID must be positive.");
            this.characterConfig = characterConfig ?? throw new ArgumentNullException(nameof(characterConfig));
            if (characterConfig.TickRate != 30) throw new ArgumentException("The authoritative battle server requires an exact 30 Hz character configuration.", nameof(characterConfig));
            if (characterConfig.PredictionHistoryTicks < BattlePredictionPolicy.ClientInputLeadTicks) throw new ArgumentException($"Prediction history {characterConfig.PredictionHistoryTicks} must be at least the configured input lead {BattlePredictionPolicy.ClientInputLeadTicks}.", nameof(characterConfig));
            requestedPort = port;
            this.characterId = characterId;
            world = new AuthoritativeBattleWorld(characterConfig);
        }

        /// <summary>获取系统实际绑定的监听端口；传入零端口时可用于自动验收发现随机端口。</summary>
        public int BoundPort => Volatile.Read(ref boundPort);

        /// <summary>获取最近完成的全局权威世界 Tick，服务器刚启动时为负一。</summary>
        public int ServerTick
        {
            get
            {
                lock (worldGate) return world.WorldTick;
            }
        }

        /// <summary>获取当前已经通过握手并注册到全局世界的角色数量。</summary>
        public int WorldEntityCount
        {
            get
            {
                lock (worldGate) return world.EntityCount;
            }
        }

        /// <summary>启动连接接受循环和唯一 30 Hz Tick 循环，直到外部取消、宿主释放或不可恢复异常发生。</summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("BattleServerHost can only be run once.");
            using (CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token))
            {
                CancellationToken lifetimeToken = linkedCancellation.Token;
                listener = new TcpListener(IPAddress.Loopback, requestedPort);
                listener.Start();
                Volatile.Write(ref boundPort, ((IPEndPoint)listener.LocalEndpoint).Port);
                Console.WriteLine($"Battle server listening on 127.0.0.1:{BoundPort} with one {characterConfig.TickRate} Hz authoritative world.");
                Task acceptTask = AcceptLoopAsync(lifetimeToken);
                try
                {
                    using (PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0d / characterConfig.TickRate)))
                    {
                        while (await timer.WaitForNextTickAsync(lifetimeToken).ConfigureAwait(false)) TickWorldOnce();
                    }
                }
                catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
                {
                    // Ctrl+C、SmokeTest 完成或宿主释放都会通过统一取消令牌正常结束服务器循环。
                }
                finally
                {
                    StopListener();
                    DisposeSessions();
                    try
                    {
                        await acceptTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
                    {
                        // 接受循环观察同一个生命周期令牌后正常退出。
                    }
                    catch (ObjectDisposedException) when (lifetimeToken.IsCancellationRequested || Volatile.Read(ref disposed) != 0)
                    {
                        // 关闭监听器会中断尚未完成的 AcceptTcpClientAsync。
                    }
                    catch (SocketException) when (lifetimeToken.IsCancellationRequested || Volatile.Read(ref disposed) != 0)
                    {
                        // Windows 在监听器关闭时可能以 SocketException 结束接受操作。
                    }
                }
            }
            Console.WriteLine("Battle server stopped.");
        }

        /// <summary>取消宿主生命周期、关闭监听器并释放当前全部会话；重复调用不会重复执行。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            lifetimeCancellation.Cancel();
            StopListener();
            DisposeSessions();
            lifetimeCancellation.Dispose();
        }

        /// <summary>在全局世界的同一临界区内动态加入角色，并返回与当前世界 Tick 完全一致的 Welcome 初始状态。</summary>
        internal SessionRegistration RegisterPlayer(int playerId, int entityId)
        {
            ThrowIfDisposed();
            lock (worldGate)
            {
                FixedVector3 spawnPosition = CreateSpawnPosition(playerId);
                world.AddEntity(entityId, playerId, characterConfig, spawnPosition);
                CharacterState initialState = world.GetState(entityId);
                return new SessionRegistration(playerId, entityId, characterId, world.WorldTick, characterConfig.TickRate, characterConfig.ContentHash, initialState);
            }
        }

        /// <summary>把经过协议边界校验的角色命令提交到全局世界，并保留接受、迟到、重传与过远未来的精确结果。</summary>
        internal AuthoritativeCommandSubmissionResult SubmitCommand(int entityId, CharacterCommand command)
        {
            lock (worldGate) return world.SubmitCommand(entityId, command);
        }

        /// <summary>从全局世界移除断开连接玩家的角色及全部待处理命令。</summary>
        internal void RemovePlayerEntity(int entityId)
        {
            lock (worldGate) world.RemoveEntity(entityId);
        }

        /// <summary>持续接受客户端，并把握手、输入接收和顺序发送交给独立会话任务。</summary>
        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient tcpClient = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                tcpClient.NoDelay = true;
                if (sessions.Count >= MaximumClientCount)
                {
                    await RejectAndCloseAsync(tcpClient, ServerRejectReason.ServerFull, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                int playerId = Interlocked.Increment(ref nextPlayerId);
                int entityId = Interlocked.Increment(ref nextEntityId);
                BattleClientSession session = new BattleClientSession(playerId, entityId, tcpClient, this, characterConfig.ContentHash);
                if (!sessions.TryAdd(playerId, session))
                {
                    session.Dispose();
                    continue;
                }
                _ = ObserveSessionAsync(session, cancellationToken);
            }
        }

        /// <summary>等待会话结束、记录异常、移除身份索引，并确保断线角色不会留在权威世界。</summary>
        private async Task ObserveSessionAsync(BattleClientSession session, CancellationToken cancellationToken)
        {
            try
            {
                await session.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || Volatile.Read(ref disposed) != 0)
            {
                // 服务器整体关闭时客户端会话同步退出。
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Player {session.PlayerId} session failed: {exception.Message}");
            }
            finally
            {
                sessions.TryRemove(session.PlayerId, out _);
                RemovePlayerEntity(session.EntityId);
                session.Dispose();
                Console.WriteLine($"Player {session.PlayerId}, entity {session.EntityId} disconnected.");
            }
        }

        /// <summary>推进全局世界一次，并把同一个不可变 Tick 结果分发给当前会话；任何单会话发送失败都不会中断世界。</summary>
        private void TickWorldOnce()
        {
            WorldTickResult tickResult;
            lock (worldGate) tickResult = world.Tick();
            foreach (BattleClientSession session in sessions.Values)
            {
                try
                {
                    session.PublishWorldTick(tickResult);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine($"Player {session.PlayerId} snapshot publication failed: {exception.Message}");
                    session.Dispose();
                }
            }
        }

        /// <summary>为本地演示玩家分配间隔三世界单位的稳定网格出生点，避免连接时完全重叠。</summary>
        private static FixedVector3 CreateSpawnPosition(int playerId)
        {
            int zeroBasedSlot = playerId - 1;
            decimal xUnits = zeroBasedSlot % 8 * 3m;
            decimal zUnits = zeroBasedSlot / 8 * 3m;
            return new FixedVector3(CharacterFixedPoint.FromUnits(xUnits), 0L, CharacterFixedPoint.FromUnits(zUnits));
        }

        /// <summary>向超过容量限制的客户端发送拒绝原因并关闭连接。</summary>
        private static async Task RejectAndCloseAsync(TcpClient tcpClient, ServerRejectReason reason, CancellationToken cancellationToken)
        {
            using (tcpClient)
            using (NetworkStream stream = tcpClient.GetStream()) await BattleProtocolCodec.WriteFrameAsync(stream, BattleProtocolCodec.Encode(new ServerRejectMessage(reason)), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>停止监听器，使等待中的接受操作立即结束。</summary>
        private void StopListener()
        {
            try
            {
                listener?.Stop();
            }
            catch (SocketException)
            {
                // 监听器已因其他生命周期出口关闭时无需重复报告。
            }
        }

        /// <summary>释放当前快照中的全部会话并从权威世界移除对应角色。</summary>
        private void DisposeSessions()
        {
            foreach (BattleClientSession session in sessions.Values)
            {
                session.Dispose();
                RemovePlayerEntity(session.EntityId);
            }
            sessions.Clear();
        }

        /// <summary>防止已经释放的宿主接受注册或再次运行。</summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(BattleServerHost));
        }
    }

    /// <summary>
    /// 保存动态注册完成时同一个世界临界区内冻结的身份、配置和完整初始状态。
    /// </summary>
    internal readonly struct SessionRegistration
    {
        /// <summary>创建一份可直接编码为 v5 ServerWelcome 的动态注册结果。</summary>
        public SessionRegistration(int playerId, int entityId, int characterId, int serverTick, int tickRate, ulong configHash, CharacterState initialState)
        {
            PlayerId = playerId;
            EntityId = entityId;
            CharacterId = characterId;
            ServerTick = serverTick;
            TickRate = tickRate;
            ConfigHash = configHash;
            InitialState = initialState;
        }

        public int PlayerId { get; }
        public int EntityId { get; }
        public int CharacterId { get; }
        public int ServerTick { get; }
        public int TickRate { get; }
        public ulong ConfigHash { get; }
        public CharacterState InitialState { get; }
    }
}
