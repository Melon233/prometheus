using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Framing;
using Xuan.Prometheus.NetworkKit.Protocol;
using Xuan.Prometheus.NetworkKit.Rpc;
using Xuan.Prometheus.NetworkKit.Transport;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit.Session
{
    /// <summary>管理一次连接的生命周期、请求关联和服务器主动推送。</summary>
    internal sealed class NetworkSession : IDisposable
    {
        private readonly IByteTransport transport;
        private readonly SemaphoreSlim connectLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<Packet>> pending = new ConcurrentDictionary<ulong, TaskCompletionSource<Packet>>();
        private readonly ConcurrentQueue<Packet> pushQueue = new ConcurrentQueue<Packet>();
        private CancellationTokenSource lifetime;
        private Task receiveLoop;
        private long nextRequestId;
        /// <summary>记录后台接收循环是否检测到意外断线，由 PumpEvents 原子取走后在调用线程通知上层。</summary>
        private int disconnectedPending;
        /// <summary>记录会话对象是否已经永久释放；主动断连不会设置该状态，因此仍可重连。</summary>
        private bool isDisposed;

        /// <summary>使用指定字节传输实现创建会话。</summary>
        public NetworkSession(IByteTransport transport) { this.transport = transport; }

        /// <summary>当前传输连接是否已建立。</summary>
        public bool IsConnected => transport.IsConnected;

        /// <summary>后台接收消息在主线程 Pump 时触发，避免网络线程直接触碰 Unity 对象。</summary>
        public event Action<Packet> PushReceived;

        /// <summary>后台接收异常在主线程 Pump 时触发，避免网络线程直接修改上层系统状态。</summary>
        public event Action Disconnected;

        /// <summary>建立连接并启动单一接收循环。</summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (IsConnected) return;
            await connectLock.WaitAsync(cancellationToken);
            try
            {
                if (IsConnected) return;
                await StopConnectionAsync();
                await ConnectCoreAsync(host, port, cancellationToken);
            }
            finally { connectLock.Release(); }
        }

        /// <summary>主动关闭当前连接和接收循环，同时保留会话对象用于后续重连。</summary>
        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await connectLock.WaitAsync(cancellationToken);
            try { await StopConnectionAsync(); }
            finally { connectLock.Release(); }
        }

        /// <summary>在同一个会话对象上先关闭旧连接，再连接指定端点并启动新的接收循环。</summary>
        public async Task ReconnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await connectLock.WaitAsync(cancellationToken);
            try
            {
                await StopConnectionAsync();
                await ConnectCoreAsync(host, port, cancellationToken);
            }
            finally { connectLock.Release(); }
        }

        /// <summary>发送带 request_id 的请求并等待匹配响应，允许多个请求并行在同一连接上运行。</summary>
        public async Task<Packet> RequestAsync(Packet request, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (!IsConnected) throw new NetworkError(NetworkErrorKind.Disconnected, "网络会话尚未连接");
            ulong requestId = unchecked((ulong)Interlocked.Increment(ref nextRequestId));
            request.RequestId = requestId;
            TaskCompletionSource<Packet> completion = new TaskCompletionSource<Packet>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(requestId, completion)) throw new NetworkError(NetworkErrorKind.Transport, "无法注册网络请求");
            try
            {
                await SendPacketAsync(request, cancellationToken);
                using (cancellationToken.Register(() => completion.TrySetCanceled())) return await completion.Task;
            }
            catch
            {
                pending.TryRemove(requestId, out _);
                throw;
            }
        }

        /// <summary>在 Unity 主线程中分发接收队列中的服务器推送。</summary>
        public void PumpEvents()
        {
            ThrowIfDisposed();
            if (Interlocked.Exchange(ref disconnectedPending, 0) != 0) Disconnected?.Invoke();
            while (pushQueue.TryDequeue(out Packet packet)) PushReceived?.Invoke(packet);
        }

        /// <summary>关闭会话并让所有等待请求结束。</summary>
        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            StopConnection();
            PushReceived = null;
            Disconnected = null;
            connectLock.Dispose();
            sendLock.Dispose();
            transport.Dispose();
        }

        /// <summary>在已持有连接锁时建立传输连接并启动唯一接收循环。</summary>
        private async Task ConnectCoreAsync(string host, int port, CancellationToken cancellationToken)
        {
            lifetime = new CancellationTokenSource();
            Interlocked.Exchange(ref disconnectedPending, 0);
            try
            {
                using (CancellationTokenSource connectionAttempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token)) await transport.ConnectAsync(host, port, connectionAttempt.Token);
                receiveLoop = ReceiveLoopAsync(lifetime.Token);
            }
            catch
            {
                _ = StopConnection();
                throw;
            }
        }

        /// <summary>停止当前连接并等待旧接收循环退出，确保重连前不会残留并行读取。</summary>
        private async Task StopConnectionAsync()
        {
            Task activeReceiveLoop = StopConnection();
            if (activeReceiveLoop != null) await activeReceiveLoop;
        }

        /// <summary>同步取消当前连接、清理请求和 Push 队列，并返回需要异步等待的旧接收循环。</summary>
        private Task StopConnection()
        {
            CancellationTokenSource activeLifetime = lifetime;
            Task activeReceiveLoop = receiveLoop;
            lifetime = null;
            receiveLoop = null;
            activeLifetime?.Cancel();
            transport.Close();
            FailPendingRequests(new NetworkError(NetworkErrorKind.Disconnected, "网络会话已关闭"));
            while (pushQueue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref disconnectedPending, 0);
            activeLifetime?.Dispose();
            return activeReceiveLoop;
        }

        /// <summary>用同一个断连错误结束全部等待请求并清空请求关联表。</summary>
        private void FailPendingRequests(Exception exception)
        {
            foreach (TaskCompletionSource<Packet> completion in pending.Values) completion.TrySetException(exception);
            pending.Clear();
        }

        /// <summary>阻止永久释放后的会话继续连接、请求或分发 Push。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(NetworkSession));
        }

        /// <summary>串行写出 Head + Body 传输 Packet，保证同一连接上的多个请求不会交错写入。</summary>
        private async Task SendPacketAsync(Packet packet, CancellationToken cancellationToken)
        {
            byte[] frame = PacketCodec.Encode(packet);
            await sendLock.WaitAsync(cancellationToken);
            try { await transport.SendAsync(frame, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                transport.Close();
                NetworkError disconnectedError = new NetworkError(NetworkErrorKind.Disconnected, "网络发送失败，当前连接已关闭", exception);
                FailPendingRequests(disconnectedError);
                Interlocked.Exchange(ref disconnectedPending, 1);
                throw disconnectedError;
            }
            finally { sendLock.Release(); }
        }

        /// <summary>持续读取定长 Head 与变长 Body，并按 request_id 分发响应或主动推送。</summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TransportPacket transportPacket = await TransportPacketCodec.ReadAsync(transport, cancellationToken);
                    Packet packet = PacketCodec.Decode(transportPacket);
                    if (packet.RequestId != 0 && pending.TryRemove(packet.RequestId, out TaskCompletionSource<Packet> completion)) completion.TrySetResult(packet);
                    else pushQueue.Enqueue(packet);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                transport.Close();
                FailPendingRequests(new NetworkError(NetworkErrorKind.Disconnected, "网络接收循环已结束", exception));
                Interlocked.Exchange(ref disconnectedPending, 1);
            }
        }
    }
}
