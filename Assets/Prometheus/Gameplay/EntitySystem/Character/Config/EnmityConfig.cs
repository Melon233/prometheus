using UnityEngine;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(menuName = "Xuan/Prometheus/EnmityConfig")]
    public class EnmityConfig : ScriptableObject
    {
        public float enmityRadius;
        public float chaseRadius;
        public float chaseSpeed;
    }
}