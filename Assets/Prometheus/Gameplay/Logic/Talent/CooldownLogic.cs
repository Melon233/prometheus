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
            var t = atkComp.elapsedComboTime += dt;

            if (t > atkComp.maxComboInterval)
            {
                atkComp.canCombo = true;
                atkComp.nextComboIndex = 0;
            }
            else if (t > atkComp.minComboInterval)
            {
                atkComp.canCombo = true;
                if (atkComp.nextComboIndex > atkComp.maxComboIndex) atkComp.nextComboIndex = 0;
            }

        }
    }
}