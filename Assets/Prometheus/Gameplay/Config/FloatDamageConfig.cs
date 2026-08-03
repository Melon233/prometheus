using UnityEngine;

namespace Xuan.Prometheus
{
    [CreateAssetMenu(menuName = "Prometheus/FloatDamageKit")]
    public class FloatDamageConfig : ScriptableObject
    {
        public FloatDmgComponent dmgComp;
        public AnimationCurve yCurve;
        public AnimationCurve scaleCurve;
        public float radius = 1f;
        public float startHeight = 1f;
        [Min(0f)]
        public float height;

        [Min(0.01f)]
        public float lifeTime = 1.5f;  // 浮点伤害文本的生命周期
    }
}
