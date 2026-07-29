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

        private void OnTriggerEnter(Collider other)
        {
            handler.OnTriggerEnter(other);
        }
    }
}