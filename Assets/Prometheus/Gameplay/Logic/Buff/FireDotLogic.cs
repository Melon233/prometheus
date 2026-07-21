using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class FireDotLogic : Logic
    {
        private FireDotComponent effectComp;

        public override void AfterNew()
        {
            Entity.TryGetComp(out effectComp);
        }

        public override bool CanEnable()
        {
            return !effectComp.IsOver;
        }

        public override bool CanDisable()
        {
            return effectComp.IsOver;
        }

        public override void OnEnable()
        {
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
            effectComp.curTime += dt;
            if ((effectComp.curTickTime += dt) > effectComp.tickTime)
                if (Entity.TryGetComp<PropertyComponent>(out var propComp))
                {
                    propComp.OnTakeDamage(effectComp.dotDmg);
                    effectComp.curTickTime -= effectComp.tickTime;
                }
        }

        public override void OnDispose()
        {
        }
    }
}