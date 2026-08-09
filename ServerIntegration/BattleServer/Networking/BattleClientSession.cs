using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    /// 管理单个客户端的 v5 握手、世界命令提交、Ping/Pong 优先发送和自身完整权威状态快照；角色状态只由宿主的全局世界持有。
    /// </summary>
    public sealed class BattleClientSession : IDisposable
    {
        private const int MaximumQueuedControlPayloadCount = 128;
        private const int MaximumControlPayloadsPerSendTurn = 16;
        private readonly TcpClient tcpClient;
        private readonly NetworkStream networkStream;
        private readonly BattleServerHost host;
        private readonly ulong expectedConfigHash;
        private readonly ConcurrentQueue<byte[]> controlPayloads = new ConcurrentQueue<byte[]>();
        private readonly ReliableSnapshotOutbox snapshotOutbox = new ReliableSnapshotOutbox();
        private readonly SemaphoreSlim outboundSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private Task sendTask;
        private int ignoredLateOrDuplicateInputCount;
        private int outboundSignalPending;
        private int ready;
        private int disposed;

        /// <summary>使用已接受 TCP 连接、稳定身份和全局世界宿主创建客户端会话。</summary>
        public BattleClientSession(int playerId, int entityId, TcpClient tcpClient, BattleServerHost host, ulong expectedConfigHash)
        {
            PlayerId = playerId > 0 ? playerId : throw new ArgumentOutOfRangeException(nameof(playerId));
            EntityId = entityId > 0 ? entityId : throw new ArgumentOutOfRangeException(nameof(entityId));
            this.tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.expectedConfigHash = expectedConfigHash;
            networkStream = tcpClient.GetStream();
        }

        /// <summary>获取服务器为连接分配的稳定玩家编号。</summary>
        public int PlayerId { get; }

        /// <summary>获取服务器为该玩家在全局世界分配的稳定角色实体编号。</summary>
        public int EntityId { get; }

        /// <summary>完成严格握手后并行运行唯一发送循环与消息接收循环，直到任一方向结束。</summary>
        public async Task RunAsync(CancellationToken serverCancellationToken)
        {
            using (CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken, lifetimeCancellation.Token))
            {
                CancellationToken cancellationToken = linkedCancellation.Token;
                byte[] helloPayload = await BattleProtocolCodec.ReadFrameAsync(networkStream, cancellationToken).ConfigureAwait(false);
                if (helloPayload == null) return;
                DecodedBattleMessage helloFrame = BattleProtocolCodec.DecodeFrame(helloPayload);
                if (helloFrame.MessageType != BattleMessageType.ClientHello)
                {
                    await SendRejectDirectAsync(ServerRejectReason.InvalidMessage, cancellationToken).ConfigureAwait(false);
                    return;
                }
                ClientHelloMessage hello = helloFrame.GetMessage<ClientHelloMessage>();
                ServerRejectReason rejection = ValidateHello(hello);
                if (rejection != ServerRejectReason.Unknown)
                {
                    await SendRejectDirectAsync(rejection, cancellationToken).ConfigureAwait(false);
                    return;
                }
                SessionRegistration registration = host.RegisterPlayer(PlayerId, EntityId);
                ServerWelcomeMessage welcome = new ServerWelcomeMessage(registration.PlayerId, registration.EntityId, registration.CharacterId, registration.ServerTick, registration.TickRate, registration.ConfigHash, CharacterNetworkMapper.ToNetworkState(registration.InitialState));
                await BattleProtocolCodec.WriteFrameAsync(networkStream, BattleProtocolCodec.Encode(welcome), cancellationToken).ConfigureAwait(false);
                sendTask = Task.Run(() => SendLoopAsync(cancellationToken));
                Volatile.Write(ref ready, 1);
                Console.WriteLine($"Player {PlayerId}, entity {EntityId} connected from {tcpClient.Client.RemoteEndPoint} at world tick {registration.ServerTick}.");
                Task receiveTask = ReceiveMessagesAsync(cancellationToken);
                try
                {
                    Task firstCompleted = await Task.WhenAny(receiveTask, sendTask).ConfigureAwait(false);
                    if (ReferenceEquals(firstCompleted, sendTask)) await sendTask.ConfigureAwait(false);
                    else await receiveTask.ConfigureAwait(false);
                }
                finally
                {
                    Volatile.Write(ref ready, 0);
                    lifetimeCancellation.Cancel();
                    SignalOutbound();
                    try
                    {
                        await Task.WhenAll(receiveTask, sendTask).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || lifetimeCancellation.IsCancellationRequested)
                    {
                        // 任一收发方向结束后统一取消另一方向属于正常会话关闭流程。
                    }
                    catch (IOException) when (Volatile.Read(ref disposed) != 0)
                    {
                        // Dispose 关闭流时未完成的读写可能以 IOException 结束。
                    }
                    catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
                    {
                        // Dispose 关闭流或信号量时未完成任务会观察到对象已释放。
                    }
                }
            }
        }

        /// <summary>从同一全局 Tick 结果筛选与本角色有关的事件，覆盖旧状态但可靠累积尚未成功写入的事件。</summary>
        public void PublishWorldTick(WorldTickResult tickResult)
        {
            if (tickResult == null) throw new ArgumentNullException(nameof(tickResult));
            if (Volatile.Read(ref ready) == 0 || Volatile.Read(ref disposed) != 0) return;
            if (!tickResult.Snapshot.TryGetEntity(EntityId, out WorldEntitySnapshot entitySnapshot)) return;
            List<BattleEventMessage> relevantEvents = new List<BattleEventMessage>();
            for (int index = 0; index < tickResult.Events.Count; index++)
            {
                WorldEvent worldEvent = tickResult.Events[index];
                if (worldEvent.SourceEntityId == EntityId || worldEvent.TargetEntityId == EntityId) relevantEvents.Add(CharacterNetworkMapper.ToBattleEventMessage(worldEvent));
            }
            ServerSnapshotMessage snapshot = new ServerSnapshotMessage(tickResult.Snapshot.WorldTick, entitySnapshot.LastProcessedCommandTick, CharacterNetworkMapper.ToNetworkState(entitySnapshot.State), relevantEvents);
            snapshotOutbox.Publish(snapshot);
            SignalOutbound();
        }

        /// <summary>取消收发任务并关闭套接字；重复调用不会重复释放。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Volatile.Write(ref ready, 0);
            lifetimeCancellation.Cancel();
            SignalOutbound();
            try
            {
                networkStream.Close();
            }
            catch (ObjectDisposedException)
            {
                // 网络流已由另一个生命周期出口关闭。
            }
            tcpClient.Close();
        }

        /// <summary>校验客户端协议版本与同一个 Character 运行时配置哈希，拒绝任何确定性输入解释不一致的连接。</summary>
        private ServerRejectReason ValidateHello(ClientHelloMessage hello)
        {
            if (hello.ProtocolVersion != BattleProtocol.Version) return ServerRejectReason.ProtocolMismatch;
            if (hello.ConfigHash != expectedConfigHash) return ServerRejectReason.ConfigMismatch;
            return ServerRejectReason.Unknown;
        }

        /// <summary>持续接收输入并直接加入全局世界队列；Ping 进入高优先级控制队列，不等待下一次 30 Hz Tick。</summary>
        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] payload = await BattleProtocolCodec.ReadFrameAsync(networkStream, cancellationToken).ConfigureAwait(false);
                if (payload == null) return;
                DecodedBattleMessage message = BattleProtocolCodec.DecodeFrame(payload);
                if (message.MessageType == BattleMessageType.ClientInput)
                {
                    ClientInputMessage input = message.GetMessage<ClientInputMessage>();
                    if (input.PredictedState.Tick != input.ClientTick) throw new InvalidDataException($"Client predicted-state tick {input.PredictedState.Tick} does not match input tick {input.ClientTick}.");
                    CharacterCommand command = CharacterNetworkMapper.ToCharacterCommand(input);
                    AuthoritativeCommandSubmissionResult submissionResult = host.SubmitCommand(EntityId, command);
                    if (submissionResult == AuthoritativeCommandSubmissionResult.Accepted)
                    {
                        continue;
                    }
                    if (submissionResult == AuthoritativeCommandSubmissionResult.Late || submissionResult == AuthoritativeCommandSubmissionResult.Duplicate)
                    {
                        RecordIgnoredInput(submissionResult, command.Tick);
                        continue;
                    }
                    if (submissionResult == AuthoritativeCommandSubmissionResult.EntityNotFound) return;
                    if (submissionResult == AuthoritativeCommandSubmissionResult.TooFarInFuture) throw new InvalidDataException($"Client input tick {command.Tick} exceeds the authoritative world's bounded future window.");
                    throw new InvalidDataException($"Unsupported authoritative command submission result {submissionResult}.");
                }
                else if (message.MessageType == BattleMessageType.ClientPing)
                {
                    ClientPingMessage ping = message.GetMessage<ClientPingMessage>();
                    QueueControlPayload(BattleProtocolCodec.Encode(new ServerPongMessage(ping.Sequence)));
                }
                else
                {
                    throw new InvalidDataException($"Unexpected post-handshake battle message {message.MessageType}.");
                }
            }
        }

        /// <summary>作为会话唯一网络写入者限量优先发送 Pong，再发送最新状态与可靠事件批次，从而同时避免 TCP 帧交叉、快照饥饿和无界堆积。</summary>
        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await outboundSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref outboundSignalPending, 0);
                int sentControlPayloadCount = 0;
                while (sentControlPayloadCount < MaximumControlPayloadsPerSendTurn && controlPayloads.TryDequeue(out byte[] controlPayload))
                {
                    await BattleProtocolCodec.WriteFrameAsync(networkStream, controlPayload, cancellationToken).ConfigureAwait(false);
                    sentControlPayloadCount++;
                }
                if (snapshotOutbox.TryReserve(out ReliableSnapshotOutbox.SnapshotOutboxReservation snapshotReservation))
                {
                    await BattleProtocolCodec.WriteFrameAsync(networkStream, snapshotReservation.Payload, cancellationToken).ConfigureAwait(false);
                    snapshotOutbox.Commit(snapshotReservation);
                }
                if (!controlPayloads.IsEmpty || snapshotOutbox.HasPending) SignalOutbound();
            }
        }

        /// <summary>以首次和每六十四次的限速频率记录幂等忽略的迟到或重传命令，既保留网络诊断又不允许日志洪泛。</summary>
        private void RecordIgnoredInput(AuthoritativeCommandSubmissionResult submissionResult, int commandTick)
        {
            int ignoredCount = Interlocked.Increment(ref ignoredLateOrDuplicateInputCount);
            if (ignoredCount != 1 && ignoredCount % 64 != 0) return;
            Console.WriteLine($"Player {PlayerId}, entity {EntityId} ignored idempotent {submissionResult} input tick {commandTick}; ignored count={ignoredCount}.");
        }

        /// <summary>把需要低延迟回复的控制负载加入有界队列，防止 Ping 洪泛产生无界内存增长。</summary>
        private void QueueControlPayload(byte[] payload)
        {
            if (controlPayloads.Count >= MaximumQueuedControlPayloadCount) throw new InvalidDataException("Outbound control-message queue exceeded its safety limit.");
            controlPayloads.Enqueue(payload);
            SignalOutbound();
        }

        /// <summary>在发送循环可能等待时发出唤醒信号，并容忍并发关闭造成的释放竞态。</summary>
        private void SignalOutbound()
        {
            if (Interlocked.Exchange(ref outboundSignalPending, 1) != 0) return;
            try
            {
                outboundSignal.Release();
            }
            catch (ObjectDisposedException)
            {
                // 会话已经完成释放时无需再次唤醒发送循环。
            }
        }

        /// <summary>在发送循环启动前直接发送握手拒绝，此时不存在并发网络写入者。</summary>
        private Task SendRejectDirectAsync(ServerRejectReason reason, CancellationToken cancellationToken)
        {
            return BattleProtocolCodec.WriteFrameAsync(networkStream, BattleProtocolCodec.Encode(new ServerRejectMessage(reason)), cancellationToken);
        }
    }
}
