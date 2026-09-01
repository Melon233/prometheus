using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class PatrolComponent : Xuan.Prometheus.Component.Component
    {
        public PatrolConfig patrolConfig;
        public CharacterController cc;
        public Vector3 spawnPoint;
        public Vector3 nextTargetPoint;
        // public Timer patrolTimer;
        public bool isPatrolling;
        public void Execute()
        {
            var angle = Random.Range(0f, Mathf.PI * 2f);
            while (Vector3.Distance(nextTargetPoint = Entity.bindGo.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * patrolConfig.moveDelta, spawnPoint) > patrolConfig.patrolRadius) angle = Random.Range(0f, Mathf.PI * 2f);
            isPatrolling = true;
            // patrolTimer.Reset();
        }
        public void Interrupt()
        {
            isPatrolling = false;
            // patrolTimer.SetLeftTime(0f);
        }
    }
}
