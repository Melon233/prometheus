using System;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.XAsset.Editor
{
    [InitializeOnLoad]
    internal static class EditorBackendRegistration
    {
        private const string DefaultManifestPath =
            "Assets/Prometheus/Framework/XAsset/Generated/defaultpackage.asset";

        static EditorBackendRegistration()
        {
            XAssets.SetBackendFactory(() => new EditorAssetDatabaseBackend());
            XAssets.SetDefaultManifestProvider(() =>
                AssetDatabase.LoadAssetAtPath<AssetManifest>(
                    DefaultManifestPath));
        }
    }

    public sealed class EditorAssetDatabaseBackend : IAssetBackend
    {
        public int SyncLoadCount { get; private set; }
        public int AsyncLoadCount { get; private set; }
        public int UnloadCount { get; private set; }

        public UnityEngine.Object LoadSync(XAssetInfo assetInfo)
        {
            if (assetInfo == null)
                throw new ArgumentNullException(nameof(assetInfo));

            SyncLoadCount++;
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                assetInfo.AssetPath);
        }

        public void LoadAsync(
            XAssetInfo assetInfo,
            Action<UnityEngine.Object, Exception> completed)
        {
            if (assetInfo == null)
                throw new ArgumentNullException(nameof(assetInfo));
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));

            AsyncLoadCount++;

            EditorApplication.CallbackFunction update = null;
            update = () =>
            {
                EditorApplication.update -= update;

                try
                {
                    UnityEngine.Object asset =
                        AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                            assetInfo.AssetPath);
                    completed(asset, null);
                }
                catch (Exception exception)
                {
                    completed(null, exception);
                }
            };

            EditorApplication.update += update;
        }

        public void Unload(
            XAssetInfo assetInfo,
            UnityEngine.Object asset)
        {
            // AssetDatabase owns the real Editor memory lifecycle. For this MVP,
            // unloading means releasing the provider's logical cache entry.
            UnloadCount++;
        }
    }
}
