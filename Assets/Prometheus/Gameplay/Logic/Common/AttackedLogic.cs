using System;
using Spine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class AttackedLogic : Logic.Logic
    {
        EventComponent eventComp;
        SpineComponent spineComp;
        AttackedExecutor attackedExecutor;
        TrackEntry trackEntry;
        public override void AfterNew()
        {
            Entity.TryGetComp(out eventComp);
            Entity.TryGetComp(out spineComp);
            attackedExecutor = spineComp.animationLib.attackedExecutor;
            eventComp.AddListener(EventName.Attacked, OnAttacked);
        }

        private void OnAttacked(object obj)
        {
            Entity.BlockLogic<PatrolLogic>();
            trackEntry = attackedExecutor.Execute();
            trackEntry.Complete += (e) => Entity.UnBlockLogic<PatrolLogic>();
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override void OnDisable()
        {

        }

        public override void OnDispose()
        {
            eventComp.RemoveListener(EventName.Attacked, OnAttacked);
        }

        public override void OnEnable()
        {

        }

        public override void OnUpdate(float dt)
        {

        }
    }
}