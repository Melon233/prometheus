using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>POI 数据变化通知；Id 为空表示需要重新读取全部 POI。</summary>
    public sealed class WorldMapPoiChangedEvent : IEvent
    {
        /// <summary>发生变化的 POI 语义 Id；批量重建时为空。</summary>
        public string PoiId { get; }

        /// <summary>创建 POI 地图表现变化事件。</summary>
        public WorldMapPoiChangedEvent(string poiId)
        {
            PoiId = poiId;
        }
    }
}
