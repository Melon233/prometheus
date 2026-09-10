using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 运行时特效端口：按地址加载预制体并生成实例。
    ///
    /// 与场景版 <see cref="PrefabVfxPort"/> 的逻辑相同，区别只在它不是 MonoBehaviour——
    /// 运行时端口由玩法流程构造，没有可以承载它的场景组件，回收根节点因此显式传入。
    /// </summary>
    internal sealed class NarrativeVfxPort : INarrativeVfxPort
    {
        /// <summary>解析特效预制体使用的资源端口。</summary>
        private readonly INarrativeAssetPort assetPort;

        /// <summary>未指定父节点时挂载特效实例的兜底根节点。</summary>
        private readonly Transform defaultParent;

        /// <summary>保存当前存活的特效实例。</summary>
        private readonly List<VfxInstance> instances = new List<VfxInstance>();

        /// <summary>创建一个按地址加载预制体的特效端口。</summary>
        /// <param name="assetPort">用于加载预制体的资源端口。</param>
        /// <param name="defaultParent">调用方未指定父节点时使用的挂载根节点。</param>
        internal NarrativeVfxPort(INarrativeAssetPort assetPort, Transform defaultParent)
        {
            this.assetPort = assetPort ?? throw new ArgumentNullException(nameof(assetPort));
            this.defaultParent = defaultParent;
        }

        /// <inheritdoc />
        public async UniTask<INarrativeVfxHandle> SpawnAsync(string location, Vector3 position, Quaternion rotation, Transform parent, CancellationToken cancellationToken)
        {
            GameObject prefab = await assetPort.LoadAsync<GameObject>(location, cancellationToken);
            GameObject spawned = UnityEngine.Object.Instantiate(prefab, position, rotation, parent != null ? parent : defaultParent);
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
