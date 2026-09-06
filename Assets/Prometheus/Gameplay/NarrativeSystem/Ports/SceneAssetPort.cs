using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>场景资源登记表中的一项。</summary>
    [Serializable]
    public sealed class SceneAssetEntry
    {
        [SerializeField] [Tooltip("剧情中引用的资源地址。")] private string location;
        [SerializeField] [Tooltip("对应的资源对象。")] private UnityEngine.Object asset;

        /// <summary>获取资源地址。</summary>
        public string Location => location;

        /// <summary>获取资源对象。</summary>
        public UnityEngine.Object Asset => asset;
    }

    /// <summary>
    /// 基于场景登记表的资源端口。
    /// <para>
    /// 资源在 Inspector 中直接引用，加载是同步返回的，因此不需要接入 YooAsset 即可运行。
    /// 正式流程应换成包装 <c>IAssetKit</c> 的实现，以满足设计文档 §16 对按需加载与释放的要求；
    /// 本端口服务于测试场景，同时也充当「已加载资源可被同步读取」这一契约的参考实现。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneAssetPort : MonoBehaviour, INarrativeAssetPort
    {
        [SerializeField] [Tooltip("可供剧情按地址引用的资源。")] private List<SceneAssetEntry> assets = new List<SceneAssetEntry>();

        /// <inheritdoc />
        public UniTask<TAsset> LoadAsync<TAsset>(string location, CancellationToken cancellationToken) where TAsset : UnityEngine.Object
        {
            if (!TryGet(location, out TAsset asset)) throw new InvalidOperationException($"SceneAssetPort on '{name}' has no entry for location '{location}' of type {typeof(TAsset).Name}.");
            return UniTask.FromResult(asset);
        }

        /// <inheritdoc />
        public bool TryGet<TAsset>(string location, out TAsset asset) where TAsset : UnityEngine.Object
        {
            for (int index = 0; index < assets.Count; index++)
            {
                SceneAssetEntry entry = assets[index];
                if (entry == null || !string.Equals(entry.Location, location, StringComparison.Ordinal)) continue;
                asset = entry.Asset as TAsset;
                // 允许按 UnityEngine.Object 预加载任意类型，同时保证按具体类型读取时类型正确。
                if (asset != null) return true;
            }
            asset = null;
            return false;
        }

        /// <inheritdoc />
        public void Release(string location)
        {
            // 场景登记的资源由场景本身持有引用，无需按地址释放。
        }

        /// <inheritdoc />
        public void ReleaseAll()
        {
            // 场景登记的资源由场景本身持有引用，无需统一释放。
        }
    }
}
