using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EnemyAttackComponent : Component.MonoComponent
    {
        public EnemyAttackConfig enemyAttackConfig;
        public Timer atkTimer;
        public EffectComponent targetEffectComp;
        public bool CheckAttack()
        {
            var cods = Physics.OverlapSphere(Entity.bindGo.transform.position, enemyAttackConfig.attckRadius, LayerMask.GetMask("Character"), QueryTriggerInteraction.UseGlobal);
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