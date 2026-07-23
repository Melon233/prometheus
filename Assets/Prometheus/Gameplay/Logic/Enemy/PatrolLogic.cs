using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    public class PatrolLogic : Logic.Logic
    {
        SlimeComponent slimeComp;
        SpineComponent spineComp;
        IdleExecutor idleExecutor;
        public override void AfterNew()
        {
            Entity.TryGetComp(out slimeComp);
            Entity.TryGetComp(out spineComp);
            idleExecutor = spineComp.animationLib.idleExecutor;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            // return slimeComp.enmityTarget == null;
            return true;
        }

        public override void OnDisable()
        {

        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            idleExecutor.Execute();
        }

        public override void OnUpdate(float dt)
        {

        }
    }
}