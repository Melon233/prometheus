using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.Service;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 背包领域网络适配器的正式实现。
    /// 每个方法只做三件事：组装 Packet、经由通用通道发出、取出本领域关心的响应字段。
    /// </summary>
    internal sealed class BagGateway : XSystem, IBagGateway
    {
        /// <summary>按 ARCH-SYS-005 在使用点解析当前单局会话通道，不长期保存 ServiceSystem 实例。</summary>
        private static IServiceSystem Service => Core.Gameplay.GetSystem<IServiceSystem>();

        /// <inheritdoc />
        public async UniTask<GetItemsResponse> GetItemsAsync(CancellationToken cancellationToken = default)
        {
            return (await Service.RequestAsync(new Packet { GetItems = new GetItemsRequest() }, cancellationToken)).GetItemsResp;
        }

        /// <inheritdoc />
        public async UniTask<GachaResponse> DrawGachaAsync(CancellationToken cancellationToken = default)
        {
            return (await Service.RequestAsync(new Packet { Gacha = new GachaRequest() }, cancellationToken)).GachaResp;
        }
    }
}
