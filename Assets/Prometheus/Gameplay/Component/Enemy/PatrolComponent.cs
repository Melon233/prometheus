using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class PatrolComponent : MonoComponent
    {
        public PatrolConfig patrolConfig;
        public CharacterController cc;
        public Vector3 spawnPoint;
        public Vector3 nextTargetPoint;
        public Timer moveTimer;
        public bool isPatrolling = false;
    }
}