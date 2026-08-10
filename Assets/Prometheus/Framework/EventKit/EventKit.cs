using System;
using System.Collections.Generic;

namespace Xuan.Prometheus
{
    /// <summary>定义由全局 EventKit 路由的事件类型。</summary>
    public enum Event
    {
        /// <summary>保留用于验证事件总线基础行为的测试事件。</summary>
        TEST_EVENT,
        /// <summary>表示当前玩家的生命值因受到伤害而发生变化。</summary>
        SelfHpChanged
    }

    /// <summary>携带当前玩家一次受伤前后的生命值快照，供全局界面和表现系统只读消费。</summary>
    public sealed class SelfHpChangedEvent : IEvent
    {
        /// <summary>获取受到本次伤害前的生命值。</summary>
        public float OldHp { get; }

        /// <summary>获取结算本次伤害后的当前生命值。</summary>
        public float CurrentHp { get; }

        /// <summary>获取结算本次伤害时的生命值上限。</summary>
        public float MaxHp { get; }

        /// <summary>创建一条不可变的当前玩家生命值变化事实。</summary>
        public SelfHpChangedEvent(float oldHp, float currentHp, float maxHp)
        {
            OldHp = oldHp;
            CurrentHp = currentHp;
            MaxHp = maxHp;
        }
    }

    /// <summary>定义全局事件总线的监听、退订和同步发布能力。</summary>
    public interface IEventKit
    {
        /// <summary>添加一条无参数全局事件监听。</summary>
        void AddListener(Event evt, Action callback);

        /// <summary>添加一条携带类型化事件数据的全局事件监听。</summary>
        void AddListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent;

        /// <summary>移除一条无参数全局事件监听。</summary>
        void RemoveListener(Event evt, Action callback);

        /// <summary>移除一条携带类型化事件数据的全局事件监听。</summary>
        void RemoveListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent;

        /// <summary>同步发布一条无参数全局事件。</summary>
        void Invoke(Event evt);

        /// <summary>同步发布一条携带类型化事件数据的全局事件。</summary>
        void Invoke<TEvent>(Event evt, TEvent eventData) where TEvent : IEvent;
    }

    /// <summary>由 GameCore 托管的全局同步事件总线，负责保存事件监听并在释放时统一清理。</summary>
    public sealed class EventKit : Kit, IEventKit
    {
        /// <summary>按全局事件类型保存当前注册的委托链。</summary>
        private readonly Dictionary<Event, Delegate> eventDict = new Dictionary<Event, Delegate>();

        /// <summary>记录事件总线是否已经释放，阻止失效实例继续收发事件。</summary>
        private bool isDisposed;

        /// <summary>创建事件总线并公开当前 GameCore 使用的全局事件入口。</summary>
        public EventKit()
        {
            Core.Event = this;
        }

        /// <inheritdoc />
        public void AddListener(Event evt, Action callback)
        {
            AddListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void AddListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent
        {
            AddListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void RemoveListener(Event evt, Action callback)
        {
            RemoveListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void RemoveListener<TEvent>(Event evt, Action<TEvent> callback) where TEvent : IEvent
        {
            RemoveListenerInternal(evt, callback);
        }

        /// <inheritdoc />
        public void Invoke(Event evt)
        {
            ThrowIfDisposed();
            if (eventDict.TryGetValue(evt, out Delegate callbacks) && callbacks is Action action) action.Invoke();
        }

        /// <inheritdoc />
        public void Invoke<TEvent>(Event evt, TEvent eventData) where TEvent : IEvent
        {
            ThrowIfDisposed();
            if (eventDict.TryGetValue(evt, out Delegate callbacks) && callbacks is Action<TEvent> action) action.Invoke(eventData);
        }

        /// <summary>清空全部全局监听，并仅在静态入口仍指向当前实例时解除引用。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            eventDict.Clear();
            if (ReferenceEquals(Core.Event, this)) Core.Event = null;
            isDisposed = true;
        }

        /// <summary>校验监听器并将其追加到指定全局事件的委托链。</summary>
        private void AddListenerInternal(Event evt, Delegate callback)
        {
            ThrowIfDisposed();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (eventDict.TryGetValue(evt, out Delegate callbacks)) eventDict[evt] = Delegate.Combine(callbacks, callback);
            else eventDict.Add(evt, callback);
        }

        /// <summary>从指定全局事件移除监听器，并在委托链为空时清理字典项。</summary>
        private void RemoveListenerInternal(Event evt, Delegate callback)
        {
            ThrowIfDisposed();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!eventDict.TryGetValue(evt, out Delegate callbacks)) return;
            Delegate remainingCallbacks = Delegate.Remove(callbacks, callback);
            if (remainingCallbacks == null) eventDict.Remove(evt);
            else eventDict[evt] = remainingCallbacks;
        }

        /// <summary>防止已经释放的事件总线继续收发全局事件。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(EventKit));
        }
    }
}
