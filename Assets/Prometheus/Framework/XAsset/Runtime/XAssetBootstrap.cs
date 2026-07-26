using System;
using UnityEngine;

namespace Xuan.Prometheus.XAsset
{
    [DisallowMultipleComponent]
    public sealed class XAssetBootstrap : MonoBehaviour
    {
        [SerializeField] private AssetManifest manifest;
        [SerializeField] private bool setAsDefaultPackage = true;

        private bool ownsPackage;

        public ResourcePackage Package { get; private set; }

        private void Awake()
        {
            if (manifest == null)
                throw new InvalidOperationException("XAssetBootstrap manifest is not assigned.");

            if (XAssets.TryGetPackage(manifest.PackageName, out ResourcePackage existing))
            {
                Package = existing;
                return;
            }

            Package = XAssets.CreatePackage(manifest, setAsDefaultPackage);
            ownsPackage = true;
        }

        private void OnDestroy()
        {
            if (!ownsPackage || Package == null)
                return;

            if (Package.ProviderCount != 0)
            {
                Debug.LogWarning(
                    $"XAsset package '{Package.PackageName}' still has " +
                    $"{Package.ProviderCount} provider(s) during bootstrap shutdown. " +
                    "Release all handles before leaving Play Mode.");
                return;
            }

            XAssets.RemovePackage(Package.PackageName);
            Package = null;
            ownsPackage = false;
        }
    }
}
