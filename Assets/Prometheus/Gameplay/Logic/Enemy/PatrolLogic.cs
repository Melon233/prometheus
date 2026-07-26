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
        public override void AfterNew()
        {
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out patrolComp);
            Entity.TryGetComp(out enmityComp);
            Entity.TryGetComp(out evtComp);
            evtComp.AddListener<StunStartEvent>(OnStunStart);
            evtComp.AddListener<StunEndEvent>(OnStunEnd);

            idleExecutor = spineComp.animationLib.idleExecutor;
            groundMoveExecutor = spineComp.animationLib.groundMoveExecutor;
            patrolComp.spawnPoint = patrolComp.transform.position;
            patrolComp.moveTimer = new(patrolComp.patrolConfig.moveInterval);
            GizmosKit.Instance.DrawWireCircle(patrolComp.spawnPoint, Vector3.up, patrolComp.patrolConfig.patrolRadius, Color.green, duration: 999f);
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override void OnDisable()
        {
            patrolComp.moveTimer.SetActive(false);
        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            idleExecutor.Execute();
            patrolComp.moveTimer.SetActive(true);
        }

        public override void OnUpdate(float dt)
        {
            GizmosKit.Instance.DrawWireCircle(patrolComp.transform.position, Vector3.up, enmityComp.enmityConfig.enmityRadius, Color.red);
            if (!patrolComp.isPatrolling)
            {
                patrolComp.moveTimer.OnUpdate(dt);
                idleExecutor.Execute();
            }
            if (patrolComp.moveTimer.IsTimeOut)
            {
                var angle = Random.Range(0f, Mathf.PI * 2f);
                while (Vector3.Distance(patrolComp.nextTargetPoint = patrolComp.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * patrolComp.patrolConfig.moveDelta, patrolComp.spawnPoint) > patrolComp.patrolConfig.patrolRadius) angle = Random.Range(0f, Mathf.PI * 2f);
                patrolComp.isPatrolling = true;
                patrolComp.moveTimer.Reset(false);
            }
            if (patrolComp.isPatrolling)
            {
                patrolComp.cc.Move(dt * patrolComp.patrolConfig.patrolSpeed * (patrolComp.nextTargetPoint - patrolComp.transform.position).normalized);
                groundMoveExecutor.Execute();
                spineComp.SetFaceDir(patrolComp.cc.velocity.x);
            }
            if (Vector3.Distance(patrolComp.transform.position, patrolComp.nextTargetPoint) < 0.1f)
            {
                patrolComp.isPatrolling = false;
                patrolComp.moveTimer.SetActive(true);
            }
        }
        private void OnStunStart(StunStartEvent evt)
        {
            Entity.BlockLogic<PatrolLogic>();
            Entity.BlockLogic<EnmityLogic>();
            Entity.BlockLogic<EnemyAttackLogic>();
        }
        private void OnStunEnd(StunEndEvent evt)
        {
            Entity.UnBlockLogic<PatrolLogic>();
            Entity.UnBlockLogic<EnmityLogic>();
            Entity.UnBlockLogic<EnemyAttackLogic>();
        }
    }
}