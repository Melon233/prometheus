using Codice.Client.Common;
using Xuan.Prometheus.Config;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    public class EnmityLogic : Logic.Logic
    {
        SlimeComponent slimeComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out slimeComp);
            slimeComp.hp = slimeComp.slimeConfig.maxHp;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return slimeComp.enmityTarget != null;
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
            var vec = (slimeComp.enmityTarget.position - slimeComp.transform.position);
            // if (vec.magnitude < slimeData.slimeConfig.enmityRadius)
            // {

            // }
            // else
            {
                slimeComp.cc.Move(vec.normalized * slimeComp.slimeConfig.walkVelo * dt);
            }
        }
    }
}