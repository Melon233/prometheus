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
    public sealed class NetworkSession : IDisposable
    {
        private readonly IByteTransport transport;
        private readonly SemaphoreSlim connectLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<ulong, TaskCompletionSource<Packet>> pending = new ConcurrentDictionary<ulong, TaskCompletionSource<Packet>>();
        private readonly ConcurrentQueue<Packet> pushQueue = new ConcurrentQueue<Packet>();
        private CancellationTokenSource lifetime;
        private Task receiveLoop;
        private long nextRequestId;

        /// <summary>使用指定字节传输实现创建会话。</summary>
        public NetworkSession(IByteTransport transport) { this.transport = transport; }

        /// <summary>当前传输连接是否已建立。</summary>
        public bool IsConnected => transport.IsConnected;

        /// <summary>后台接收消息在主线程 Pump 时触发，避免网络线程直接触碰 Unity 对象。</summary>
        public event Action<Packet> PushReceived;

        /// <summary>建立连接并启动单一接收循环。</summary>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            if (IsConnected) return;
            await connectLock.WaitAsync(cancellationToken);
            try
            {
                if (IsConnected) return;
                lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                try
                {
                    await transport.ConnectAsync(host, port, lifetime.Token);
                    receiveLoop = ReceiveLoopAsync(lifetime.Token);
                }
                catch
                {
                    lifetime.Cancel();
                    lifetime.Dispose();
                    lifetime = null;
                    transport.Close();
                    throw;
                }
            }
            finally { connectLock.Release(); }
        }

        /// <summary>发送带 request_id 的请求并等待匹配响应，允许多个请求并行在同一连接上运行。</summary>
        public async Task<Packet> RequestAsync(Packet request, CancellationToken cancellationToken = default)
        {
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
            while (pushQueue.TryDequeue(out Packet packet)) PushReceived?.Invoke(packet);
        }

        /// <summary>关闭会话并让所有等待请求结束。</summary>
        public void Dispose()
        {
            lifetime?.Cancel();
            transport.Close();
            foreach (TaskCompletionSource<Packet> completion in pending.Values) completion.TrySetException(new NetworkError(NetworkErrorKind.Disconnected, "网络会话已关闭"));
            pending.Clear();
            connectLock.Dispose();
            sendLock.Dispose();
            lifetime?.Dispose();
            transport.Dispose();
        }

        /// <summary>串行写帧，保证同一连接上的多个请求不会交错写入。</summary>
        private async Task SendPacketAsync(Packet packet, CancellationToken cancellationToken)
        {
            byte[] frame = PacketCodec.Encode(packet);
            await sendLock.WaitAsync(cancellationToken);
            try { await transport.SendAsync(frame, cancellationToken); }
            finally { sendLock.Release(); }
        }

        /// <summary>持续读取长度帧并按 request_id 分发响应或主动推送。</summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    byte[] body = await LengthPrefixedFrameCodec.ReadAsync(transport, cancellationToken);
                    Packet packet = PacketCodec.Decode(body);
                    if (packet.RequestId != 0 && pending.TryRemove(packet.RequestId, out TaskCompletionSource<Packet> completion)) completion.TrySetResult(packet);
                    else pushQueue.Enqueue(packet);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                transport.Close();
                foreach (TaskCompletionSource<Packet> completion in pending.Values) completion.TrySetException(new NetworkError(NetworkErrorKind.Disconnected, "网络接收循环已结束", exception));
                pending.Clear();
            }
        }
    }
}
