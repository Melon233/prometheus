using UnityEngine;

namespace Xuan.Prometheus.Component
{
    [CreateAssetMenu(fileName = "FloatingDmgTextConfig", menuName = "Prometheus/FloatingDmgTextConfig")]
    public class FloatingDmgTextConfig : ScriptableObject
    {
        public AnimationCurve upCurve;
        public AnimationCurve scaleCurve;
        public float upOffset;
        public float scaleOffset;
        public float duration;
    }
}