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

        /// <summary>
        /// 加载当前追踪任务的指引点图标。
        ///
        /// 目前用的是纯白占位图，由调用方染成金色并旋转 45 度成菱形——公共 Atlas 里还没有任务指引图标。
        /// 直接画正方形会被误读成「地上有个黄色方块」，菱形至少能读出「这是个地图标记」。
        /// 等美术出图后把这里的地址换掉、并把 <see cref="QuestGuideRotation"/> 归零即可，两处地图无需改动。
        /// </summary>
        public static Sprite LoadQuestGuideIcon()
        {
            return Core.Asset.LoadAssetSync<Sprite>("white");
        }

        /// <summary>获取任务指引点的染色；与追踪条标题同一个金色，使两处在视觉上指向同一件事。</summary>
        public static Color QuestGuideColor => new Color(1f, 0.84f, 0.35f, 1f);

        /// <summary>获取任务指引点占位图的旋转角度；换成正式图标后应归零。</summary>
        public static float QuestGuideRotation => 45f;
    }
}
