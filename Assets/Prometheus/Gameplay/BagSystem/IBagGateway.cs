using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 背包领域的网络适配器：把背包与抽卡语义翻译成协议 Packet，再把响应翻译回背包语义。
    /// 它是本领域唯一被允许调用 <see cref="Service.IServiceSystem.RequestAsync"/> 的位置；
    /// 会话、并发与断线语义仍然由 ServiceSystem 独占。Gateway 不保存领域状态，缓存归 BagSystem。
    /// </summary>
    public interface IBagGateway : ISystemContract
    {
        /// <summary>获取当前会话玩家的权威背包快照。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default);

        /// <summary>执行一次抽卡并返回奖励与最新背包快照。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<GachaResponse> DrawGachaAsync(CancellationToken cancellationToken = default);
    }
}
