using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EAttackComponent : Component.Component
    {
        public EnemyAttackConfig eAttackConfig;
        public Timer recoveryTimer;
        public PropertyComponent targetPropertyComp;
        [System.NonSerialized] public AnimationPlayback animationPlayback;
        public bool isAttacking = false;
        public bool isRecovery = false;
        public float attackRecoveryTime = 2f;
        public Timer attackRecoveryTimer;
        public bool CheckAttack()
        {
            var cods = Physics.OverlapSphere(Entity.bindGo.transform.position, eAttackConfig.attckRadius, LayerMask.GetMask("Character"), QueryTriggerInteraction.UseGlobal);
            if (cods.Length > 0)
            {
                targetPropertyComp = ColliderProxy.TryGetHostEntity(cods[0], out Logic.Entity target) && target.TryGetComp(out PropertyComponent property) ? property : null;
                if (targetPropertyComp == null) return false;
                return true;
            }
            targetPropertyComp = null; // If no target is found, set the target to null
            return false;
        }
    }
}
