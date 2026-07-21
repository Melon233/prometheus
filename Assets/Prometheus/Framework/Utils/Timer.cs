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
        private bool isRunning;
        private float leftTime;
        private Action onTimeOut;
        private float totalTime;

        public Timer(float totalTime, Action onTimeOut = null)
        {
            this.totalTime = totalTime;
            this.onTimeOut = onTimeOut;
        }

        public void Activate()
        {
            isRunning = true;
        }

        public void Pause()
        {
            isRunning = false;
        }

        public void SetTotalTime(float total)
        {
            totalTime = total;
        }

        public void SetLeftTime(float time)
        {
            leftTime = time;
            elapsedTime = totalTime - elapsedTime;
        }

        public void OnUpdate(float dt)
        {
            if (!isRunning) return;
            elapsedTime += dt;
            leftTime = totalTime - elapsedTime;
            if (leftTime <= 0)
            {
                onTimeOut?.Invoke();
                isRunning = false;
            }
        }
    }
}