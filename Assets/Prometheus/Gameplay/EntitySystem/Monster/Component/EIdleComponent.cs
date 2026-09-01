using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class EIdleComponent : Component.Component
    {
        public float idleTime = 2f; // Idle time in seconds
        public Timer idleTimer;
        public void Execute(float time)
        {
            idleTimer.SetTotalTime(time);
            idleTimer.Reset();
        }
        public void Interrupt()
        {
            idleTimer.SetLeftTime(0f);
        }
    }
}
