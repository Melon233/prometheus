using System;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class TimerManager : MonoSingleton<TimerManager>
    {
        private XMap<object, Timer> timerMap = new XMap<object, Timer>();

        private void Update()
        {
            foreach (var timer in timerMap) timer.OnUpdate(Time.deltaTime);
        }
    }

    public class Timer
    {
        private float elapsedTime;
        private float leftTime;
        private float totalTime;
        public bool IsTimeOut => leftTime <= 0;
        public Timer(float totalTime, Action onTimeOut = null)
        {
            this.totalTime = totalTime;
            this.leftTime = totalTime;
            this.elapsedTime = 0f;
        }
        public void Reset()
        {
            leftTime = totalTime;
            elapsedTime = 0f;
        }
        public void TimeOut()
        {
            leftTime = 0f;
            elapsedTime = totalTime;
        }
        public void SetTotalTime(float total)
        {
            totalTime = total;
        }

        public void SetLeftTime(float time)
        {
            leftTime = time;
            elapsedTime = totalTime - leftTime;
        }

        public void OnUpdate(float dt)
        {
            elapsedTime += dt;
            leftTime = totalTime - elapsedTime;
            if (leftTime <= 0)
            {
                elapsedTime = totalTime;
                leftTime = 0f; // Reset left time to avoid negative values
            }
        }
    }
}