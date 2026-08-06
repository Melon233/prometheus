using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    public interface ITimeMachine
    {
        void RegisterUpdate(Action<float> update);
        void RemoveUpdate(Action<float> update);
    }
    public class TimeMachine : ITimeMachine
    {
        private XMap<object, Action<float>> updates = new();
        public void RegisterUpdate(Action<float> update)
        {
        }

        public void RemoveUpdate(Action<float> update)
        {
            throw new NotImplementedException();
        }
    }
}