using DG.Tweening;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class PropertyComponent : MonoComponent
    {
        public PropertyConfig propConfig;
        public float curHp;
        public bool NoHp => curHp <= 0;
        void Start()
        {
            curHp = propConfig.hp;
        }
        public void OnTakeDamage(float damage)
        {
            if ((curHp -= damage) <= 0)
            {
                curHp = 0;
            }
            FloatDamageKit.Instance.CastDamageText(damage, transform.position);
        }
        public void OnRecoverHp(float recover)
        {
            if ((curHp += recover) > propConfig.hp)
            {
                curHp = propConfig.hp;
            }
        }
        public float GetAttackDamage()
        {
            return propConfig.atk * (1f + (Random.Range(0f, 1f) >= propConfig.critRate ? propConfig.critDmg : 0));
        }
    }
}