using System;

namespace Xuan.Prometheus.World
{
    /// <summary>按 PoiType 创建对应的 POI 行为逻辑。</summary>
    public static class PoiLogicFactory
    {
        /// <summary>根据类型返回对应 PoiLogic 实例。</summary>
        public static PoiLogic Create(PoiType type) => type switch
        {
            PoiType.TeleAnchor => new TeleAnchorLogic(),
            PoiType.Statue => new StatueLogic(),
            PoiType.Chest => new ChestLogic(),
            PoiType.SpiritCore => new SpiritCoreLogic(),
            PoiType.Gathering => new GatheringLogic(),
            PoiType.Dungeon => new DungeonLogic(),
            PoiType.MapBoss => new MapBossLogic(),
            PoiType.MonsterCamp => new MonsterCampLogic(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }
}
