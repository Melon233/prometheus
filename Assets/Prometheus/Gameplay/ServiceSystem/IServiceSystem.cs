using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.Service
{
    /// <summary>
    /// 单局唯一的网络会话与通用请求通道。
    /// 本契约刻意保持业务无关：它只回答"连接是否可用""把这个 Packet 发出去""收到了一个 Packet"，
    /// 不认识 POI、背包、抽卡等任何具体玩法概念——那些属于各领域自己的 Gateway。
    /// 连接、断连、重连和 PumpEvents 属于 NetworkKit 生命周期能力，不出现在这里。
    /// </summary>
    public interface IServiceSystem : ISystemContract
    {
        /// <summary>获取当前是否已经进入世界且底层网络连接仍然有效。</summary>
        bool IsWorldAvailable { get; }

        /// <summary>通知业务系统当前世界服务已经不可用；首次进入失败不会触发，仅用于已进入世界后的意外断线。</summary>
        event Action WorldUnavailable;

        /// <summary>
        /// 接收服务器下发的通用 Packet，由 ServiceSystem 在自身 OnUpdate 中泵送后于 Unity 主线程触发。
        /// 按业务类型分类是各领域 Gateway 的职责，本通道不做任何解释。
        /// </summary>
        event Action<Packet> PushReceived;

        /// <summary>进入默认世界房间并返回服务器保存的会话数据；底层连接过程不对业务调用方暴露。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<JoinRoomResponse> EnterWorldAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 在确保已进入世界后发送一个通用业务 Packet 并等待关联响应。
        /// 该入口只允许由各领域 Gateway 使用：Gateway 负责组包与解包，本通道负责会话、并发与断线语义。
        /// </summary>
        /// <param name="request">已经由领域 Gateway 组装完成的请求 Packet。</param>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<Packet> RequestAsync(Packet request, CancellationToken cancellationToken = default);
    }
}
