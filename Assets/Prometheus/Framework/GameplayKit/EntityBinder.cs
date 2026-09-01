using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>作为 Entity Prefab 根节点唯一的 Unity 引用容器，集中保存表现引用、目标碰撞代理并提供显式完整性校验。</summary>
    [DisallowMultipleComponent]
    public abstract class EntityBinder : MonoBehaviour
    {
        [SerializeField] private ColliderProxy[] entityColliderProxies = Array.Empty<ColliderProxy>();

        /// <summary>获取全部可作为物理目标的碰撞代理；每个代理在初始化后直接持有宿主 Entity。</summary>
        public ColliderProxy[] EntityColliderProxies => entityColliderProxies;

        /// <summary>校验目标代理集合后一次性写入宿主 Entity，使碰撞回调不再依赖 EntitySystem 反向索引。</summary>
        internal void BindHost(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            HashSet<ColliderProxy> uniqueProxies = new HashSet<ColliderProxy>();
            for (int index = 0; index < entityColliderProxies.Length; index++)
            {
                ColliderProxy proxy = entityColliderProxies[index];
                if (proxy == null) throw new InvalidOperationException($"EntityBinder '{name}' contains a null target ColliderProxy at index {index}.");
                if (!uniqueProxies.Add(proxy)) throw new InvalidOperationException($"EntityBinder '{name}' contains duplicate target ColliderProxy '{proxy.name}'.");
                if (proxy.HostEntity != null) throw new InvalidOperationException($"ColliderProxy '{proxy.name}' is already bound to Entity {proxy.HostEntity.EntityId}.");
            }
            foreach (ColliderProxy proxy in entityColliderProxies) proxy.BindHost(entity);
        }

        /// <summary>在表现对象释放前解除全部目标代理的宿主引用，保持场景复用对象生命周期闭合。</summary>
        internal void UnbindHost(Entity entity)
        {
            foreach (ColliderProxy proxy in entityColliderProxies) proxy.UnbindHost(entity);
        }

        /// <summary>校验当前 Binder 是否满足对应 Entity 的全部必需引用约束。</summary>
        public abstract void Validate();
    }
}
