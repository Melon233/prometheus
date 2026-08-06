using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class Core : Singleton<Core>
    {
        private AssetKit assetKit;
        private TimeMachine timeMachine;
        private void Awake()
        {
        }
    }
}