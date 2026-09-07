using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Xuan.Prometheus.Tests
{
    /// <summary>验证 EventKit 按事件具体类型路由、精确退订、异常隔离、发布期快照以及 Core 生命周期接入。</summary>
    public sealed class EventKitTests
    {
        /// <summary>保存每个测试独占的事件总线，避免静态 Core.Event 在测试之间泄漏。</summary>
        private EventKit eventKit;

        /// <summary>为每个测试创建一条独立的全局事件总线。</summary>
        [SetUp]
        public void SetUp()
        {
            eventKit = new EventKit();
        }

        /// <summary>释放测试事件总线。</summary>
        [TearDown]
        public void TearDown()
        {
            eventKit?.Dispose();
            eventKit = null;
        }

        /// <summary>验证同一类型事件可以通知多个监听器，并且移除其中一个不会影响其余监听器。</summary>
        [Test]
        public void TypedEvent_RoutesToAllListenersAndRemovesPrecisely()
        {
            int firstListenerCalls = 0;
            int secondListenerCalls = 0;
            ProbeEvent observedEvent = null;
            Action<ProbeEvent> firstListener = eventData => { firstListenerCalls++; observedEvent = eventData; };
            Action<ProbeEvent> secondListener = _ => secondListenerCalls++;
            eventKit.AddListener(firstListener);
            eventKit.AddListener(secondListener);
            ProbeEvent firstEvent = new ProbeEvent(17);
            eventKit.Invoke(firstEvent);
            Assert.That(firstListenerCalls, Is.EqualTo(1));
            Assert.That(secondListenerCalls, Is.EqualTo(1));
            Assert.That(observedEvent, Is.SameAs(firstEvent));
            eventKit.RemoveListener(firstListener);
            eventKit.Invoke(new ProbeEvent(18));
            Assert.That(firstListenerCalls, Is.EqualTo(1));
            Assert.That(secondListenerCalls, Is.EqualTo(2));
        }

        /// <summary>验证不同事件类型互不串扰，且发布一个没有监听器的事件类型是合法空操作。</summary>
        [Test]
        public void DistinctEventTypes_DoNotCrossTalk()
        {
            int probeCalls = 0;
            int otherCalls = 0;
            eventKit.AddListener<ProbeEvent>(_ => probeCalls++);
            eventKit.AddListener<OtherProbeEvent>(_ => otherCalls++);
            eventKit.Invoke(new ProbeEvent(1));
            Assert.That(probeCalls, Is.EqualTo(1));
            Assert.That(otherCalls, Is.EqualTo(0));
            eventKit.Invoke(new OtherProbeEvent());
            Assert.That(probeCalls, Is.EqualTo(1));
            Assert.That(otherCalls, Is.EqualTo(1));
            Assert.DoesNotThrow(() => eventKit.Invoke(new UnobservedProbeEvent()));
        }

        /// <summary>验证单个监听器抛出的异常被记录并隔离，同一事件的其余监听器仍然完成投递。</summary>
        [Test]
        public void ThrowingListener_IsIsolatedFromOtherListeners()
        {
            LogAssert.Expect(LogType.Exception, new Regex("listener failure"));
            int survivingListenerCalls = 0;
            eventKit.AddListener<ProbeEvent>(_ => throw new InvalidOperationException("listener failure"));
            eventKit.AddListener<ProbeEvent>(_ => survivingListenerCalls++);
            eventKit.Invoke(new ProbeEvent(1));
            Assert.That(survivingListenerCalls, Is.EqualTo(1));
        }

        /// <summary>验证监听器在回调中订阅或退订不会影响本次发布正在遍历的快照，新订阅从下一次发布开始生效。</summary>
        [Test]
        public void SubscriptionChangesDuringPublish_ApplyToNextPublishOnly()
        {
            int lateListenerCalls = 0;
            int removedListenerCalls = 0;
            Action<ProbeEvent> lateListener = _ => lateListenerCalls++;
            Action<ProbeEvent> removedListener = _ => removedListenerCalls++;
            eventKit.AddListener<ProbeEvent>(_ =>
            {
                eventKit.AddListener(lateListener);
                eventKit.RemoveListener(removedListener);
            });
            eventKit.AddListener(removedListener);
            eventKit.Invoke(new ProbeEvent(1));
            Assert.That(lateListenerCalls, Is.EqualTo(0), "本次发布已经捕获快照，新监听器不应参与。");
            Assert.That(removedListenerCalls, Is.EqualTo(1), "本次发布已经捕获快照，退订在本次仍然收到通知。");
            eventKit.Invoke(new ProbeEvent(2));
            Assert.That(lateListenerCalls, Is.EqualTo(1));
            Assert.That(removedListenerCalls, Is.EqualTo(1));
        }

        /// <summary>验证释放后的事件总线拒绝继续订阅或发布。</summary>
        [Test]
        public void DisposedEventKit_RejectsFurtherUse()
        {
            eventKit.Dispose();
            Assert.Throws<ObjectDisposedException>(() => eventKit.Invoke(new ProbeEvent(1)));
            Assert.Throws<ObjectDisposedException>(() => eventKit.AddListener<ProbeEvent>(_ => { }));
        }

        /// <summary>验证 Core 可以通过 IEventKit 获取全局事件总线，并在逆序释放时清空 Core.Event。</summary>
        [Test]
        public void Core_RegistersAndDisposesEventKit()
        {
            Core core = new Core();
            try
            {
                IEventKit registeredEventKit = core.GetKit<IEventKit>();
                Assert.That(registeredEventKit, Is.SameAs(Core.Event));
            }
            finally
            {
                core.Dispose();
            }
            Assert.That(Core.Event, Is.Null);
        }

        /// <summary>测试专用事件载荷，使总线行为的验证不依赖任何生产事件类型。</summary>
        private sealed class ProbeEvent : IEvent
        {
            /// <summary>创建一条携带可辨识数值的测试事实。</summary>
            public ProbeEvent(int value) { Value = value; }

            /// <summary>获取本次测试事实携带的数值。</summary>
            public int Value { get; }
        }

        /// <summary>用于验证事件类型之间互不串扰的第二种测试载荷。</summary>
        private sealed class OtherProbeEvent : IEvent
        {
        }

        /// <summary>用于验证发布无监听事件类型是合法空操作的测试载荷。</summary>
        private sealed class UnobservedProbeEvent : IEvent
        {
        }
    }
}
