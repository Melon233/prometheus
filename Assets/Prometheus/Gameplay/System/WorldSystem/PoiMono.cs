using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 场景中摆放的 POI 组件：承载 PoiConfig，运行时由 WorldSystem 扫描并绑定到 PoiEntity。
    /// 策划可直接在场景放置 POI 预制体预览效果；PoiId 在导出时按网格归属生成。
    /// </summary>
    public class PoiMono : MonoBehaviour
    {
        /// <summary>该 POI 的配置数据，由 WorldSystem 扫描读取。</summary>
        public PoiConfig Config;
    }
}
