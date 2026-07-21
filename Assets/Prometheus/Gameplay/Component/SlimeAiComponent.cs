using log4net.Util;

namespace Xuan.Prometheus.Component
{
    public interface ISlimeCtx : ICtx
    {
        Transform EnmityTarget { get; set; }
    }
    public class SlimeAiComponent : Component, ISlimeCtx
    {
        public Transform EnmityTarget { get; set; }
    }
}