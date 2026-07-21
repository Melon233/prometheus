using System;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class SlimeLogic : Logic
    {
        SlimeComponent slimeComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out slimeComp);
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override void OnEnable()
        {
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnDispose()
        {
        }
    }
}