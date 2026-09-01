using UnityEngine;
using Xuan.Prometheus.Config;

namespace Xuan.Prometheus.Logic
{
    public class SlimeComponent : Component.Component
    {
        public CharacterController cc;
        public float hp;
        public Transform enmityTarget;
        public SlimeConfig slimeConfig;
    }
}
