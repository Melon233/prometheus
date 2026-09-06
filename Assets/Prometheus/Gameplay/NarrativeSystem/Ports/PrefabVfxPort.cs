using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 按预制体生成剧情特效的端口实现。
    /// 生成的实例统一挂在本组件之下，便于在舞台退出或场景卸载时整体回收。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrefabVfxPort : MonoBehaviour, INarrativeVfxPort
    {
        [SerializeField] [Tooltip("解析特效预制体使用的资源端口；留空则在同一对象上查找。")] private MonoBehaviour assetPortSource;

        /// <summary>保存当前存活的特效实例。</summary>
        private readonly List<VfxInstance> instances = new List<VfxInstance>();

        private INarrativeAssetPort assetPort;

        /// <summary>解析资源端口来源。</summary>
        private void Awake()
        {
            assetPort = assetPortSource as INarrativeAssetPort ?? GetComponent<INarrativeAssetPort>();
        }

        /// <inheritdoc />
        public async UniTask<INarrativeVfxHandle> SpawnAsync(string location, Vector3 position, Quaternion rotation, Transform parent, CancellationToken cancellationToken)
        {
            if (assetPort == null) throw new InvalidOperationException($"PrefabVfxPort on '{name}' requires an {nameof(INarrativeAssetPort)}.");
            GameObject prefab = await assetPort.LoadAsync<GameObject>(location, cancellationToken);
            if (prefab == null) throw new InvalidOperationException($"Vfx prefab '{location}' could not be loaded.");
            GameObject spawned = Instantiate(prefab, position, rotation, parent != null ? parent : transform);
            VfxInstance instance = new VfxInstance(location, spawned);
            instances.Add(instance);
            return instance;
        }

        /// <inheritdoc />
        public void Stop(INarrativeVfxHandle handle)
        {
            if (!(handle is VfxInstance instance)) return;
            instances.Remove(instance);
            instance.Destroy();
        }

        /// <inheritdoc />
        public void StopAll()
        {
            for (int index = 0; index < instances.Count; index++) instances[index].Destroy();
            instances.Clear();
        }

        /// <summary>场景卸载时回收全部特效实例。</summary>
        private void OnDestroy()
        {
            StopAll();
        }

        /// <summary>一个已生成特效实例的句柄。</summary>
        private sealed class VfxInstance : INarrativeVfxHandle
        {
            private GameObject target;

            /// <summary>创建一个特效实例句柄。</summary>
            internal VfxInstance(string location, GameObject target)
            {
                Location = location;
                this.target = target;
            }

            /// <inheritdoc />
            public string Location { get; }

            /// <inheritdoc />
            public bool IsAlive => target != null;

            /// <summary>销毁实例；重复调用保持幂等。</summary>
            internal void Destroy()
            {
                if (target == null) return;
                StageScope.DestroyObject(target);
                target = null;
            }
        }
    }
}
