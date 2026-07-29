using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic.Talent
{
    public class SpecialAttackComponent : Component.MonoComponent
    {
        public ColliderProxy colliderProxy;
        public Timer specialTimer = new(0.5f);
        public bool canSpecial = true;
    }
}