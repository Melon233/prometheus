using System.Collections.Generic;

namespace Xuan.Prometheus.World
{
    /// <summary>策划导出 JSON 的根包装（JsonUtility 不支持序列化 List 根）。由编辑器导出工具生成 PoiExport.json，供 Go 服务器读取入库。</summary>
    [System.Serializable]
    public sealed class PoiExportList
    {
        public List<PoiConfig> pois = new List<PoiConfig>();
    }
}
