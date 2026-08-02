using System;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public interface ITriggerHandler
    {
        void OnTriggerEnter(Collider other);
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
            handler?.OnTriggerEnter(other);
        }
    }
}
