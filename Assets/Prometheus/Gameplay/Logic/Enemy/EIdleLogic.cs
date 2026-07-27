using System;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EIdleLogic : Logic.Logic
    {
        EAttackComponent eAttackComp;
        PropertyComponent propComp;
        SpineComponent spineComp;
        AttackComponent atkComp;
        EIdleComponent eIdleComp;
        EventComponent evtComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out eAttackComp);
            Entity.TryGetComp(out propComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out atkComp);
            Entity.TryGetComp(out eIdleComp); // Add this line to get the EIdleComponent
            Entity.TryGetComp(out evtComp);
            evtComp.AddListener<AttackedEvent>(OnAttacked);
            eIdleComp.idleTimer = new(eIdleComp.idleTime);
        }

        private void OnAttacked(AttackedEvent evt)
        {

        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return !eIdleComp.idleTimer.IsTimeOut;
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
            Entity.BlockLogic<PatrolLogic>();
            spineComp.animationLib.idleExecutor.Execute();
        }

        public override void OnUpdate(float dt)
        {
            eIdleComp.idleTimer.OnUpdate(dt);
        }
    }
}