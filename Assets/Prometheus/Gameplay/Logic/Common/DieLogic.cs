using System;
using Spine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    public class DieLogic : Logic.Logic
    {
        SpineComponent spineComp;
        DieExecutor dieExecutor;
        EventComponent eventComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out eventComp);
            eventComp.AddListener(EventName.Die, OnDie);
            dieExecutor = spineComp.animationLib.dieExecutor;
        }

        private void OnDie(object obj)
        {
            Entity.BlockLogic<PatrolLogic>();
            dieExecutor.Execute();
            Entity.OnDispose(dieExecutor.dieAnimation.Animation.Duration + 1f);
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

        }

        public override void OnDispose()
        {
            eventComp.RemoveListener(EventName.Die, OnDie);
        }

        public override void OnEnable()
        {
        }

        public override void OnUpdate(float dt)
        {

        }
    }
}