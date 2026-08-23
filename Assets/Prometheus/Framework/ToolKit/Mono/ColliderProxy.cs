using System;
using UnityEngine;

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

    /// <summary>碰撞代理：把触发器进入/离开转发给当前绑定的处理器；处理器解绑后安全忽略迟到的物理回调。</summary>
    [RequireComponent(typeof(Collider))]
    public class ColliderProxy : MonoBehaviour
    {
        [NonSerialized] public Collider cod;
        [NonSerialized] public ITriggerHandler handler;

        private void Awake()
        {
            cod = GetComponent<Collider>();
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
