using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(fileName = "EnemySpawnConfig", menuName = "Prometheus/EnemySpawnConfig")]
    public class EnemySpawnConfig : ScriptableObject
    {
        public List<Transform> spawnPoints;
    }
}