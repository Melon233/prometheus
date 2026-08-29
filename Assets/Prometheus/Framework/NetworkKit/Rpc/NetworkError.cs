using System;

namespace Xuan.Prometheus.NetworkKit.Rpc
{
    /// <summary>网络请求统一错误分类，业务层不需要解析底层异常文本。</summary>
    public sealed class NetworkError : Exception
    {
        /// <summary>创建带分类和描述的网络错误。</summary>
        public NetworkError(NetworkErrorKind kind, string message, Exception inner = null) : base(message, inner) { Kind = kind; }

        /// <summary>获取网络错误分类。</summary>
        public NetworkErrorKind Kind { get; }
    }

    /// <summary>网络请求可能遇到的底层错误类别。</summary>
    public enum NetworkErrorKind
    {
        Transport,
        Frame,
        Codec,
        Timeout,
        Cancelled,
        Disconnected,
        Remote,
    }
}
