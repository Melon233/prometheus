using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.XAsset.Editor
{
    public enum CollectorFilter
    {
        All,
        Prefab,
        ScriptableObject,
        Texture,
        Material,
        AudioClip,
        Scene
    }

    public enum AddressRule
    {
        FullPath,
        FileName,
        GroupAndFileName
    }

    public enum PackRule
    {
        PackTogether,
        PackSeparately,
        PackByFirstDirectory
    }

    [Serializable]
    public sealed class CollectorConfig
    {
        public string collectorName = "Collector";
        public string collectPath = "Assets/BundleResources";
        public CollectorFilter filter = CollectorFilter.All;
        public AddressRule addressRule = AddressRule.GroupAndFileName;
        public PackRule packRule = PackRule.PackSeparately;
        public bool includeEditorAssets;
    }

    [Serializable]
    public sealed class CollectorGroupConfig
    {
        public string groupName = "DefaultGroup";
        public List<CollectorConfig> collectors = new List<CollectorConfig>();
    }

    [Serializable]
    public sealed class CollectorPackageConfig
    {
        public string packageName = "DefaultPackage";
        public List<CollectorGroupConfig> groups = new List<CollectorGroupConfig>();
    }

    [CreateAssetMenu(
        fileName = "XAssetCollectorSettings",
        menuName = "Prometheus/XAsset/Collector Settings")]
    public sealed class CollectorSettings : ScriptableObject
    {
        public string outputFolder =
            "Assets/Prometheus/Framework/XAsset/Generated";
        public List<CollectorPackageConfig> packages =
            new List<CollectorPackageConfig>();
    }
}
