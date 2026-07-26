using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EnemyAttackLogic : Logic.Logic, ITriggerHandler
    {
        EnemyAttackComponent enemyAttackComp;
        PropertyComponent propComp;
        SpineComponent spineComp;
        AttackComponent atkComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out enemyAttackComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            enemyAttackComp.atkTimer = new(3f);
            atkComp.atkCollider.handler = this;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return enemyAttackComp.CheckAttack();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<EnmityLogic>();
            Entity.UnBlockLogic<PatrolLogic>();
        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            Entity.BlockLogic<EnmityLogic>();
            Entity.BlockLogic<PatrolLogic>();
            spineComp.animationLib.atkExecutor.Execute();
            enemyAttackComp.atkTimer.SetActive(true);
        }

        public void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
            {
                enemyAttackComp.targetEffectComp.toAddEffects.Add(new DamageEffect(enemyAttackComp.targetEffectComp.Entity, propComp.propConfig.atk));
            }
        }

        public override void OnUpdate(float dt)
        {
            enemyAttackComp.atkTimer.OnUpdate(dt);
            if (enemyAttackComp.atkTimer.IsTimeOut)
            {
                atkComp.curTrackEntry = spineComp.animationLib.atkExecutor.Execute();
                enemyAttackComp.atkTimer.Reset(true);
            }
        }
    }
}