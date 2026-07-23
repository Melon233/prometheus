using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    public enum EventName
    {
        Attacked,
        Die
    }
    public class EventComponent : Component.Component
    {
        Dictionary<EventName, List<Action<object>>> eventDict = new();
        public void AddListener(EventName e, Action<object> action)
        {
            if (!eventDict.ContainsKey(e))
            {
                eventDict.Add(e, new List<Action<object>>());
            }
            eventDict[e].Add(action);
        }

        public void RemoveListener(EventName e, Action<object> action)
        {
            if (eventDict.ContainsKey(e))
            {
                eventDict[e].Remove(action);
            }
        }
        public void Invoke(EventName e, object param = null)
        {
            if (eventDict.ContainsKey(e))
            {
                foreach (var action in eventDict[e])
                {
                    action(param);
                }
            }
        }
    }
}