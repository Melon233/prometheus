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
            eventComp.AddListener<AttackedEvent>(OnAttacked);
        }

        private void OnAttacked(object obj)
        {
            eventComp.Invoke(new AttackedStartEvent());
            trackEntry = attackedExecutor.Execute();
            trackEntry.Complete += (e) => eventComp.Invoke(new AttackedEndEvent());
            trackEntry.Interrupt += (e) => eventComp.Invoke(new AttackedEndEvent());
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
            eventComp.RemoveListener<AttackedEvent>(OnAttacked);
        }

        public override void OnEnable()
        {

        }

        public override void OnUpdate(float dt)
        {

        }
    }
}