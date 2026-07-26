using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    public class AttackedEvent : IEvent { }
    public class StunStartEvent : IEvent { }
    public class StunEndEvent : IEvent { }
    public class DieEvent : IEvent { }
    public class HpChangedEvent : IEvent
    {
        public float hp;
        public float maxHp;
    }

    public class EventComponent : Component.Component
    {
        Dictionary<Type, Delegate> eventDict = new();
        public void AddListener<T>(Action<T> action) where T : IEvent
        {
            Type type = typeof(T);

            if (eventDict.TryGetValue(type, out Delegate callbacks))
                eventDict[type] = Delegate.Combine(callbacks, action);
            else
                eventDict[type] = action;
        }

        public void RemoveListener<T>(Action<T> action)
         where T : IEvent
        {
            Type type = typeof(T);

            if (!eventDict.TryGetValue(type, out Delegate callbacks))
                return;

            callbacks = Delegate.Remove(callbacks, action);
            if (callbacks == null)
                eventDict.Remove(type);
            else
                eventDict[type] = callbacks;
        }
        public void Invoke<T>(T evt) where T : IEvent
        {
            if (eventDict.TryGetValue(typeof(T), out Delegate callbacks))
                ((Action<T>)callbacks)?.Invoke(evt);
        }
    }
}