using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Xuan.Prometheus.XAsset
{
    public sealed class ResourcePackage : IDisposable
    {
        private readonly IAssetBackend backend;
        private readonly Dictionary<string, XAssetInfo> locationMap =
            new Dictionary<string, XAssetInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, AssetProvider> providers =
            new Dictionary<string, AssetProvider>(StringComparer.Ordinal);
        private bool disposed;

        public string PackageName { get; }
        public AssetManifest Manifest { get; }
        public int ProviderCount => providers.Count;
        public int ActiveReferenceCount => providers.Values.Sum(item => item.ReferenceCount);

        public ResourcePackage(AssetManifest manifest, IAssetBackend backend)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));

            if (string.IsNullOrWhiteSpace(manifest.PackageName))
                throw new ArgumentException("Manifest package name is empty.", nameof(manifest));

            PackageName = manifest.PackageName;
            BuildLocationMap(manifest);
        }

        public AssetHandle<T> LoadAssetSync<T>(string location)
            where T : UnityEngine.Object
        {
            EnsureNotDisposed();
            XAssetInfo assetInfo = Resolve(location);
            AssetProvider provider = GetOrCreateProvider(assetInfo);
            var handle = new AssetHandle<T>(provider);
            provider.LoadSync();
            return handle;
        }

        public AssetHandle<T> LoadAssetAsync<T>(string location)
            where T : UnityEngine.Object
        {
            EnsureNotDisposed();
            XAssetInfo assetInfo = Resolve(location);
            AssetProvider provider = GetOrCreateProvider(assetInfo);
            var handle = new AssetHandle<T>(provider);
            provider.LoadAsync();
            return handle;
        }

        public bool TryGetAssetInfo(string location, out XAssetInfo assetInfo)
        {
            assetInfo = null;
            return !string.IsNullOrWhiteSpace(location) &&
                   locationMap.TryGetValue(NormalizeLocation(location), out assetInfo);
        }

        public int GetReferenceCount(string location)
        {
            XAssetInfo assetInfo = Resolve(location);
            return providers.TryGetValue(assetInfo.Guid, out AssetProvider provider)
                ? provider.ReferenceCount
                : 0;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (providers.Count != 0)
            {
                throw new InvalidOperationException(
                    $"Package '{PackageName}' still has {providers.Count} provider(s) " +
                    $"and {ActiveReferenceCount} active reference(s).");
            }

            disposed = true;
            locationMap.Clear();
        }

        private void BuildLocationMap(AssetManifest manifest)
        {
            foreach (XAssetInfo assetInfo in manifest.Assets)
            {
                if (assetInfo == null)
                    throw new InvalidOperationException("Manifest contains a null asset entry.");

                RegisterLocation(assetInfo.Address, assetInfo);
                RegisterLocation(assetInfo.AssetPath, assetInfo);
                RegisterLocation(assetInfo.Guid, assetInfo);
            }
        }

        private void RegisterLocation(string location, XAssetInfo assetInfo)
        {
            string normalized = NormalizeLocation(location);

            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException(
                    $"Asset '{assetInfo.AssetPath}' contains an empty location.");

            if (locationMap.TryGetValue(normalized, out XAssetInfo existing))
            {
                if (!string.Equals(existing.Guid, assetInfo.Guid, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Location '{normalized}' points to both '{existing.AssetPath}' " +
                        $"and '{assetInfo.AssetPath}'.");
                }

                return;
            }

            locationMap.Add(normalized, assetInfo);
        }

        private XAssetInfo Resolve(string location)
        {
            if (TryGetAssetInfo(location, out XAssetInfo assetInfo))
                return assetInfo;

            throw new KeyNotFoundException(
                $"Package '{PackageName}' does not contain location '{location}'.");
        }

        private AssetProvider GetOrCreateProvider(XAssetInfo assetInfo)
        {
            if (providers.TryGetValue(assetInfo.Guid, out AssetProvider provider))
                return provider;

            provider = new AssetProvider(assetInfo, backend, OnProviderUnused);
            providers.Add(assetInfo.Guid, provider);
            return provider;
        }

        private void OnProviderUnused(AssetProvider provider)
        {
            if (provider.ReferenceCount != 0 || !provider.IsDone)
                return;

            if (!providers.TryGetValue(provider.AssetInfo.Guid, out AssetProvider cached) ||
                cached != provider)
            {
                return;
            }

            providers.Remove(provider.AssetInfo.Guid);
            provider.Unload();
        }

        private void EnsureNotDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ResourcePackage));
        }

        private static string NormalizeLocation(string location)
        {
            return location?.Trim().Replace('\\', '/');
        }
    }
}
