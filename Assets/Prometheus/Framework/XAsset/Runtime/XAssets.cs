using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.XAsset
{
    public static class XAssets
    {
        private static readonly Dictionary<string, ResourcePackage> Packages =
            new Dictionary<string, ResourcePackage>(StringComparer.Ordinal);
        private static Func<IAssetBackend> backendFactory;
        private static Func<AssetManifest> defaultManifestProvider;

        public static ResourcePackage DefaultPackage { get; private set; }
        public static int PackageCount => Packages.Count;

        public static void SetBackendFactory(Func<IAssetBackend> factory)
        {
            backendFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static void SetDefaultManifestProvider(
            Func<AssetManifest> provider)
        {
            defaultManifestProvider =
                provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public static ResourcePackage CreateDefaultPackage()
        {
            if (DefaultPackage != null)
                return DefaultPackage;

            if (defaultManifestProvider == null)
            {
                throw new InvalidOperationException(
                    "XAssets has no default manifest provider.");
            }

            AssetManifest manifest = defaultManifestProvider();
            if (manifest == null)
            {
                throw new InvalidOperationException(
                    "XAsset default manifest provider returned null.");
            }

            return CreatePackage(manifest, setAsDefault: true);
        }

        public static ResourcePackage CreatePackage(
            AssetManifest manifest,
            bool setAsDefault = false)
        {
            if (backendFactory == null)
            {
                throw new InvalidOperationException(
                    "XAssets has no backend factory. In Editor, make sure the " +
                    "XAsset.Editor assembly is loaded.");
            }

            IAssetBackend backend = backendFactory();
            if (backend == null)
                throw new InvalidOperationException("XAsset backend factory returned null.");

            return CreatePackage(manifest, backend, setAsDefault);
        }

        public static ResourcePackage CreatePackage(
            AssetManifest manifest,
            IAssetBackend backend,
            bool setAsDefault = false)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            if (Packages.ContainsKey(manifest.PackageName))
            {
                throw new InvalidOperationException(
                    $"Package '{manifest.PackageName}' already exists.");
            }

            var package = new ResourcePackage(manifest, backend);
            Packages.Add(package.PackageName, package);

            if (setAsDefault || DefaultPackage == null)
                DefaultPackage = package;

            return package;
        }

        public static ResourcePackage GetPackage(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("Package name is empty.", nameof(packageName));

            if (Packages.TryGetValue(packageName, out ResourcePackage package))
                return package;

            throw new KeyNotFoundException($"Package '{packageName}' does not exist.");
        }

        public static bool TryGetPackage(
            string packageName,
            out ResourcePackage package)
        {
            package = null;
            return !string.IsNullOrWhiteSpace(packageName) &&
                   Packages.TryGetValue(packageName, out package);
        }

        public static bool RemovePackage(string packageName)
        {
            if (!Packages.TryGetValue(packageName, out ResourcePackage package))
                return false;

            package.Dispose();
            Packages.Remove(packageName);

            if (DefaultPackage == package)
                DefaultPackage = null;

            return true;
        }
    }
}
