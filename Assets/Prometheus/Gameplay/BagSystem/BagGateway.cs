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
        /// <summary>构造注入的会话通道；依赖写在签名上，注册顺序因此由编译器强制。</summary>
        private readonly IServiceSystem Service;

        /// <summary>创建背包领域网关。</summary>
        /// <param name="serviceSystem">承载本领域请求的唯一会话通道。</param>
        public BagGateway(IServiceSystem serviceSystem)
        {
            Service = serviceSystem ?? throw new System.ArgumentNullException(nameof(serviceSystem));
        }

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
