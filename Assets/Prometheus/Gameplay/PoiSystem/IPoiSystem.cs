using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>定义世界 POI 生命周期、交互和玩家位置能力的公共入口；地图投影由 IWorldMapSystem 负责，权威状态读写由 IPoiGateway 负责。</summary>
    public interface IPoiSystem : ISystem
    {
        /// <summary>获取当前已加载的 POI 数量。</summary>
        int PoiCount { get; }

        /// <summary>获取当前全部 POI 实体的只读列表。</summary>
        IReadOnlyList<PoiMono> AllPois { get; }

        /// <summary>尝试读取当前玩家位置。</summary>
        bool TryGetPlayerPosition(out Vector3 position);

        /// <summary>向服务器提交一次 POI 交互；交互操作按 POI 类型解析。</summary>
        UniTask<bool> TryInteractAsync(PoiMono poi);

        /// <summary>按语义编号查询场景 POI 组件。</summary>
        bool TryGetPoi(string poiId, out PoiMono poi);

        /// <summary>尝试把当前玩家传送到指定 POI。</summary>
        bool TryTeleportToPoi(string poiId);

    }
}
