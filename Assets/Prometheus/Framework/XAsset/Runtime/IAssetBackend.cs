using System;
using UnityEngine;

namespace Xuan.Prometheus.XAsset
{
    public interface IAssetBackend
    {
        UnityEngine.Object LoadSync(XAssetInfo assetInfo);

        void LoadAsync(
            XAssetInfo assetInfo,
            Action<UnityEngine.Object, Exception> completed);

        void Unload(XAssetInfo assetInfo, UnityEngine.Object asset);
    }
}
