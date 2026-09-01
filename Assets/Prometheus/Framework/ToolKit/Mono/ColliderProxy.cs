using System;
using UnityEngine;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Component
{
    /// <summary>接收触发进入/离开回调及其来源代理，使命中盒与交互感应能验证当前实际生效的碰撞体。</summary>
    public interface ITriggerHandler
    {
        /// <summary>接收触发进入回调及其来源代理。</summary>
        void OnTriggerEnter(ColliderProxy source, Collider other);

        /// <summary>接收触发离开回调及其来源代理。</summary>
        void OnTriggerExit(ColliderProxy source, Collider other);
    }

    /// <summary>碰撞代理：保存初始化阶段绑定的宿主 Entity，并把触发器进入/离开转发给当前处理器。</summary>
    [RequireComponent(typeof(Collider))]
    public class ColliderProxy : MonoBehaviour
    {
        [NonSerialized] public Collider cod;
        [NonSerialized] public ITriggerHandler handler;

        /// <summary>获取 GameObjectLogic 初始化阶段写入的宿主 Entity，供碰撞接收方直接解析目标。</summary>
        public Entity HostEntity { get; private set; }

        private void Awake()
        {
            cod = GetComponent<Collider>();
        }

        /// <summary>由根 EntityBinder 在表现初始化阶段绑定唯一宿主，禁止一个碰撞代理跨 Entity 复用。</summary>
        internal void BindHost(Entity entity)
        {
            if (HostEntity != null) throw new InvalidOperationException($"ColliderProxy '{name}' is already bound to Entity {HostEntity.EntityId}.");
            HostEntity = entity ?? throw new ArgumentNullException(nameof(entity));
        }

        /// <summary>由根 EntityBinder 在表现释放阶段解除宿主，避免场景复用对象继续暴露已释放 Entity。</summary>
        internal void UnbindHost(Entity entity)
        {
            if (!ReferenceEquals(HostEntity, entity)) throw new InvalidOperationException($"ColliderProxy '{name}' is not bound to the requested Entity.");
            HostEntity = null;
        }

        /// <summary>从物理回调给出的 Collider 获取同节点代理所持有的宿主 Entity。</summary>
        public static bool TryGetHostEntity(Collider collider, out Entity entity)
        {
            entity = null;
            if (collider == null || !collider.TryGetComponent(out ColliderProxy proxy)) return false;
            entity = proxy.HostEntity;
            return entity != null;
        }

        private void OnTriggerEnter(Collider other)
        {
            handler?.OnTriggerEnter(this, other);
        }

        private void OnTriggerExit(Collider other)
        {
            handler?.OnTriggerExit(this, other);
        }
    }
}
