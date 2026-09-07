using System;
using UnityEngine;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus
{
    /// <summary>集中管理地图 POI 图标的 YooAsset 地址，保证 HUD 小地图和大地图使用完全一致的图标资源。</summary>
    public static class WorldMapIconCatalog
    {
        /// <summary>根据 POI 类型加载对应的公共 Atlas Sprite。</summary>
        /// <param name="poiType">需要显示的 POI 类型。</param>
        /// <returns>与 POI 类型匹配的图标 Sprite。</returns>
        public static Sprite LoadPoiIcon(PoiType poiType)
        {
            string address = poiType switch
            {
                PoiType.TeleAnchor => "UI_TeleAnchor",
                PoiType.Statue => "UI_Statue",
                PoiType.Chest => "UI_Chest",
                PoiType.SpiritCore => "UI_SpiritCore",
                PoiType.Gathering => "UI_Gathering",
                PoiType.Dungeon => "UI_Dungeon",
                PoiType.MapBoss => "UI_Boss",
                PoiType.MonsterCamp => "UI_MonsterCamp",
                _ => throw new ArgumentOutOfRangeException(nameof(poiType), poiType, "Unknown POI type.")
            };
            return Core.Asset.LoadAssetSync<Sprite>(address);
        }

        /// <summary>加载地图左上角关闭按钮使用的公共 Atlas Sprite。</summary>
        /// <returns>关闭按钮图标。</returns>
        public static Sprite LoadCloseIcon()
        {
            return Core.Asset.LoadAssetSync<Sprite>("UI_Close");
        }

        /// <summary>加载地图中玩家当前位置使用的原始角色标记。</summary>
        public static Sprite LoadPlayerIcon()
        {
            return Core.Asset.LoadAssetSync<Sprite>("UI_MarkLocalAvatar");
        }
    }
}
