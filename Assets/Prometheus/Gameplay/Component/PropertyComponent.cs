using DG.Tweening;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class PropertyComponent : MonoComponent
    {
        public PropertyConfig propConfig;
        public float atkBoost;
        public float defBoost;
        public float moveSpeed;
        public float moveSpeedBoost;
        public float atkSpeedBoost;
        public float critRateBoost;
        public float critDmgBoost;

        public float Atk => propConfig.atk * (1f + atkBoost);
        public float Def => propConfig.def * (1f + defBoost);
        public float Speed => moveSpeed * (1f + moveSpeedBoost);
        public float AtkSpeed => 1f + atkSpeedBoost;
        public float CritRate => propConfig.critRate * (1f + critRateBoost);
        public float CritDmg => propConfig.critDmg * (1f + critDmgBoost);
        public float MaxHp => propConfig.hp;
        public float Hp { get; set; }
        public bool NoHp => Hp <= 0;
        void Start()
        {
            Hp = propConfig.hp;
        }
        public float OnTakeDamage(float damage)
        {
            FloatDamageKit.Instance.CastDamageText(damage, transform.position);
            if (Hp <= damage)
            {
                Hp = 0;
                damage = Hp;
            }
            else Hp -= damage;
            return damage;
        }
        public void OnRecoverHp(float recover)
        {
            if ((Hp += recover) > propConfig.hp)
            {
                Hp = propConfig.hp;
            }
            FloatDamageKit.Instance.CastDamageText(recover, transform.position, true);
        }
        public float GetAttackDamage()
        {
            return Atk * (1f + (Random.Range(0f, 1f) >= CritRate ? CritDmg : 0));
        }
    }
}