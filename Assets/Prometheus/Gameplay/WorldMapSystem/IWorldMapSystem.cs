using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 世界地图的只读投影：加载静态地图定义，并提供世界坐标到地图归一化坐标的换算。
    /// 它不持有任何 POI 状态，也不保存任何界面视图状态（缩放档位属于面板自身）。
    /// </summary>
    public interface IWorldMapSystem : ISystemContract
    {
        /// <summary>获取地图静态定义；地图资源尚未拍摄时为空。</summary>
        WorldMapDefinition Definition { get; }

        /// <summary>获取地图纹理；定义缺失时为空。</summary>
        Texture2D Texture { get; }

        /// <summary>获取地图覆盖的世界 X 轴长度。</summary>
        float WorldLength { get; }

        /// <summary>获取地图覆盖的世界 Z 轴宽度。</summary>
        float WorldWidth { get; }

        /// <summary>获取配置文件中的初始缩放倍数，供面板首次打开时确定默认视图。</summary>
        float InitialZoom { get; }

        /// <summary>把世界坐标换算为地图归一化坐标，保证 HUD 小地图和大地图使用同一套规则。</summary>
        /// <param name="worldPosition">待换算的世界坐标。</param>
        Vector2 WorldToNormalized(Vector3 worldPosition);
    }
}
