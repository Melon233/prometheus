using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    public class AttackedEvent : IEvent { }
    public class AttackedStartEvent : IEvent { }
    public class AttackedEndEvent : IEvent { }
    public class DieEvent : IEvent { }
    public class HpChangedEvent : IEvent
    {
        public float oldHp;
        public float newHp;
        public float maxHp;
    }
    public class StiffnessStartEvent : IEvent { }
    public class StiffnessEndEvent : IEvent { }
    public class MotionBlockerStartEvent : IEvent { }
    public class MotionBlockerEndEvent : IEvent { }
    public class HitEvent : IEvent { }
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

        public void RemoveListener<T>(Action<T> action) where T : IEvent
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