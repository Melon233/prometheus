using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.World.Editor
{
    /// <summary>
    /// 烘焙 / 导出工具：扫描场景中全部 PoiMono。
    /// 1. 生成语义化 Id（"{Region}_{类型}_{序号}"）与 chunkId，导出到 JSON 供服务器入库；
    /// 2. 按 chunkId 分组烘焙为 WorldRegionsConfig 资产（保留兼容）。
    /// </summary>
    public class WorldBakeWindow : EditorWindow
    {
        /// <summary>默认烘焙输出资产路径（位于 BundleResources 收集目录内）。</summary>
        public const string DefaultOutputPath = "Assets/BundleResources/Config/Global/WorldRegionsConfig.asset";

        /// <summary>默认 JSON 导出路径（Resources/Config 下，供 Go 服务器读取）。</summary>
        public const string DefaultExportPath = "Assets/Resources/Config/PoiExport.json";

        /// <summary>当前默认地区（所有 POI 均属 Mond）。</summary>
        public const string DefaultRegion = "Mond";

        [SerializeField] private string outputPath = DefaultOutputPath;
        [SerializeField] private string exportPath = DefaultExportPath;

        [MenuItem("Prometheus/World/Bake World Regions")]
        private static void Open() => GetWindow<WorldBakeWindow>("World Bake");

        [MenuItem("Prometheus/World/Bake World Regions (Direct)")]
        private static void BakeDirect() => BakeScene(DefaultOutputPath);

        /// <summary>直接导出全部 POI 定义到默认 JSON 路径（供服务器入库）。</summary>
        [MenuItem("Prometheus/World/Export POI Data (JSON)")]
        private static void ExportDirect() => ExportPoiJsonTo(DefaultExportPath);

        private void OnGUI()
        {
            EditorGUILayout.LabelField("World Bake / Export", EditorStyles.boldLabel);
            outputPath = EditorGUILayout.TextField("Bake Asset", outputPath);
            exportPath = EditorGUILayout.TextField("Export JSON", exportPath);
            EditorGUILayout.Space();
            if (GUILayout.Button("Bake")) BakeScene(outputPath);
            if (GUILayout.Button("Export POI Data (JSON)")) ExportPoiJsonTo(exportPath);
        }

        /// <summary>扫描场景全部 PoiMono，为每个生成语义 Id（缺失时）与 chunkId，并深拷贝为 PoiConfig 列表。</summary>
        /// <param name="writeBackToScene">若为 true，同时把生成的 Id/Region/ChunkId 写回场景 PoiMono.Config（保证运行时与导出一致）。</param>
        public static List<PoiConfig> CollectPoiDefs(bool writeBackToScene = false)
        {
            var result = new List<PoiConfig>();
            PoiMono[] pois = FindObjectsOfType<PoiMono>(true);
            var typeSeq = new Dictionary<PoiType, int>();
            foreach (PoiMono poi in pois)
            {
                if (poi == null || poi.Config == null) continue;
                PoiConfig src = poi.Config;
                // 已分配过 Id 则复用；否则按类型分配 "{Region}_{类型}_{序号}"（序号 1 起，按类型独立计数）。
                string id = src.Id;
                if (string.IsNullOrEmpty(id))
                {
                    if (!typeSeq.TryGetValue(src.PoiType, out int n)) n = 0;
                    typeSeq[src.PoiType] = ++n;
                    id = $"{DefaultRegion}_{src.PoiType}_{n}";
                }
                Vector3 pos = poi.transform.position; // 权威位置 = GameObject 世界坐标（策划直接移动对象，字段仅作缓存）
                int chunkId = ChunkIdCodec.EncodeFromPosition(pos);
                if (writeBackToScene && (src.Id != id || src.Region != DefaultRegion || src.ChunkId != chunkId || src.Position != pos))
                {
                    src.Id = id;
                    src.Region = DefaultRegion;
                    src.ChunkId = chunkId;
                    src.Position = pos;
                    EditorUtility.SetDirty(poi);
                }
                // 深拷贝，避免资产/导出与场景摆放对象共享可变数据；使用刚生成的 Id/chunkId/位置。
                PoiConfig copy = CloneConfig(src);
                copy.Id = id;
                copy.Region = DefaultRegion;
                copy.ChunkId = chunkId;
                copy.Position = pos;
                result.Add(copy);
            }
            if (writeBackToScene) AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>按 chunkId 分组烘焙为 WorldRegionsConfig 资产。</summary>
        public static void BakeScene(string outputPath)
        {
            List<PoiConfig> defs = CollectPoiDefs();
            if (defs.Count == 0) { Debug.LogWarning("World Bake: no PoiMono found in scene."); return; }

            var chunkMap = new Dictionary<int, RegionConfig>();
            foreach (PoiConfig cfg in defs)
            {
                if (!chunkMap.TryGetValue(cfg.ChunkId, out RegionConfig region))
                {
                    region = new RegionConfig { RegionId = cfg.ChunkId.ToString(), Pois = new List<PoiConfig>() };
                    chunkMap.Add(cfg.ChunkId, region);
                }
                region.Pois.Add(cfg);
            }

            WorldRegionsConfig config = ScriptableObject.CreateInstance<WorldRegionsConfig>();
            config.RegionSize = ChunkIdCodec.ChunkSize;
            config.Regions = chunkMap.Values.OrderBy(r => r.RegionId).ToList();
            SaveConfig(config, outputPath);
            Debug.Log($"World Bake: {defs.Count} POIs -> {config.Regions.Count} chunks, saved to {outputPath}");
        }

        /// <summary>导出全部 POI 定义（含 Id/Region/PoiType/Position/Rotation/ChunkId）到 JSON，供服务器读取入库。</summary>
        public static void ExportPoiJsonTo(string outputPath)
        {
            // 单次遍历：把唯一 Id/ChunkId 写回场景配置，并生成导出 JSON（两者一致）。
            List<PoiConfig> defs = CollectPoiDefs(writeBackToScene: true);
            if (defs.Count == 0) { Debug.LogWarning("World Export: no PoiMono found in scene."); return; }

            PoiExportList wrapper = new PoiExportList { pois = defs };
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), outputPath);
            EnsureFolderExists(outputPath);
            File.WriteAllText(fullPath, JsonUtility.ToJson(wrapper, true));
            AssetDatabase.Refresh();
            Debug.Log($"World Export: {defs.Count} POIs exported to {outputPath}");
        }

        /// <summary>按字段拷贝 PoiConfig；各类型子 Config 使用新实例，避免资产引用场景对象。</summary>
        private static PoiConfig CloneConfig(PoiConfig src)
        {
            return new PoiConfig
            {
                Id = src.Id,
                Region = src.Region,
                PoiType = src.PoiType,
                Position = src.Position,
                Rotation = src.Rotation,
                ChunkId = src.ChunkId,
                aoiExempt = src.aoiExempt,
                Statue = src.Statue != null ? new StatueConfig() : null,
                TeleAnchor = CloneTeleAnchor(src.TeleAnchor),
                Chest = src.Chest != null ? new ChestConfig() : null,
                SpiritCore = src.SpiritCore != null ? new SpiritCoreConfig() : null,
                Gathering = src.Gathering != null ? new GatheringConfig() : null,
                Dungeon = src.Dungeon != null ? new DungeonConfig() : null,
                MapBoss = src.MapBoss != null ? new MapBossConfig() : null,
                MonsterCamp = src.MonsterCamp != null ? new MonsterCampConfig() : null
            };
        }

        /// <summary>拷贝传送锚点配置（当前唯一带字段的类型）；其它类型字段为空，暂用空实例。</summary>
        private static TeleAnchorConfig CloneTeleAnchor(TeleAnchorConfig src)
        {
            if (src == null) return null;
            return new TeleAnchorConfig { initiallyUnlocked = src.initiallyUnlocked };
        }

        /// <summary>若目标资产已存在则覆盖写入，否则新建；保证输出目录存在。</summary>
        private static void SaveConfig(WorldRegionsConfig config, string outputPath)
        {
            EnsureFolderExists(outputPath);
            WorldRegionsConfig existing = AssetDatabase.LoadAssetAtPath<WorldRegionsConfig>(outputPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(config, existing);
                Object.DestroyImmediate(config);
            }
            else
            {
                AssetDatabase.CreateAsset(config, outputPath);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>递归创建资产路径的父目录，保证 CreateAsset 目标文件夹存在。</summary>
        private static void EnsureFolderExists(string assetPath)
        {
            string[] parts = assetPath.Substring(0, assetPath.LastIndexOf('/')).Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                current += "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(current))
                    AssetDatabase.CreateFolder(current.Substring(0, current.LastIndexOf('/')), parts[i]);
            }
        }
    }
}
