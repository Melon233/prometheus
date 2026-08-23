using Xuan.Prometheus.Protocol;

namespace Xuan.Prometheus.World
{
    /// <summary>按 poiId 把服务端下发的 PoiState（协议消息）应用到本地 POI 实体的对应 Logic（启动同步，服务器为权威）。</summary>
    public static class PoiStateApplier
    {
        /// <summary>按实体类型应用持久化状态到对应 Logic。</summary>
        public static void Apply(PoiEntity entity, PoiState state)
        {
            if (entity == null || state == null) return;
            if (entity.TryGetLogic(out StatueLogic s)) s.SetState(state.StatueUnlocked, state.StatueLevel, state.StatueProgress);
            else if (entity.TryGetLogic(out TeleAnchorLogic a)) a.SetState(state.AnchorUnlocked);
            else if (entity.TryGetLogic(out GatheringLogic g)) g.SetRespawnAt(state.GatheringRespawnAt);
            else if (entity.TryGetLogic(out ChestLogic c)) c.SetState(state.ChestOpened);
            else if (entity.TryGetLogic(out DungeonLogic d)) d.SetState(state.DungeonUnlocked);
            else if (entity.TryGetLogic(out MapBossLogic b)) b.SetRespawnAt(state.MapBossRespawnAt);
            else if (entity.TryGetLogic(out SpiritCoreLogic k)) k.SetState(state.SpiritCoreCollected);
        }
    }
}
