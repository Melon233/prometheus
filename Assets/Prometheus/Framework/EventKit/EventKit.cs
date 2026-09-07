using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>标记可由全局 EventKit 路由的类型化事件载荷；载荷的具体类型就是唯一路由键。</summary>
    public interface IEvent
    {
    }

    /// <summary>
    /// 定义全局事件总线的监听、退订和同步发布能力。
    /// 事件的具体类型是唯一路由键，因此新增事件只需要在所属领域定义载荷类型，不需要修改本模块。
    /// 方法命名与实体内 EventComponent 保持一致，使全局事实与实体内事实的调用形状完全相同。
    /// </summary>
    public interface IEventKit : IKitContract
    {
        /// <summary>添加一条针对指定事件类型的全局监听；同一委托重复添加会被重复通知。</summary>
        /// <typeparam name="TEvent">监听的事件载荷类型。</typeparam>
        /// <param name="callback">事件发布时同步执行的监听器。</param>
        void AddListener<TEvent>(Action<TEvent> callback) where TEvent : IEvent;

        /// <summary>移除一条针对指定事件类型的全局监听；未注册的委托会被安全忽略。</summary>
        /// <typeparam name="TEvent">退订的事件载荷类型。</typeparam>
        /// <param name="callback">此前通过 AddListener 注册的同一委托实例。</param>
        void RemoveListener<TEvent>(Action<TEvent> callback) where TEvent : IEvent;

        /// <summary>向指定事件类型的全部监听器同步发布一条已经发生的事实；没有监听器时是合法的空操作。</summary>
        /// <typeparam name="TEvent">发布的事件载荷类型，决定路由目标。</typeparam>
        /// <param name="eventData">构造时即完成快照的不可变事件载荷。</param>
        void Invoke<TEvent>(TEvent eventData) where TEvent : IEvent;
    }

    /// <summary>由 Core 托管的全局同步事件总线，按事件具体类型保存监听通道并在释放时统一清理。</summary>
    internal sealed class EventKit : Kit, IEventKit
    {
        /// <summary>按事件具体类型保存监听通道；通道数量以项目中的事件类型数为上限，因此不随订阅次数增长。</summary>
        private readonly Dictionary<Type, EventChannel> channels = new Dictionary<Type, EventChannel>();

        /// <summary>记录事件总线是否已经释放，阻止失效实例继续收发事件。</summary>
        private bool isDisposed;

        /// <inheritdoc />
        public void AddListener<TEvent>(Action<TEvent> callback) where TEvent : IEvent
        {
            ThrowIfDisposed();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            ResolveChannel<TEvent>(createIfMissing: true).Add(callback);
        }

        /// <inheritdoc />
        public void RemoveListener<TEvent>(Action<TEvent> callback) where TEvent : IEvent
        {
            ThrowIfDisposed();
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            ResolveChannel<TEvent>(createIfMissing: false)?.Remove(callback);
        }

        /// <inheritdoc />
        public void Invoke<TEvent>(TEvent eventData) where TEvent : IEvent
        {
            ThrowIfDisposed();
            ResolveChannel<TEvent>(createIfMissing: false)?.Invoke(eventData);
        }

        /// <summary>清空全部监听通道，使释放后的总线不再持有任何订阅者引用。</summary>
        public override void Dispose()
        {
            if (isDisposed) return;
            channels.Clear();
            isDisposed = true;
        }

        /// <summary>
        /// 取得指定事件类型的强类型通道。
        /// 同一事件类型在字典中只会出现一次，因此这里的向下转换在任何注册路径下都必然成立。
        /// </summary>
        /// <typeparam name="TEvent">需要解析通道的事件载荷类型。</typeparam>
        /// <param name="createIfMissing">发布和退订不需要创建通道，只有订阅需要。</param>
        /// <returns>对应事件类型的通道；不创建且尚未订阅过时返回空。</returns>
        private EventChannel<TEvent> ResolveChannel<TEvent>(bool createIfMissing) where TEvent : IEvent
        {
            Type eventType = typeof(TEvent);
            if (channels.TryGetValue(eventType, out EventChannel channel)) return (EventChannel<TEvent>)channel;
            if (!createIfMissing) return null;
            EventChannel<TEvent> createdChannel = new EventChannel<TEvent>();
            channels.Add(eventType, createdChannel);
            return createdChannel;
        }

        /// <summary>阻止已经释放的事件总线继续被订阅或发布。</summary>
        private void ThrowIfDisposed()
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(EventKit));
        }

        /// <summary>为字典提供与事件类型无关的通道基类，使强类型通道可以统一保存。</summary>
        private abstract class EventChannel
        {
        }

        /// <summary>
        /// 保存单一事件类型的全部监听器。
        /// 监听器数组采用写时复制：订阅和退订各分配一次（只发生在生命周期边界），
        /// 发布只捕获当前数组引用并按下标遍历，因此逐帧发布不产生任何分配，
        /// 且监听器在回调中订阅或退订不会影响本次发布正在遍历的快照。
        /// </summary>
        /// <typeparam name="TEvent">该通道路由的事件载荷类型。</typeparam>
        private sealed class EventChannel<TEvent> : EventChannel where TEvent : IEvent
        {
            /// <summary>当前生效的监听器快照；只允许整体替换，不允许原地修改。</summary>
            private Action<TEvent>[] handlers = Array.Empty<Action<TEvent>>();

            /// <summary>把监听器追加到新数组末尾，保证通知顺序与订阅顺序一致。</summary>
            public void Add(Action<TEvent> callback)
            {
                Action<TEvent>[] updatedHandlers = new Action<TEvent>[handlers.Length + 1];
                Array.Copy(handlers, updatedHandlers, handlers.Length);
                updatedHandlers[handlers.Length] = callback;
                handlers = updatedHandlers;
            }

            /// <summary>移除首个匹配的监听器；委托相等性按目标实例和方法比较，与订阅时的委托一一对应。</summary>
            public void Remove(Action<TEvent> callback)
            {
                int removedIndex = Array.IndexOf(handlers, callback);
                if (removedIndex < 0) return;
                Action<TEvent>[] updatedHandlers = new Action<TEvent>[handlers.Length - 1];
                Array.Copy(handlers, 0, updatedHandlers, 0, removedIndex);
                Array.Copy(handlers, removedIndex + 1, updatedHandlers, removedIndex, handlers.Length - removedIndex - 1);
                handlers = updatedHandlers;
            }

            /// <summary>
            /// 按订阅顺序同步通知全部监听器。
            /// 单个监听器抛出的异常在此处被记录并隔离：全局事实的订阅者互不相识，
            /// 让其中一个的失败静默吞掉其余订阅者的投递，会产生比异常本身更难定位的状态不一致。
            /// </summary>
            public void Invoke(TEvent eventData)
            {
                Action<TEvent>[] snapshot = handlers;
                for (int index = 0; index < snapshot.Length; index++)
                {
                    try
                    {
                        snapshot[index].Invoke(eventData);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }
            }
        }
    }
}
