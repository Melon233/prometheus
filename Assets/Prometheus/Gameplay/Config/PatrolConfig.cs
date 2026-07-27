using UnityEngine;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(menuName = "Xuan/Prometheus/PatrolConfig")]
    public class PatrolConfig : ScriptableObject
    {
        public float patrolRadius = 5f;
        public float patrolSpeed = 2f;
        public float moveDelta = 3f;
        public float moveInterval = 3f;
        public float patrolFrequency = 2f;
    }
}