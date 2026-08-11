using System;
using NUnit.Framework;

namespace Xuan.Prometheus.Tests
{
    /// <summary>验证 EventKit 的多监听器路由、精确退订以及 GameCore 生命周期接入。</summary>
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

        /// <summary>释放测试事件总线并清空当前静态事件入口。</summary>
        [TearDown]
        public void TearDown()
        {
            eventKit?.Dispose();
            eventKit = null;
        }

        /// <summary>验证同一类型事件可以通知多个监听器，并且移除其中一个监听器不会影响其余监听器。</summary>
        [Test]
        public void TypedEvent_CombinesInvokesAndRemovesListeners()
        {
            int firstListenerCalls = 0;
            int secondListenerCalls = 0;
            EntityHpChangedEvent observedEvent = null;
            Action<EntityHpChangedEvent> firstListener = eventData => { firstListenerCalls++; observedEvent = eventData; };
            Action<EntityHpChangedEvent> secondListener = _ => secondListenerCalls++;
            eventKit.AddListener(Event.EntityHpChanged, firstListener);
            eventKit.AddListener(Event.EntityHpChanged, secondListener);
            EntityHpChangedEvent firstEvent = new EntityHpChangedEvent(17, 100f, 75f, 100f);
            eventKit.Invoke(Event.EntityHpChanged, firstEvent);
            Assert.That(firstListenerCalls, Is.EqualTo(1));
            Assert.That(secondListenerCalls, Is.EqualTo(1));
            Assert.That(observedEvent, Is.SameAs(firstEvent));
            Assert.That(observedEvent.EntityId, Is.EqualTo(17));
            eventKit.RemoveListener(Event.EntityHpChanged, firstListener);
            eventKit.Invoke(Event.EntityHpChanged, new EntityHpChangedEvent(17, 75f, 50f, 100f));
            Assert.That(firstListenerCalls, Is.EqualTo(1));
            Assert.That(secondListenerCalls, Is.EqualTo(2));
        }

        /// <summary>验证 UI 打开事件保留具体 HudPanel 类型，并能通过类型辅助方法执行语义化筛选。</summary>
        [Test]
        public void UIPanelOpenedEvent_RoutesConcreteHudPanelType()
        {
            UIPanelOpenedEvent observedEvent = null;
            eventKit.AddListener<UIPanelOpenedEvent>(Event.UIPanelOpened, eventData => observedEvent = eventData);
            UIPanelOpenedEvent openedEvent = new UIPanelOpenedEvent(typeof(HudPanel));
            eventKit.Invoke(Event.UIPanelOpened, openedEvent);
            Assert.That(observedEvent, Is.SameAs(openedEvent));
            Assert.That(observedEvent.PanelType, Is.EqualTo(typeof(HudPanel)));
            Assert.That(observedEvent.Is<HudPanel>(), Is.True);
        }

        /// <summary>验证 GameCore 可以通过 IEventKit 获取全局事件总线，并在逆序释放时清空 Core.Event。</summary>
        [Test]
        public void GameCore_RegistersAndDisposesEventKit()
        {
            GameCore core = new GameCore();
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
    }
}
