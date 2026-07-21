using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public interface ICollisionHandler
    {
        void OnTriggerEnter(Collider other);
    }

    [RequireComponent(typeof(Collider))]
    public class ColliderProxy : MonoBehaviour
    {
        public Collider cod;
        public ICollisionHandler handler;

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