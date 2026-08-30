using UnityEngine;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 一个兴趣点的配置数据：既是烘焙产物 WorldRegionsConfig 中的最小单元，也是编辑器 PoiMono 的配置载体。
    /// Id 是创建时分配并写回场景的不可变 UUID（32 位无分隔小写字符串），是服务器主键与客户端同步键；ChunkId 为空间分区，用于按区块查询。
    /// </summary>
    [System.Serializable]
    public class PoiConfig
    {
        /// <summary>不可变 UUID 主键，格式为 32 位无分隔小写字符串。缺失或旧格式会在烘焙/导出时生成并写回场景，删除后永不复用。</summary>
        public string Id;

        /// <summary>所属地区（当前均为 Mond）。</summary>
        public string Region = "Mond";

        /// <summary>八种兴趣点之一，决定运行时注入哪套 Logic/Component。</summary>
        public PoiType PoiType;

        /// <summary>世界坐标，用于 chunk 归属计算与兴趣半径距离过滤。</summary>
        public Vector3 Position;

        /// <summary>世界旋转（策划导出的朝向），当前仅存储暂不应用。</summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>空间分区 chunkId（三位编码 chunkX*1000 + chunkY，非负）。</summary>
        public int ChunkId;

        /// <summary>是否豁免 AOI 裁剪：大体建筑（七天神像/副本/传送锚点）常驻可见，远离也不回收。</summary>
        public bool aoiExempt;

        /// <summary>七天神像专属配置。</summary>
        public StatueConfig Statue;

        /// <summary>传送锚点专属配置。</summary>
        public TeleAnchorConfig TeleAnchor;

        /// <summary>宝箱专属配置。</summary>
        public ChestConfig Chest;

        /// <summary>神瞳专属配置。</summary>
        public SpiritCoreConfig SpiritCore;

        /// <summary>采集物专属配置。</summary>
        public GatheringConfig Gathering;

        /// <summary>副本专属配置。</summary>
        public DungeonConfig Dungeon;

        /// <summary>地图 Boss 专属配置。</summary>
        public MapBossConfig MapBoss;

        /// <summary>怪物营地专属配置。</summary>
        public MonsterCampConfig MonsterCamp;
    }
}
