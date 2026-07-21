using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class TakeDmgLogic : Logic.Logic
    {
        PropertyComponent propComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out propComp);
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

        }

        public override void OnEnable()
        {

        }

        public override void OnUpdate(float dt)
        {

        }
    }
}