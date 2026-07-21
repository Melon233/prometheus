using UnityEngine;

namespace Xuan.Prometheus.Logic
{
    public class PlayerConfig : ScriptableObject
    {
        public float maxHp;
        public float maxSkillPoints;
        public float walkVelo;
        public float runVelo;
        public float jumpVelo;
        public float gravity;
        public float minAtkInterval;
        public float maxAtkInterval;
    }
}