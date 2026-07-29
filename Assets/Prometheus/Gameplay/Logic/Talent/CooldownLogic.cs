using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class CooldownLogic : Logic.Logic
    {
        AttackComponent atkComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out atkComp);
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override bool CanEnable()
        {
            return false;
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