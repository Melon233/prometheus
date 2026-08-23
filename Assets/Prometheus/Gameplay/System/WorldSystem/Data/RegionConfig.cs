using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.World
{
    /// <summary>一个网格区域内的兴趣点集合；RegionId 用网格坐标表示，如 "1x1"、"12x345"。</summary>
    [Serializable]
    public class RegionConfig
    {
        /// <summary>唯一区域 id，网格坐标字符串。</summary>
        public string RegionId;

        /// <summary>该区域内的兴趣点列表。</summary>
        public List<PoiConfig> Pois;
    }
}
