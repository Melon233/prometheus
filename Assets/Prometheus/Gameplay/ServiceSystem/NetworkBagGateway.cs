using System.Threading;
using Cysharp.Threading.Tasks;
using Xuan.Prometheus.Protocol;
using Xuan.Prometheus.Service;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 背包领域网关的联网实现（当前未接入组合根，单机版使用 LocalBagGateway）。
    /// 每个方法只做三件事：组装 Packet、经由通用通道发出、取出本领域关心的响应字段。
    /// </summary>
    internal sealed class NetworkBagGateway : IBagGateway
    {
        /// <summary>构造注入的会话通道；依赖写在签名上，注册顺序因此由编译器强制。</summary>
        private readonly IServiceSystem Service;

        /// <summary>创建背包领域联网网关。</summary>
        /// <param name="serviceSystem">承载本领域请求的唯一会话通道。</param>
        public NetworkBagGateway(IServiceSystem serviceSystem)
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

        /// <summary>本系统没有异步初始化工作，直接返回已完成任务。</summary>
        public UniTask AfterNewAsync()
        {
            return UniTask.CompletedTask;
        }

        /// <summary>本系统没有同步初始化工作。</summary>
        public void AfterNew()
        {
        }

        /// <summary>本系统不需要逐帧驱动。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public void OnUpdate(float dt)
        {
        }

        /// <summary>本系统不持有需要释放的资源。</summary>
        public void Dispose()
        {
        }
    }
}
