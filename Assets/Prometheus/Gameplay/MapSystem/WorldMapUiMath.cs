using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>集中保存地图 UI 的纯坐标计算，保证 HUD 小地图和大地图使用一致的归一化规则。</summary>
    public static class WorldMapUiMath
    {
        /// <summary>根据玩家归一化坐标计算小地图视口，并把视口限制在地图范围内。</summary>
        public static Rect CalculateMinimapViewport(Vector2 playerUv, float viewportFraction)
        {
            float x = Mathf.Clamp(playerUv.x - viewportFraction * 0.5f, 0f, 1f - viewportFraction);
            float y = Mathf.Clamp(playerUv.y - viewportFraction * 0.5f, 0f, 1f - viewportFraction);
            return new Rect(x, y, viewportFraction, viewportFraction);
        }

        /// <summary>把地图归一化坐标换算为当前小地图视口内的锚点；视口外的 POI 不创建标记。</summary>
        public static bool TryGetViewportAnchor(Vector2 mapUv, Rect viewport, out Vector2 anchor)
        {
            anchor = new Vector2((mapUv.x - viewport.x) / viewport.width, (mapUv.y - viewport.y) / viewport.height);
            return anchor.x >= 0f && anchor.x <= 1f && anchor.y >= 0f && anchor.y <= 1f;
        }

        /// <summary>返回抵消大地图父节点缩放的标记局部缩放，使图标保持固定屏幕尺寸。</summary>
        public static float CalculateMarkerInverseScale(float zoom)
        {
            return 1f / zoom;
        }
    }
}
