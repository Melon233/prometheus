using System;

namespace Xuan.Prometheus.World
{
    /// <summary>七天神像专属配置：初始祝福、回复/供奉/切祝福等。</summary>
    [Serializable]
    public class StatueConfig { }

    /// <summary>传送锚点专属配置：是否初始解锁等。</summary>
    [Serializable]
    public class TeleAnchorConfig
    {
        /// <summary>该锚点是否初始即处于已解锁状态。</summary>
        public bool initiallyUnlocked;
    }

    /// <summary>宝箱专属配置：奖励表引用等。</summary>
    [Serializable]
    public class ChestConfig { }

    /// <summary>神瞳专属配置：所属区域、收集里程碑等。</summary>
    [Serializable]
    public class SpiritCoreConfig { }

    /// <summary>采集物专属配置：资源类型、掉落表、刷新周期等。</summary>
    [Serializable]
    public class GatheringConfig
    {
        /// <summary>采集后的重生周期（秒）。</summary>
        public float respawnSeconds = 30f;
    }

    /// <summary>副本专属配置：关联副本场景、解锁条件等。</summary>
    [Serializable]
    public class DungeonConfig { }

    /// <summary>地图 Boss 专属配置：战斗配置、掉落表、刷新周期等。</summary>
    [Serializable]
    public class MapBossConfig
    {
        /// <summary>击杀后的重生周期（秒）。</summary>
        public float respawnSeconds = 300f;
    }

    /// <summary>怪物营地专属配置：敌人配置、掉落、刷新周期等。</summary>
    [Serializable]
    public class MonsterCampConfig
    {
        /// <summary>清剿后的重生周期（秒）。</summary>
        public float respawnSeconds = 120f;
    }
}
