using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    public class PatrolLogic : Logic.Logic
    {
        SlimeComponent slimeComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out slimeComp);
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return slimeComp.enmityTarget == null;
        }

        public override void OnDisable()
        {

        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {

        }

        public override void OnUpdate(float dt)
        {

        }
    }
}