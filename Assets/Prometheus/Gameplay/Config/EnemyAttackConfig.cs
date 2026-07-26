using UnityEngine;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(menuName = "Xuan/Prometheus/EnemyAttackConfig")]
    public class EnemyAttackConfig : ScriptableObject
    {
        public float attckRadius = 2f;
        public float attackInterval = 2f;
    }
}