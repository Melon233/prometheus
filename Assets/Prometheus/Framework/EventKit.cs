using System;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

namespace Xuan.Prometheus
{
    public enum EventType
    {
        TEST_EVENT,
    }
    public interface IEventKit
    {
        void AddListener(EventType evt, Action callback);
        void AddListener<T1>(EventType evt, Action<T1> callback);
        void RemoveListener(EventType evt, Action callback);
        void RemoveListener<T1>(EventType evt, Action<T1> callback);
        void Invoke(EventType evt);
        void Invoke<T1>(EventType evt, T1 arg1) where T1 : IEvent;  // 事件总线接口
    }  // 事件总线接口
    public class EventKit : Kit, IEventKit
    {
        private Dictionary<EventType, Delegate> eventDict = new Dictionary<EventType, Delegate>();
        // private Dictionary<string, Action> stringEventDict = new();
        public void AddListener(EventType evt, Action callback)
        {
            if (eventDict.TryGetValue(evt, out var action))
                action = Delegate.Combine(action, callback);  // 使用 Delegate.Combine 方法合并多个委托
            else
                eventDict.Add(evt, callback);
        }
        public void AddListener<T1>(EventType evt, Action<T1> callback)
        {
            if (eventDict.TryGetValue(evt, out var action))
                action = Delegate.Combine(action, callback);  // 使用 Delegate.Combine 方法合并多个委托
            else
                eventDict.Add(evt, callback);
        }
        public void RemoveListener(EventType evt, Action callback)
        {
            if (eventDict.TryGetValue(evt, out var action))
                action = Delegate.Remove(action, callback);  // 使用 Delegate.Remove 方法移除委托
        }

        public void RemoveListener<T1>(EventType evt, Action<T1> callback)
        {
            if (eventDict.TryGetValue(evt, out var action))
                action = Delegate.Remove(action, callback);
        }
        public void Invoke(EventType evt)
        {
            if (eventDict.TryGetValue(evt, out var del))
                if (del is Action action)
                    action?.Invoke();  // 使用?.Invoke 方法调用委托
        }
        public void Invoke<T1>(EventType evt, T1 arg1) where T1 : IEvent
        {
            if (eventDict.TryGetValue(evt, out var del))
                if (del is Action<T1> action)
                    action?.Invoke(arg1);  // 使用?.Invoke 方法调用委托
        }
    }
}