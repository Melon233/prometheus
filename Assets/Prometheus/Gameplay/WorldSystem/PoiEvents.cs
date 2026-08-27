namespace Xuan.Prometheus.World
{
    /// <summary>宝箱被开启的事件数据。</summary>
    public sealed class PoiOpenedEvent : IEvent { public string Id; }

    /// <summary>神瞳被收集的事件数据。</summary>
    public sealed class PoiCollectedEvent : IEvent { public string Id; }

    /// <summary>采集物被采集的事件数据。</summary>
    public sealed class PoiGatheredEvent : IEvent { public string Id; }

    /// <summary>解锁类 POI（锚点/神像/副本）被解锁的事件数据。</summary>
    public sealed class PoiUnlockedEvent : IEvent { public string Id; }

    /// <summary>可刷新战斗类 POI（地图Boss/怪物营地）被击败/清剿的事件数据。</summary>
    public sealed class PoiDefeatedEvent : IEvent { public string Id; }
}
