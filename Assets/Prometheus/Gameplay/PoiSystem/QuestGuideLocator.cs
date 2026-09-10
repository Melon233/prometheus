using UnityEngine;
using Xuan.Prometheus.Quest;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 把任务的语义导航目标换算成世界坐标。
    ///
    /// 这一步落在世界侧而不是任务系统里：任务只保存「去找长者」这样的语义目标，
    /// 换算成坐标需要查 <see cref="IPoiSystem"/>，而任务系统不允许解析任何其他 System（铁律 Q6）。
    /// 分工的额外好处是存档稳定——NPC 被挪了位置，旧存档里的导航目标依然正确。
    ///
    /// HUD 小地图与大地图共用本换算，两处指引点因此不可能指向不同的地方。
    /// </summary>
    public static class QuestGuideLocator
    {
        /// <summary>解析一个导航目标的世界坐标；目标无效或当前不在已加载区域内时返回 false。</summary>
        /// <param name="guide">任务步骤上的导航目标。</param>
        /// <param name="position">解析成功时返回世界坐标。</param>
        public static bool TryResolve(QuestGuide guide, out Vector3 position)
        {
            position = default;
            if (!guide.IsValid) return false;
            if (guide.Kind == QuestGuideKind.Position)
            {
                position = guide.Position;
                return true;
            }
            if (!Core.Gameplay.TryGetSystem(out IPoiSystem poiSystem)) return false;
            if (guide.Kind == QuestGuideKind.Poi)
            {
                if (!poiSystem.TryGetPoi(guide.TargetId, out PoiMono poi) || poi == null) return false;
                position = poi.transform.position;
                return true;
            }
            return TryResolveNpc(poiSystem, guide.TargetId, out position);
        }

        /// <summary>在已加载的 POI 中查找承载指定 NPC 的场景对象。</summary>
        private static bool TryResolveNpc(IPoiSystem poiSystem, string npcId, out Vector3 position)
        {
            position = default;
            System.Collections.Generic.IReadOnlyList<PoiMono> pois = poiSystem.AllPois;
            for (int index = 0; index < pois.Count; index++)
            {
                PoiMono poi = pois[index];
                if (poi == null || poi.Config == null || poi.Config.Npc == null) continue;
                if (!string.Equals(poi.Config.Npc.NpcId, npcId, System.StringComparison.Ordinal)) continue;
                position = poi.transform.position;
                return true;
            }
            return false;
        }
    }
}
