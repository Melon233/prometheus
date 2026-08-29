using System.Threading;
using System.Threading.Tasks;
using Xuan.Prometheus.NetworkKit.Session;
using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.NetworkKit.Services
{
    /// <summary>抽卡业务服务；服务器负责扣除一个神瞳、随机选择奖励并返回最新背包。</summary>
    public sealed class GachaService
    {
        private readonly NetworkSession session;

        /// <summary>创建抽卡服务。</summary>
        public GachaService(NetworkSession session) { this.session = session; }

        /// <summary>请求一次抽卡；失败时由响应 Error 给出业务原因。</summary>
        public async Task<GachaResponse> DrawAsync(CancellationToken cancellationToken = default)
        {
            Packet response = await session.RequestAsync(new Packet { Gacha = new GachaRequest() }, cancellationToken);
            return response.GachaResp;
        }
    }
}
