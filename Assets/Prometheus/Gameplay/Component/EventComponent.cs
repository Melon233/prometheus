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
    /// <summary>
    /// ControlStateChangedEvent 是 PropertyComponent 聚合控制状态发生变化后的只读事实，供表现层和调试工具订阅。
    /// </summary>
    public sealed class ControlStateChangedEvent : IEvent
    {
        /// <summary>获取变化前的控制状态集合。</summary>
        public Xuan.Prometheus.Component.ControlState PreviousStates { get; }

        /// <summary>获取变化后的控制状态集合。</summary>
        public Xuan.Prometheus.Component.ControlState CurrentStates { get; }

        /// <summary>创建一条包含变化前后完整快照的控制状态事件。</summary>
        public ControlStateChangedEvent(Xuan.Prometheus.Component.ControlState previousStates, Xuan.Prometheus.Component.ControlState currentStates)
        {
            PreviousStates = previousStates;
            CurrentStates = currentStates;
        }
    }
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

        /// <summary>清除当前实体的全部监听器，由 Entity 最终回收阶段调用以阻断延迟动画回调持有的失效订阅。</summary>
        public void ClearListeners()
        {
            eventDict.Clear();
        }
    }
}
