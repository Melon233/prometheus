namespace Xuan.Prometheus.World
{
    /// <summary>承载一个 POI 的运行时数据（源自烘焙的 PoiConfig）。</summary>
    public sealed class PoiComponent : Xuan.Prometheus.Component.Component
    {
        /// <summary>该 POI 的配置数据。</summary>
        public PoiConfig Config;
    }
}
