using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 描述一张世界地图的静态资源：纹理只负责视觉，原点和覆盖范围负责把世界 XZ 坐标稳定映射到纹理 UV。
    /// 地图拍摄工具会生成或更新该资产，运行时由 WorldSystem 统一提供给 HUD 与 MapPanel。
    /// </summary>
    [CreateAssetMenu(menuName = "World/World Map Definition", fileName = "WorldMapDefinition")]
    public sealed class WorldMapDefinition : ScriptableObject
    {
        /// <summary>当前地图的稳定业务标识，后续支持多张地图时用于切换数据。</summary>
        public string MapId = "MainWorld";

        /// <summary>地图左下角在世界 XZ 平面上的坐标；Y 仅保留原点高度信息，不参与 UV 换算。</summary>
        public Vector3 Origin = Vector3.zero;

        /// <summary>地图覆盖的世界 X 轴长度，必须为正数。</summary>
        [Min(0.01f)] public float WorldLength = 1000f;

        /// <summary>地图覆盖的世界 Z 轴宽度，必须为正数。</summary>
        [Min(0.01f)] public float WorldWidth = 1000f;

        /// <summary>编辑器拍摄生成的静态地图纹理；动态玩家、敌人和 POI 图标不应烘焙进纹理。</summary>
        public Texture2D MapTexture;

        /// <summary>打开大地图时使用的初始缩放倍数；实际值会在 MapPanel 中限制到允许范围。</summary>
        [Range(1f, 4f)] public float InitialZoom = 1f;

        /// <summary>把世界位置映射到地图左下角为零、右上角为一的归一化坐标，不对结果裁剪。</summary>
        /// <param name="worldPosition">待转换的世界坐标。</param>
        /// <returns>X 对应地图 U，Y 对应地图 V。</returns>
        public Vector2 WorldToNormalized(Vector3 worldPosition)
        {
            return new Vector2((worldPosition.x - Origin.x) / WorldLength, (worldPosition.z - Origin.z) / WorldWidth);
        }

        /// <summary>把归一化地图坐标转换回世界 XZ 坐标，并使用地图原点的 Y 作为高度。</summary>
        /// <param name="normalizedPosition">地图归一化坐标。</param>
        /// <returns>对应的世界坐标。</returns>
        public Vector3 NormalizedToWorld(Vector2 normalizedPosition)
        {
            return new Vector3(Origin.x + normalizedPosition.x * WorldLength, Origin.y, Origin.z + normalizedPosition.y * WorldWidth);
        }
    }
}
