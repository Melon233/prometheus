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
        public float OnTakeDamage(float damage)
        {
            FloatDamageKit.Instance.CastDamageText(damage, transform.position);
            if (curHp <= damage)
            {
                curHp = 0;
                damage = curHp;
            }
            else curHp -= damage;
            return damage;
        }
        public void OnRecoverHp(float recover)
        {
            if ((curHp += recover) > propConfig.hp)
            {
                curHp = propConfig.hp;
            }
            FloatDamageKit.Instance.CastDamageText(recover, transform.position, true);
        }
        public float GetAttackDamage()
        {
            return propConfig.atk * (1f + (Random.Range(0f, 1f) >= propConfig.critRate ? propConfig.critDmg : 0));
        }
    }
}