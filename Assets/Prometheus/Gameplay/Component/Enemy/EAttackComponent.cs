using Spine;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EAttackComponent : Component.MonoComponent
    {
        public EnemyAttackConfig eAttackConfig;
        public Timer atkTimer;
        public EffectComponent targetEffectComp;
        public TrackEntry trackEntry;
        public bool isAttacking = false;
        public bool CheckAttack()
        {
            var cods = Physics.OverlapSphere(Entity.bindGo.transform.position, eAttackConfig.attckRadius, LayerMask.GetMask("Character"), QueryTriggerInteraction.UseGlobal);
            if (cods.Length > 0)
            {
                targetEffectComp = cods[0].GetComponent<EffectComponent>();
                return true;
            }
            targetEffectComp = null; // If no target is found, set the target to null
            return false;
        }
    }
}