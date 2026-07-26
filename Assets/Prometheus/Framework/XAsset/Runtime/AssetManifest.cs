using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.XAsset
{
    [Serializable]
    public sealed class XAssetInfo
    {
        [SerializeField] private string guid;
        [SerializeField] private string address;
        [SerializeField] private string assetPath;
        [SerializeField] private string typeName;
        [SerializeField] private string bundleId;
        [SerializeField] private List<string> dependencyGuids = new List<string>();

        public string Guid => guid;
        public string Address => address;
        public string AssetPath => assetPath;
        public string TypeName => typeName;
        public string BundleId => bundleId;
        public IReadOnlyList<string> DependencyGuids => dependencyGuids;

        public XAssetInfo(
            string guid,
            string address,
            string assetPath,
            string typeName,
            string bundleId,
            IEnumerable<string> dependencyGuids)
        {
            this.guid = guid;
            this.address = address;
            this.assetPath = assetPath;
            this.typeName = typeName;
            this.bundleId = bundleId;
            this.dependencyGuids = dependencyGuids == null
                ? new List<string>()
                : new List<string>(dependencyGuids);
        }
    }

    [Serializable]
    public sealed class VirtualBundleInfo
    {
        [SerializeField] private string bundleId;
        [SerializeField] private List<string> assetGuids = new List<string>();
        [SerializeField] private List<string> dependencyBundleIds = new List<string>();

        public string BundleId => bundleId;
        public IReadOnlyList<string> AssetGuids => assetGuids;
        public IReadOnlyList<string> DependencyBundleIds => dependencyBundleIds;

        public VirtualBundleInfo(
            string bundleId,
            IEnumerable<string> assetGuids,
            IEnumerable<string> dependencyBundleIds)
        {
            this.bundleId = bundleId;
            this.assetGuids = assetGuids == null
                ? new List<string>()
                : new List<string>(assetGuids);
            this.dependencyBundleIds = dependencyBundleIds == null
                ? new List<string>()
                : new List<string>(dependencyBundleIds);
        }
    }

    [CreateAssetMenu(
        fileName = "XAssetManifest",
        menuName = "Prometheus/XAsset/Manifest")]
    public sealed class AssetManifest : ScriptableObject
    {
        [SerializeField] private string packageName;
        [SerializeField] private List<XAssetInfo> assets = new List<XAssetInfo>();
        [SerializeField] private List<VirtualBundleInfo> bundles = new List<VirtualBundleInfo>();

        public string PackageName => packageName;
        public IReadOnlyList<XAssetInfo> Assets => assets;
        public IReadOnlyList<VirtualBundleInfo> Bundles => bundles;

        public void SetData(
            string newPackageName,
            IEnumerable<XAssetInfo> newAssets,
            IEnumerable<VirtualBundleInfo> newBundles)
        {
            packageName = newPackageName;
            assets = newAssets == null
                ? new List<XAssetInfo>()
                : new List<XAssetInfo>(newAssets);
            bundles = newBundles == null
                ? new List<VirtualBundleInfo>()
                : new List<VirtualBundleInfo>(newBundles);
        }
    }
}
