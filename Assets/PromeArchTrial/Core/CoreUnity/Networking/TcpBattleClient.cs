using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PromeArchTrial.Core.Networking;

namespace PromeArchTrial.Core.Unity.Networking
{
    /// <summary>
    /// 保存后台网络线程测得的单次 Ping/Pong 协议往返时间。
    /// </summary>
    public readonly struct PingRoundTripSample
    {
        /// <summary>创建一条已经完成的协议往返延迟样本。</summary>
        public PingRoundTripSample(int sequence, double milliseconds)
        {
            Sequence = sequence;
            Milliseconds = milliseconds;
        }

        /// <summary>获取客户端 Ping 序号。</summary>
        public int Sequence { get; }

        /// <summary>获取不包含 Unity 主线程帧等待的协议往返毫秒数。</summary>
        public double Milliseconds { get; }
    }

    /// <summary>
    /// 描述 Unity 客户端网络连接的只读生命周期状态。
    /// </summary>
    public enum BattleClientConnectionState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,
        Rejected = 3,
        Faulted = 4,
        Disposed = 5
    }

    /// <summary>
    /// 在后台任务处理 TCP 收发，并通过线程安全队列把服务器消息交给 Unity 主线程。
    /// </summary>
    public sealed class TcpBattleClient : IDisposable
    {
        private readonly ConcurrentQueue<byte[]> outboundPayloads = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<ServerWelcomeMessage> welcomeMessages = new ConcurrentQueue<ServerWelcomeMessage>();
        private readonly ConcurrentQueue<ServerSnapshotMessage> snapshotMessages = new ConcurrentQueue<ServerSnapshotMessage>();
        private readonly ConcurrentQueue<ServerRejectMessage> rejectMessages = new ConcurrentQueue<ServerRejectMessage>();
        private readonly ConcurrentQueue<PingRoundTripSample> pingSamples = new ConcurrentQueue<PingRoundTripSample>();
        private readonly ConcurrentDictionary<int, long> pendingPingTimestamps = new ConcurrentDictionary<int, long>();
        private readonly SemaphoreSlim outboundSignal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private TcpClient tcpClient;
        private NetworkStream networkStream;
        private Task sendTask;
        private Task receiveTask;
        private int connectionState;
        private int disposed;
        private string lastError;

        /// <summary>获取当前连接生命周期状态。</summary>
        public BattleClientConnectionState ConnectionState => (BattleClientConnectionState)Volatile.Read(ref connectionState);

        /// <summary>获取后台网络任务记录的最后一个错误文本。</summary>
        public string LastError => lastError;

        /// <summary>连接服务器、发送握手消息，并启动顺序发送和接收任务。</summary>
        public async Task ConnectAsync(string host, int port, ClientHelloMessage hello)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Battle server host cannot be empty.", nameof(host));
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            if (Interlocked.CompareExchange(ref connectionState, (int)BattleClientConnectionState.Connecting, (int)BattleClientConnectionState.Disconnected) != (int)BattleClientConnectionState.Disconnected) throw new InvalidOperationException("Battle client can only connect once.");
            ThrowIfDisposed();
            try
            {
                tcpClient = new TcpClient { NoDelay = true };
                await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
                networkStream = tcpClient.GetStream();
                await BattleProtocolCodec.WriteFrameAsync(networkStream, BattleProtocolCodec.Encode(hello), lifetimeCancellation.Token).ConfigureAwait(false);
                Volatile.Write(ref connectionState, (int)BattleClientConnectionState.Connected);
                sendTask = Task.Run(() => SendLoopAsync(lifetimeCancellation.Token));
                receiveTask = Task.Run(() => ReceiveLoopAsync(lifetimeCancellation.Token));
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref disposed) == 0)
                {
                    lastError = exception.Message;
                    Volatile.Write(ref connectionState, (int)BattleClientConnectionState.Faulted);
                }
                CloseSocket();
                throw;
            }
        }

        /// <summary>把一条客户端输入加入后台顺序发送队列。</summary>
        public bool TrySend(ClientInputMessage message)
        {
            if (ConnectionState != BattleClientConnectionState.Connected || Volatile.Read(ref disposed) != 0) return false;
            outboundPayloads.Enqueue(BattleProtocolCodec.Encode(message));
            outboundSignal.Release();
            return true;
        }

        /// <summary>把一条独立延迟探测消息加入后台顺序发送队列。</summary>
        public bool TrySend(ClientPingMessage message)
        {
            if (ConnectionState != BattleClientConnectionState.Connected || Volatile.Read(ref disposed) != 0) return false;
            pendingPingTimestamps[message.Sequence] = Stopwatch.GetTimestamp();
            outboundPayloads.Enqueue(BattleProtocolCodec.Encode(message));
            outboundSignal.Release();
            return true;
        }

        /// <summary>尝试在 Unity 主线程获取一条服务器握手确认。</summary>
        public bool TryDequeueWelcome(out ServerWelcomeMessage message)
        {
            return welcomeMessages.TryDequeue(out message);
        }

        /// <summary>尝试在 Unity 主线程获取一条服务器权威快照。</summary>
        public bool TryDequeueSnapshot(out ServerSnapshotMessage message)
        {
            return snapshotMessages.TryDequeue(out message);
        }

        /// <summary>尝试在 Unity 主线程获取一条服务器拒绝消息。</summary>
        public bool TryDequeueReject(out ServerRejectMessage message)
        {
            return rejectMessages.TryDequeue(out message);
        }

        /// <summary>尝试在 Unity 主线程获取一条已由后台接收线程完成计时的 Ping 样本。</summary>
        public bool TryDequeuePingSample(out PingRoundTripSample sample)
        {
            return pingSamples.TryDequeue(out sample);
        }

        /// <summary>取消全部后台任务并立即关闭 TCP 连接；重复调用不会重复释放。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Volatile.Write(ref connectionState, (int)BattleClientConnectionState.Disposed);
            lifetimeCancellation.Cancel();
            CloseSocket();
            try
            {
                outboundSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // 信号已可用时无需再次释放，后台任务会观察取消状态退出。
            }
        }

        /// <summary>按入队顺序发送客户端输入，保证 TCP 写操作不会并发交错。</summary>
        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await outboundSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
                    while (outboundPayloads.TryDequeue(out byte[] payload)) await BattleProtocolCodec.WriteFrameAsync(networkStream, payload, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 主线程释放客户端后通过取消令牌结束后台发送循环。
            }
            catch (Exception exception)
            {
                RecordBackgroundFailure(exception);
            }
        }

        /// <summary>持续读取服务器消息并只写入线程安全队列，不访问任何 Unity API。</summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[] payload = await BattleProtocolCodec.ReadFrameAsync(networkStream, cancellationToken).ConfigureAwait(false);
                    if (payload == null)
                    {
                        lastError = "Battle server closed the connection.";
                        Volatile.Write(ref connectionState, (int)BattleClientConnectionState.Disconnected);
                        return;
                    }
                    DispatchServerPayload(payload);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 主线程释放客户端后通过取消令牌结束后台接收循环。
            }
            catch (Exception exception)
            {
                RecordBackgroundFailure(exception);
            }
        }

        /// <summary>解码服务器负载并投递到对应的主线程消息队列。</summary>
        private void DispatchServerPayload(byte[] payload)
        {
            DecodedBattleMessage message = BattleProtocolCodec.DecodeFrame(payload);
            switch (message.MessageType)
            {
                case BattleMessageType.ServerWelcome:
                    welcomeMessages.Enqueue(message.GetMessage<ServerWelcomeMessage>());
                    return;
                case BattleMessageType.ServerSnapshot:
                    snapshotMessages.Enqueue(message.GetMessage<ServerSnapshotMessage>());
                    return;
                case BattleMessageType.ServerReject:
                    rejectMessages.Enqueue(message.GetMessage<ServerRejectMessage>());
                    Volatile.Write(ref connectionState, (int)BattleClientConnectionState.Rejected);
                    return;
                case BattleMessageType.ServerPong:
                    RecordPong(message.GetMessage<ServerPongMessage>());
                    return;
                default:
                    throw new InvalidOperationException("Battle client received a message type that is not valid from the server.");
            }
        }

        /// <summary>在 Pong 抵达后台接收线程时立即完成 RTT 计时，排除等待 Unity 下一帧 Update 的时间。</summary>
        private void RecordPong(ServerPongMessage pong)
        {
            if (!pendingPingTimestamps.TryRemove(pong.Sequence, out long sentTimestamp)) return;
            long elapsedTicks = Stopwatch.GetTimestamp() - sentTimestamp;
            double elapsedMilliseconds = Math.Max(0.0d, elapsedTicks * 1000.0d / Stopwatch.Frequency);
            pingSamples.Enqueue(new PingRoundTripSample(pong.Sequence, elapsedMilliseconds));
        }

        /// <summary>记录后台异常并关闭连接，使 Unity 主线程可以显示明确状态。</summary>
        private void RecordBackgroundFailure(Exception exception)
        {
            if (Volatile.Read(ref disposed) != 0) return;
            lastError = exception.Message;
            Volatile.Write(ref connectionState, (int)BattleClientConnectionState.Faulted);
            lifetimeCancellation.Cancel();
            CloseSocket();
        }

        /// <summary>安全关闭网络流和套接字，允许多个生命周期出口重复调用。</summary>
        private void CloseSocket()
        {
            try
            {
                networkStream?.Close();
            }
            catch (ObjectDisposedException)
            {
                // 另一个生命周期出口已经关闭网络流时无需继续处理。
            }
            try
            {
                tcpClient?.Close();
            }
            catch (ObjectDisposedException)
            {
                // 另一个生命周期出口已经关闭套接字时无需继续处理。
            }
        }

        /// <summary>防止已经释放的网络客户端重新建立连接。</summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(TcpBattleClient));
        }
    }
}
