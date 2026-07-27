using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EAttackLogic : Logic.Logic, ITriggerHandler
    {
        EAttackComponent eAttackComp;
        EnmityComponent enmityComp; // Add this line to get the EnmityComponent
        PropertyComponent propComp;
        SpineComponent spineComp;
        AttackComponent atkComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out eAttackComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out enmityComp); // Add this line to get the EnmityComponent
            eAttackComp.atkTimer = new(5f);
            atkComp.atkCollider.handler = this;
        }

        public override bool CanDisable()
        {
            // return eAttackComp.trackEntry == null || eAttackComp.trackEntry.IsComplete;
            return !CanEnable() && !eAttackComp.isAttacking;
        }

        public override bool CanEnable()
        {
            return CheckAttack();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<EnmityLogic>();
            Entity.UnBlockLogic<PatrolLogic>();
            eAttackComp.atkTimer.Reset();
        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            Entity.BlockLogic<EnmityLogic>();
            Entity.BlockLogic<PatrolLogic>();
            eAttackComp.trackEntry = spineComp.animationLib.atkExecutor.Execute();
            eAttackComp.isAttacking = true;
            // eAttackComp.trackEntry.Complete += (entry) => spineComp.animationLib.idleExecutor.Execute();
            // eAttackComp.trackEntry.Interrupt += (entry) => entry = null;
            // enemyAttackComp.atkTimer.SetActive(true);
        }

        public void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                eAttackComp.targetEffectComp.toAddEffects.Add(new DamageEffect(Entity, eAttackComp.targetEffectComp.Entity, propComp.propConfig.atk));
            }
        }
        public bool CheckAttack()
        {
            if (enmityComp.target != null && Vector3.Distance(Entity.bindGo.transform.position, enmityComp.target.position) < eAttackComp.eAttackConfig.attckRadius)
            {
                eAttackComp.targetEffectComp = enmityComp.target.GetComponent<EffectComponent>();
                return true;
            }
            eAttackComp.targetEffectComp = null; // If no target is found, set the target to null
            return false;
        }
        public override void OnUpdate(float dt)
        {
            eAttackComp.atkTimer.OnUpdate(dt);
            if (eAttackComp.atkTimer.IsTimeOut)
            {
                eAttackComp.trackEntry = spineComp.animationLib.atkExecutor.Execute();
                eAttackComp.isAttacking = true;
                eAttackComp.atkTimer.Reset();
            }
            if (eAttackComp.trackEntry.IsComplete)
            {
                spineComp.animationLib.idleExecutor.Execute();
                eAttackComp.isAttacking = false;
            }
        }
    }
}