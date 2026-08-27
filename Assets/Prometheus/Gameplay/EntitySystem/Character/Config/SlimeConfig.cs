using UnityEngine;

namespace Xuan.Prometheus.Config
{
    /// <summary>
    /// 半径里没敌人随机移动，有敌人时向敌人移动，进入攻击范围时攻击
    /// </summary>
    [CreateAssetMenu(menuName = "Configs/SlimeConfig")]
    public class SlimeConfig : ScriptableObject
    {
        public float maxHp = 100f;
        public float atk = 10f;
        public float walkVelo = 1f;
        public float enmity = 1f;
        public float enmityRadius = 3f;
        public float atkRadius = 1f;
        public float minAtkInterval = 1f;
    }
}