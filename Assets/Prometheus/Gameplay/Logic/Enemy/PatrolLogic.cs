using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    public class PatrolLogic : Logic.Logic
    {
        SpineComponent spineComp;
        PatrolComponent patrolComp;
        EnmityComponent enmityComp;
        IdleExecutor idleExecutor;
        GroundMoveExecutor groundMoveExecutor;
        EventComponent evtComp;
        EIdleComponent eIdleComp;
        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out patrolComp);
            Entity.TryGetComp(out enmityComp);
            Entity.TryGetComp(out evtComp);
            Entity.TryGetComp(out eIdleComp);
            // evtComp.AddListener<StunStartEvent>(OnStunStart);
            // evtComp.AddListener<StunEndEvent>(OnStunEnd);

            idleExecutor = spineComp.animationLib.idleExecutor;
            groundMoveExecutor = spineComp.animationLib.groundMoveExecutor;
            patrolComp.spawnPoint = patrolComp.transform.position;
            // patrolComp.patrolTimer = new(patrolComp.patrolConfig.moveInterval);
            GizmosKit.Ins.DrawWireCircle(patrolComp.spawnPoint, Vector3.up, patrolComp.patrolConfig.patrolRadius, Color.green, duration: 999f);
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return eIdleComp.idleTimer.IsTimeOut;
        }

        public override void OnDisable()
        {

        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            patrolComp.Execute();
            spineComp.animationLib.groundMoveExecutor.Execute();
        }

        public override void OnUpdate(float dt)
        {
            GizmosKit.Ins.DrawWireCircle(patrolComp.transform.position, Vector3.up, enmityComp.enmityConfig.enmityRadius, Color.red);
            // patrolComp.patrolTimer.OnUpdate(dt);
            patrolComp.cc.Move(dt * patrolComp.patrolConfig.patrolSpeed * (patrolComp.nextTargetPoint - patrolComp.transform.position).normalized);
            spineComp.SetFaceDir(patrolComp.cc.velocity.x);
            if (Vector3.Distance(patrolComp.transform.position, patrolComp.nextTargetPoint) < 0.1f)
            {
                patrolComp.isPatrolling = false;
                eIdleComp.idleTimer.Reset();
                // patrolComp.patrolTimer.SetActive(true);
            }
        }
        // private void OnStunStart(StunStartEvent evt)
        // {
        //     Entity.BlockLogic<PatrolLogic>();
        //     Entity.BlockLogic<EnmityLogic>();
        //     Entity.BlockLogic<EAttackLogic>();
        // }
        // private void OnStunEnd(StunEndEvent evt)
        // {
        //     Entity.UnBlockLogic<PatrolLogic>();
        //     Entity.UnBlockLogic<EnmityLogic>();
        //     Entity.UnBlockLogic<EAttackLogic>();
        // }
    }
}
