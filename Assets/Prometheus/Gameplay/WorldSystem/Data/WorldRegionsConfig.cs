using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 烘焙生成的整表数据资产：记录区域边长与全部区域，是运行时 POI 加载的唯一数据源。
    /// 由编辑器烘焙工具生成（见 WorldSystem 设计文档 §4.2）。
    /// </summary>
    [CreateAssetMenu(menuName = "World/WorldRegionsConfig", fileName = "WorldRegionsConfig")]
    public class WorldRegionsConfig : ScriptableObject
    {
        /// <summary>区域边长（默认 100m），也是网格分割的格子长。</summary>
        public float RegionSize = 100f;

        /// <summary>全部区域配置。</summary>
        public List<RegionConfig> Regions;
    }
}
