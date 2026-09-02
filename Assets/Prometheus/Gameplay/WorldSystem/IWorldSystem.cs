using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.World
{
    /// <summary>定义世界 POI、地图坐标和玩家位置能力的公共入口，不承担通用网络请求职责。</summary>
    public interface IWorldSystem : ISystemContract
    {
        /// <summary>获取当前已加载的 POI 数量。</summary>
        int PoiCount { get; }

        /// <summary>获取地图静态定义。</summary>
        WorldMapDefinition MapDefinition { get; }

        /// <summary>获取地图纹理。</summary>
        Texture2D MapTexture { get; }

        /// <summary>获取地图对应的世界长度。</summary>
        float MapWorldLength { get; }

        /// <summary>获取地图对应的世界宽度。</summary>
        float MapWorldWidth { get; }

        /// <summary>获取地图初始缩放。</summary>
        float MapInitialZoom { get; }

        /// <summary>获取或设置当前地图缩放。</summary>
        float MapZoom { get; set; }

        /// <summary>获取当前全部 POI 实体的只读列表。</summary>
        IReadOnlyList<PoiEntity> AllPois { get; }

        /// <summary>尝试读取当前玩家位置。</summary>
        bool TryGetPlayerPosition(out Vector3 position);

        /// <summary>把世界坐标转换为地图归一化坐标。</summary>
        Vector2 WorldToMapNormalized(Vector3 worldPosition);

        /// <summary>向服务器提交一次 POI 交互。</summary>
        UniTask<bool> TryInteractAsync(PoiEntity entity, PoiOp op);

        /// <summary>按语义编号查询 POI 实体。</summary>
        bool TryGetPoiEntity(string poiId, out PoiEntity entity);

        /// <summary>尝试把当前玩家传送到指定 POI。</summary>
        bool TryTeleportToPoi(string poiId);

        /// <summary>按玩家位置刷新 POI 兴趣范围。</summary>
        void RefreshAt(Vector3 playerPos);
    }
}
