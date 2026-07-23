using DG.Tweening;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class PropertyComponent : MonoComponent
    {
        public float atk = 30f;
        public float critDmg = 0.5f;
        public float critRate = 0.1f;
        public float def = 10f;
        public float hp = 100f;
        public float maxHp = 100f;
        public HpBar hpBar;
        public bool NoHp => hp <= 0;
        public void OnTakeDamage(float damage)
        {
            if ((hp -= damage) <= 0)
            {
                hp = 0;
                DOVirtual.Float(1f, 0f, 1f, f =>
                {
                    hpBar.canvasGroup.alpha = f;
                });
            }
            FloatDamageKit.Instance.CastDamageText(damage, transform.position);
            hpBar.SetHp(hp / maxHp);
        }
    }
}