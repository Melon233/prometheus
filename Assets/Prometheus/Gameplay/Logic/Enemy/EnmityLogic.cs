using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    public class EnmityLogic : Logic.Logic
    {
        EnmityComponent enmityComp;
        PatrolComponent patrolComp;
        SpineComponent spineComp;
        EAttackComponent eAttackComp;
        EIdleComponent eIdleComp;
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out patrolComp);
            Entity.TryGetComp(out enmityComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out eAttackComp);
            Entity.TryGetComp(out eIdleComp);
            GizmosKit.Instance.DrawWireCircle(patrolComp.transform.position, Vector3.up, enmityComp.enmityConfig.chaseRadius, Color.yellow, 999f);
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return enmityComp.CheckEnmity();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<PatrolLogic>();
        }

        public override void OnDispose()
        {
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<PatrolLogic>(); // Block the PatrolLogic
            eIdleComp.Interrupt();
            spineComp.animationLib.groundMoveExecutor.Execute();
        }

        public override void OnUpdate(float dt)
        {
            GizmosKit.Instance.DrawWireCircle(patrolComp.transform.position, Vector3.up, enmityComp.enmityConfig.enmityRadius, Color.red);
            // GizmosKit.Instance.DrawWireCircle(patrolComp.transform.position, Vector3.up, enemyAttackComp.eAttackConfig.attckRadius, Color.blue);
            if (enmityComp.needGoHome)
            {
                patrolComp.cc.Move(dt * patrolComp.patrolConfig.patrolSpeed * (patrolComp.spawnPoint - patrolComp.transform.position).NormalizeToXZ());
                if (Vector3.Distance(patrolComp.transform.position, patrolComp.spawnPoint) < patrolComp.patrolConfig.patrolRadius) enmityComp.needGoHome = false;
            }
            else
            {
                patrolComp.cc.Move(dt * enmityComp.enmityConfig.chaseSpeed * (enmityComp.target.position - patrolComp.transform.position).NormalizeToXZ());
                if (Vector3.Distance(patrolComp.transform.position, patrolComp.spawnPoint) > enmityComp.enmityConfig.chaseRadius) enmityComp.needGoHome = true;
            }
            spineComp.SetFaceDir(patrolComp.cc.velocity.x);
        }
    }
}
