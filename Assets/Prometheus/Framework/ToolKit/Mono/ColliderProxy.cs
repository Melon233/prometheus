using System;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public interface ITriggerHandler
    {
        /// <summary>接收触发回调及其来源代理，使多命中盒行为能够验证当前实际生效的碰撞体。</summary>
        void OnTriggerEnter(ColliderProxy source, Collider other);
    }

    [RequireComponent(typeof(Collider))]
    public class ColliderProxy : MonoBehaviour
    {
        [NonSerialized] public Collider cod;
        public ITriggerHandler handler;

        private void Awake()
        {
            cod = GetComponent<Collider>();
        }

        /// <summary>把命中转发给当前运行时处理器；Entity 回收已经解绑处理器时安全忽略迟到的物理回调。</summary>
        private void OnTriggerEnter(Collider other)
        {
            handler?.OnTriggerEnter(this, other);
        }
    }
}
