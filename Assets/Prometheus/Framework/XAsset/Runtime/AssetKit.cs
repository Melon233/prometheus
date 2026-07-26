using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xuan.Prometheus.XAsset;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Compatibility facade for the original framework entry points. New code
    /// should keep and release the generic handles returned by this class.
    /// </summary>
    public interface IAssetKit
    {
        void LoadAssetSync(string location);
        void LoadSceneSync(string location);
        void InstantiateAsset(string location);
    }

    public sealed class AssetKit : Kit, IAssetKit
    {
        public ResourcePackage Package { get; private set; }

        public AssetKit()
        {
        }

        public AssetKit(ResourcePackage package)
        {
            Initialize(package);
        }

        public void Initialize(ResourcePackage package)
        {
            Package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public AssetHandle<T> LoadAssetSync<T>(string location)
            where T : UnityEngine.Object
        {
            return GetPackage().LoadAssetSync<T>(location);
        }

        public AssetHandle<T> LoadAssetAsync<T>(string location)
            where T : UnityEngine.Object
        {
            return GetPackage().LoadAssetAsync<T>(location);
        }

        void IAssetKit.LoadAssetSync(string location)
        {
            using (AssetHandle<UnityEngine.Object> handle =
                   LoadAssetSync<UnityEngine.Object>(location))
            {
                UnityEngine.Object ignored = handle.Asset;
            }
        }

        public void LoadSceneSync(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                throw new ArgumentException("Scene location is empty.", nameof(location));

            SceneManager.LoadScene(location, LoadSceneMode.Single);
        }

        public void InstantiateAsset(string location)
        {
            using (AssetHandle<GameObject> handle =
                   LoadAssetSync<GameObject>(location))
            {
                UnityEngine.Object.Instantiate(handle.Asset);
            }
        }

        private ResourcePackage GetPackage()
        {
            if (Package != null)
                return Package;

            if (XAssets.DefaultPackage != null)
                return XAssets.DefaultPackage;

            throw new InvalidOperationException(
                "AssetKit is not initialized and XAssets has no default package.");
        }
    }
}
