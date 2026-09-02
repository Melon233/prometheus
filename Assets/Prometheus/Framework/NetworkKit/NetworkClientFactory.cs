using Xuan.Prometheus.NetworkKit.Transport;

namespace Xuan.Prometheus.NetworkKit
{
    /// <summary>创建只以 INetworkClient 契约暴露的业务无关网络客户端，具体会话和传输保持在 NetworkKit 内部。</summary>
    public static class NetworkClientFactory
    {
        /// <summary>使用默认 TCP 传输创建网络客户端。</summary>
        public static INetworkClient Create() { return new NetworkClient(); }

        /// <summary>使用调用方提供的字节传输创建网络客户端，供替换传输实现或隔离测试使用。</summary>
        public static INetworkClient Create(IByteTransport transport) { return new NetworkClient(transport); }
    }
}
