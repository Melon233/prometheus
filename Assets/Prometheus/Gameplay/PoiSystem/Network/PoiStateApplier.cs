using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>把网关下发的 PoiState（协议消息）按类型应用到场景 POI 组件；网关为唯一权威。</summary>
    public static class PoiStateApplier
    {
        /// <summary>按 POI 类型把网关下发的权威状态写入对应字段，并同步场景表现的显隐。</summary>
        /// <param name="poi">目标场景 POI 组件。</param>
        /// <param name="state">网关下发的该 POI 状态。</param>
        public static void Apply(PoiMono poi, PoiState state)
        {
            if (poi == null || poi.Config == null || state == null) return;
            switch (poi.Config.PoiType)
            {
                case PoiType.Statue: poi.SetUnlocked(state.StatueUnlocked); break;
                case PoiType.TeleAnchor: poi.SetUnlocked(state.AnchorUnlocked); break;
                case PoiType.Dungeon: poi.SetUnlocked(state.DungeonUnlocked); break;
                case PoiType.Chest: poi.SetConsumed(state.ChestOpened); break;
                case PoiType.SpiritCore: poi.SetConsumed(state.SpiritCoreCollected); break;
                case PoiType.Gathering: poi.SetRespawnAt(state.GatheringRespawnAt); break;
                case PoiType.MapBoss: poi.SetRespawnAt(state.MapBossRespawnAt); break;
            }
        }
    }
}
