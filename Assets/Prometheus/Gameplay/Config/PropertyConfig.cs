using UnityEngine;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(menuName = "Prometheus/PropertyConfig")]
    public class PropertyConfig : ScriptableObject
    {
        public float atk = 1f;
        public float critDmg;
        public float critRate;
        public float def;
        public float hp; // 100ms
        public float walkSpeed;
        public float runSpeed;
        public float sprintSpeed;
        public float airMoveSpeed;
        public float jumpSpeed;
        public float gravity;
        public float coreEnergyLimit;
        public float ultEnergyLimit;
    }
}