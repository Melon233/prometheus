using System;

namespace Xuan.Prometheus
{
    public interface IStaticEventKit
    {

    }
    public interface IEvent
    {
    }
    public static class EventHandler<T> where T : IEvent
    {
        private static Action callback;
        public static void AddListener(Action callback)
        {
            EventHandler<T>.callback += callback;
        }
        public static void RemoveListener(Action callback)
        {
            EventHandler<T>.callback -= callback;  // 移除监听器
        }
        public static void Invoke(T evt)
        {
            EventHandler<T>.callback?.Invoke();
        }
    }
    public class StaticEventKit : Kit, IStaticEventKit
    {
        public void Invoke(IEvent evt)
        {
            EventHandler<IEvent>.Invoke(evt);
        }
        public void AddListener<T>(Action callback) where T : IEvent
        {
            EventHandler<T>.AddListener(callback);
        }
        public void RemoveListener<T>(Action callback) where T : IEvent
        {
            EventHandler<T>.RemoveListener(callback);
        }
    }
}