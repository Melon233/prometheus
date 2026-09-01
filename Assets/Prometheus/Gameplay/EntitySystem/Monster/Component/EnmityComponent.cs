using UnityEngine;

namespace Xuan.Prometheus
{
    public class EnmityComponent : Component.Component
    {
        public EnmityConfig enmityConfig;
        public Transform target; // The target that the enemy is chasing
        public bool needGoHome;
        public bool CheckEnmity()
        {
            var cods = Physics.OverlapSphere(Entity.bindGo.transform.position, enmityConfig.enmityRadius, LayerMask.GetMask("Character"), QueryTriggerInteraction.UseGlobal);
            if (cods.Length > 0)
            {
                target = cods[0].transform;
                return true;
            }
            target = null; // If no target is found, set the target to null
            return false;
        }
    }
}
