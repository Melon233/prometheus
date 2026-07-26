using System;
using UnityEngine;

namespace Xuan.Prometheus.XAsset
{
    public enum AssetProviderState
    {
        None,
        Loading,
        Succeeded,
        Failed,
        Released
    }

    internal sealed class AssetProvider
    {
        private readonly IAssetBackend backend;
        private readonly Action<AssetProvider> onUnused;

        internal event Action<AssetProvider> Completed;

        internal XAssetInfo AssetInfo { get; }
        internal UnityEngine.Object AssetObject { get; private set; }
        internal Exception Error { get; private set; }
        internal AssetProviderState State { get; private set; }
        internal int ReferenceCount { get; private set; }

        internal bool IsDone =>
            State == AssetProviderState.Succeeded ||
            State == AssetProviderState.Failed;

        internal AssetProvider(
            XAssetInfo assetInfo,
            IAssetBackend backend,
            Action<AssetProvider> onUnused)
        {
            AssetInfo = assetInfo ?? throw new ArgumentNullException(nameof(assetInfo));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.onUnused = onUnused ?? throw new ArgumentNullException(nameof(onUnused));
        }

        internal void AddReference()
        {
            if (State == AssetProviderState.Released)
                throw new ObjectDisposedException(nameof(AssetProvider));

            ReferenceCount++;
        }

        internal void RemoveReference()
        {
            if (ReferenceCount <= 0)
                throw new InvalidOperationException(
                    $"Asset provider '{AssetInfo.Address}' has no references to release.");

            ReferenceCount--;

            if (ReferenceCount == 0 && IsDone)
                onUnused(this);
        }

        internal void LoadSync()
        {
            if (IsDone || State == AssetProviderState.Released)
                return;

            try
            {
                UnityEngine.Object result = backend.LoadSync(AssetInfo);
                Complete(result, null);
            }
            catch (Exception exception)
            {
                Complete(null, exception);
            }
        }

        internal void LoadAsync()
        {
            if (State != AssetProviderState.None)
                return;

            State = AssetProviderState.Loading;

            try
            {
                backend.LoadAsync(AssetInfo, Complete);
            }
            catch (Exception exception)
            {
                Complete(null, exception);
            }
        }

        internal void Unload()
        {
            if (State == AssetProviderState.Released)
                return;

            UnityEngine.Object asset = AssetObject;
            AssetObject = null;
            State = AssetProviderState.Released;
            Completed = null;

            if (asset != null)
                backend.Unload(AssetInfo, asset);
        }

        private void Complete(UnityEngine.Object asset, Exception exception)
        {
            if (IsDone || State == AssetProviderState.Released)
                return;

            if (exception == null && asset == null)
            {
                exception = new InvalidOperationException(
                    $"Backend returned null for asset '{AssetInfo.AssetPath}'.");
            }

            AssetObject = asset;
            Error = exception;
            State = exception == null
                ? AssetProviderState.Succeeded
                : AssetProviderState.Failed;

            Action<AssetProvider> completed = Completed;
            completed?.Invoke(this);

            if (ReferenceCount == 0)
                onUnused(this);
        }
    }
}
