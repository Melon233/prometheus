namespace Xuan.Prometheus.World
{
    /// <summary>宝箱被开启的事件数据。</summary>
    public sealed class PoiOpenedEvent : IEvent
    {
        /// <summary>创建一条携带稳定 POI ID 的宝箱开启事实。</summary>
        public PoiOpenedEvent(string id) { Id = id; }

        /// <summary>获取被开启 POI 的稳定 ID。</summary>
        public string Id { get; }
    }

    /// <summary>神瞳被收集的事件数据。</summary>
    public sealed class PoiCollectedEvent : IEvent
    {
        /// <summary>创建一条携带稳定 POI ID 的神瞳收集事实。</summary>
        public PoiCollectedEvent(string id) { Id = id; }

        /// <summary>获取被收集 POI 的稳定 ID。</summary>
        public string Id { get; }
    }

    /// <summary>采集物被采集的事件数据。</summary>
    public sealed class PoiGatheredEvent : IEvent
    {
        /// <summary>创建一条携带稳定 POI ID 的采集完成事实。</summary>
        public PoiGatheredEvent(string id) { Id = id; }

        /// <summary>获取被采集 POI 的稳定 ID。</summary>
        public string Id { get; }
    }

    /// <summary>解锁类 POI（锚点/神像/副本）被解锁的事件数据。</summary>
    public sealed class PoiUnlockedEvent : IEvent
    {
        /// <summary>创建一条携带稳定 POI ID 的解锁事实。</summary>
        public PoiUnlockedEvent(string id) { Id = id; }

        /// <summary>获取被解锁 POI 的稳定 ID。</summary>
        public string Id { get; }
    }

    /// <summary>可刷新战斗类 POI（地图Boss/怪物营地）被击败/清剿的事件数据。</summary>
    public sealed class PoiDefeatedEvent : IEvent
    {
        /// <summary>创建一条携带稳定 POI ID 的击败或清剿事实。</summary>
        public PoiDefeatedEvent(string id) { Id = id; }

        /// <summary>获取被击败或清剿 POI 的稳定 ID。</summary>
        public string Id { get; }
    }
}
