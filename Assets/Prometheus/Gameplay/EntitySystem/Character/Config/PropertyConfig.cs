using UnityEngine;
using UnityEngine.Serialization;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(menuName = "Prometheus/PropertyConfig")]
    public class PropertyConfig : ScriptableObject
    {
        /// <summary>配置角色自身元素；普通攻击和特殊攻击仍以物理为基础，技能与大招读取该值。</summary>
        public Cfg.ElementType elementAttribute = Cfg.ElementType.Physical;
        public float atk = 1f;
        /// <summary>
        /// 攻击速度的基础倍率，默认值 1 表示正常播放速度。
        /// </summary>
        public float atkSpeed = 1f;
        public float critDmg;
        public float critRate;
        public float def;
        /// <summary>
        /// 配置单位的**抗打断等级**（08 第 2.2 节）。
        ///
        /// 0 表示任何攻击都能打断（小型敌人），1～2 一般敌人，3～4 精英怪，5 及以上 Boss。
        /// 它是等级而不是阈值：判定是 `攻击打断等级 > 抗打断等级` 的整数比较，
        /// 与伤害数值无关——大伤害的普攻打不断遗迹守卫，小伤害的大招可以。
        /// </summary>
        [FormerlySerializedAs("toughness")]
        public float staggerResistance = 1f;
        public float hp; // 100ms
        public float walkSpeed;
        public float runSpeed;
        public float sprintSpeed;
        public float airMoveSpeed;
        public float jumpSpeed;
        public float gravity;
        public float coreEnergyLimit;
        public float ultEnergyLimit;
        public float walkEdge = 0.2f;
        public float sprintEdge = 0.7f;
        public float damp = 0.05f;
    }
}
