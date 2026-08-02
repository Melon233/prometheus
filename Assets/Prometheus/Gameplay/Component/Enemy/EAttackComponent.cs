using Spine;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EAttackComponent : Component.MonoComponent
    {
        public EnemyAttackConfig eAttackConfig;
        public Timer recoveryTimer;
        public PropertyComponent targetPropertyComp;
        public TrackEntry trackEntry;
        public bool isAttacking = false;
        public bool isRecovery = false;
        public float attackRecoveryTime = 2f;
        public Timer attackRecoveryTimer;
        public bool CheckAttack()
        {
            var cods = Physics.OverlapSphere(Entity.bindGo.transform.position, eAttackConfig.attckRadius, LayerMask.GetMask("Character"), QueryTriggerInteraction.UseGlobal);
            if (cods.Length > 0)
            {
                targetPropertyComp = cods[0].GetComponent<PropertyComponent>();
                return true;
            }
            targetPropertyComp = null; // If no target is found, set the target to null
            return false;
        }
    }
}
