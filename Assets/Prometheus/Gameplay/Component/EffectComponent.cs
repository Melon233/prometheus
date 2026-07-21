using System.Collections.Generic;

namespace Xuan.Prometheus.Component
{
    public class EffectComponent : Component
    {
        public float curTickTime;
        public float curTime;
        public float duration = 10f;
        public List<float> paras = new List<float> { 20f };
        public float tickTime = 2f;
        public float NormalizedTime => curTime / duration;
        public bool IsOver => curTime >= duration;
    }
}