using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.ConfigKit;

namespace Xuan.Prometheus.ConfigKit.Editor
{
    /// <summary>负责扫描项目自有 ScriptableObject 并维护配置中心的派生 JSON 索引。</summary>
    internal static class ConfigCenterIndexer
    {
        private const string IndexPath = "Library/Prometheus/ConfigCenter/config-index.json";
        private const string ConfiguredRootsPreferenceKey = "Prometheus.ConfigKit.ConfiguredRoots.v2";
        private static readonly string[] Roots = { "Assets/BundleResources/Config", "Assets/" };
        private static readonly string[] ExcludedFragments = { "/Plugins/", "/Trd/", "/ThirdParty/", "/Tests/", "/Test/" };

        /// <summary>获取配置中心当前展示的扫描根目录，目录顺序同时决定窗口中的一级节点顺序。</summary>
        internal static IReadOnlyList<string> ScanRoots => LoadConfiguredRoots();

        /// <summary>读取配置中心根目录设置；首次使用时返回内置的两个默认目录。</summary>
        internal static List<string> GetConfiguredRoots() { return LoadConfiguredRoots(); }

        /// <summary>保存配置中心根目录设置，并供窗口和资产导入回调立即使用。</summary>
        internal static void SaveConfiguredRoots(IList<string> roots)
        {
            RootPathList value = new RootPathList { paths = roots.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim().Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            EditorPrefs.SetString(ConfiguredRootsPreferenceKey, JsonUtility.ToJson(value));
        }

        /// <summary>读取持久化根目录列表；没有设置时复制默认值，避免调用方修改静态默认数组。</summary>
        private static List<string> LoadConfiguredRoots()
        {
            if (!EditorPrefs.HasKey(ConfiguredRootsPreferenceKey)) return Roots.ToList();
            RootPathList value = JsonUtility.FromJson<RootPathList>(EditorPrefs.GetString(ConfiguredRootsPreferenceKey));
            return value?.paths?.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => path.Trim().Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        }

        /// <summary>读取现有索引；索引不存在或损坏时返回空索引。</summary>
        public static ConfigCenterIndex Load()
        {
            if (!File.Exists(IndexPath)) return new ConfigCenterIndex();
            try { return JsonUtility.FromJson<ConfigCenterIndex>(File.ReadAllText(IndexPath)) ?? new ConfigCenterIndex(); }
            catch (Exception exception) { Debug.LogWarning($"ConfigKit 索引无法读取，将重新扫描：{exception.Message}"); return new ConfigCenterIndex(); }
        }

        /// <summary>扫描所有配置根目录并完整重建索引。</summary>
        public static ConfigCenterIndex Rebuild()
        {
            ConfigCenterIndex index = new ConfigCenterIndex();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", ScanRoots.Where(AssetDatabase.IsValidFolder).ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!visited.Add(path) || !IsIncluded(path)) continue;
                ScriptableObject asset = AssetDatabase.LoadMainAssetAtPath(path) as ScriptableObject;
                if (asset == null) continue;
                index.entries.Add(CreateEntry(guid, path, asset));
            }
            index.entries = index.entries.OrderBy(entry => entry.groupPath, StringComparer.Ordinal).ThenBy(entry => entry.displayName, StringComparer.Ordinal).ToList();
            Save(index);
            return index;
        }

        /// <summary>将索引写入 Library 派生目录，不把扫描结果提交到版本库。</summary>
        public static void Save(ConfigCenterIndex index)
        {
            string directory = Path.GetDirectoryName(IndexPath).Replace('\\', '/');
            Directory.CreateDirectory(directory);
            File.WriteAllText(IndexPath, JsonUtility.ToJson(index, true));
        }

        /// <summary>判断资产路径是否位于配置中心允许的项目目录并排除测试与第三方内容。</summary>
        public static bool IsIncluded(string path)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return false;
            if (!ScanRoots.Any(root => path.StartsWith(root.EndsWith("/") ? root : root + "/", StringComparison.OrdinalIgnoreCase))) return false;
            return !ExcludedFragments.Any(fragment => path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>根据配置资产路径取得其所属扫描根目录；索引条目必然来自扫描白名单，因此必须匹配到一个根目录。</summary>
        internal static string GetRootPath(string path) { return ScanRoots.Where(root => path.StartsWith(root.EndsWith("/") ? root : root + "/", StringComparison.OrdinalIgnoreCase)).OrderByDescending(root => root.Length).FirstOrDefault(); }

        /// <summary>根据显式 Attribute、目录层级和类型名生成一条稳定索引记录。</summary>
        private static ConfigCenterEntry CreateEntry(string guid, string path, ScriptableObject asset)
        {
            Type type = asset.GetType();
            ConfigCenterGroupAttribute group = type.GetCustomAttribute<ConfigCenterGroupAttribute>();
            ConfigCenterDisplayNameAttribute displayName = type.GetCustomAttribute<ConfigCenterDisplayNameAttribute>();
            string groupPath = group != null && !string.IsNullOrWhiteSpace(group.GroupPath) ? group.GroupPath.Trim('/') : DeriveGroupPath(path, type);
            return new ConfigCenterEntry { guid = guid, assetPath = path, assetName = asset.name, typeName = type.Name, fullTypeName = type.FullName, groupPath = groupPath, displayName = displayName != null && !string.IsNullOrWhiteSpace(displayName.DisplayName) ? displayName.DisplayName : asset.name, isThirdParty = path.IndexOf("/Plugins/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("/Trd/", StringComparison.OrdinalIgnoreCase) >= 0 };
        }

        /// <summary>从配置目录的相对路径推导没有显式分组声明的资产分组。</summary>
        private static string DeriveGroupPath(string path, Type type)
        {
            string root = GetRootPath(path);
            string relative = path.Substring(root.Length + 1);
            string directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
            string[] parts = directory.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Where(part => !string.Equals(part, "Config", StringComparison.OrdinalIgnoreCase) && !string.Equals(part, "Configs", StringComparison.OrdinalIgnoreCase)).ToArray();
            return parts.Length == 0 ? type.Name : string.Join("/", parts);
        }
    }

    /// <summary>配置中心索引文件的根对象。</summary>
    [Serializable]
    internal sealed class ConfigCenterIndex
    {
        /// <summary>当前扫描得到的所有配置条目。</summary>
        public List<ConfigCenterEntry> entries = new List<ConfigCenterEntry>();
    }

    /// <summary>配置中心中的单个 ScriptableObject 定位记录。</summary>
    [Serializable]
    internal sealed class ConfigCenterEntry
    {
        public string guid;
        public string assetPath;
        public string assetName;
        public string typeName;
        public string fullTypeName;
        public string groupPath;
        public string displayName;
        public bool isThirdParty;
    }

    /// <summary>EditorPrefs 中序列化的配置根目录列表容器。</summary>
    [Serializable]
    internal sealed class RootPathList
    {
        public List<string> paths = new List<string>();
    }
}
