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
        EventComponent evtComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out eAttackComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out enmityComp); // Add this line to get the EnmityComponent
            Entity.TryGetComp(out evtComp);
            eAttackComp.recoveryTimer = new(3f);
            atkComp.atkCollider.handler = this;
            // evtComp.AddListener<AttackedEvent>(evt => eAttackComp.isAttacking = false);
        }

        public override bool CanDisable()
        {
            // return eAttackComp.trackEntry == null || eAttackComp.trackEntry.IsComplete;
            return !CanEnable() && !eAttackComp.isAttacking && !eAttackComp.isRecovery;
        }

        public override bool CanEnable()
        {
            return CheckAttack();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<EnmityLogic>();
            Entity.UnBlockLogic<PatrolLogic>();
            eAttackComp.recoveryTimer.TimeOut();
        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            Entity.BlockLogic<EnmityLogic>();
            Entity.BlockLogic<PatrolLogic>();
            eAttackComp.recoveryTimer.TimeOut();
            // eAttackComp.trackEntry.Complete += (entry) => spineComp.animationLib.idleExecutor.Execute();
            // eAttackComp.trackEntry.Interrupt += (entry) => entry = null;
            // enemyAttackComp.atkTimer.SetActive(true);
        }

        public void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                if (eAttackComp.targetEffectComp == null) other.TryGetComponent(out eAttackComp.targetEffectComp); // Check if the target has an EffectComponent
                eAttackComp.targetEffectComp.toAddEffects.Add(new DamageEffect(Entity, eAttackComp.targetEffectComp.Entity, propComp.propConfig.atk));
            }
        }
        public bool CheckAttack()
        {
            GizmosKit.Instance.DrawWireCircle(Entity.bindGo.transform.position, Vector3.up, eAttackComp.eAttackConfig.attckRadius, Color.yellow); // Debug log to confirm the enemy is chasing the player
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
            if (eAttackComp.isRecovery)
                eAttackComp.recoveryTimer.OnUpdate(dt);
            if (eAttackComp.recoveryTimer.IsTimeOut && !eAttackComp.isAttacking)
            {
                eAttackComp.isRecovery = false;
                if (eAttackComp.targetEffectComp != null)
                {
                    eAttackComp.isAttacking = true;
                    eAttackComp.trackEntry = spineComp.animationLib.atkExecutor.Execute();
                    eAttackComp.trackEntry.Complete += (entry) =>
                    {
                        spineComp.animationLib.idleExecutor.Execute();
                        eAttackComp.recoveryTimer.Reset();
                        eAttackComp.isAttacking = false;
                        eAttackComp.isRecovery = true;
                    };
                    eAttackComp.trackEntry.Interrupt += (entry) =>
                    {
                        eAttackComp.isAttacking = false;
                    };
                }
            }
        }
    }
}