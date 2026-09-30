using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 走 AssetKit 的运行时资源端口。
    ///
    /// 按地址异步加载，并登记本次演出加载过的全部地址，退出时统一释放——这是设计文档
    /// §16「剧情资源一律按地址加载、句柄登记在作用域、退出时统一释放」的落地实现。
    /// 与场景登记表版本（<see cref="SceneAssetPort"/>）的区别只在资源从哪来，契约完全一致。
    /// </summary>
    internal sealed class AssetKitPort : INarrativeAssetPort
    {
        /// <summary>保存本端口已加载的资源，供落终态同步读取与统一释放。</summary>
        private readonly Dictionary<string, UnityEngine.Object> loaded = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        /// <inheritdoc />
        public async UniTask<TAsset> LoadAsync<TAsset>(string location, CancellationToken cancellationToken) where TAsset : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("Narrative asset location cannot be empty.", nameof(location));
            if (TryGet(location, out TAsset cached)) return cached;

            TAsset asset = null;
            await Core.Asset.LoadAssetAsync<TAsset>(location, result => asset = result, error => throw new InvalidOperationException($"Narrative asset '{location}' failed to load: {error}")).ToUniTask();
            if (asset == null) throw new InvalidOperationException($"Narrative asset '{location}' resolved to null for type {typeof(TAsset).Name}.");

            // 先登记再检查取消：取消路径下资源已经在表里，仍然会被 ReleaseAll 正常释放。
            loaded[location] = asset;
            cancellationToken.ThrowIfCancellationRequested();
            return asset;
        }

        /// <inheritdoc />
        public bool TryGet<TAsset>(string location, out TAsset asset) where TAsset : UnityEngine.Object
        {
            if (loaded.TryGetValue(location, out UnityEngine.Object cached))
            {
                asset = cached as TAsset;
                if (asset != null) return true;
            }
            asset = null;
            return false;
        }

        /// <inheritdoc />
        public void Release(string location)
        {
            if (!loaded.Remove(location)) return;
            Core.Asset.ReleaseAsset(location);
        }

        /// <inheritdoc />
        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, UnityEngine.Object> pair in loaded) Core.Asset.ReleaseAsset(pair.Key);
            loaded.Clear();
        }
    }
}
