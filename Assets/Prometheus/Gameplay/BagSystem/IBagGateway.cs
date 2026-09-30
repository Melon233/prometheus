using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 背包领域的权威数据适配器：BagSystem 只通过它读取背包与执行抽卡，不关心数据来自哪里。
    /// 当前单机版由 <see cref="LocalBagGateway"/> 直接读写内存中的背包（不做存档）；
    /// 联网版实现（Prometheus.Gameplay.Online 中的 NetworkBagGateway）把同一组调用翻译成协议 Packet。
    /// Gateway 不保存 BagSystem 的领域状态，缓存归 BagSystem。
    /// </summary>
    public interface IBagGateway : ISystem
    {
        /// <summary>获取当前玩家的权威背包快照。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default);

        /// <summary>执行一次抽卡并返回奖励与最新背包快照。</summary>
        /// <param name="cancellationToken">调用方自身生命周期令牌。</param>
        UniTask<GachaResponse> DrawGachaAsync(CancellationToken cancellationToken = default);
    }
}
