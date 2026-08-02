using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证 Actor 运行时跨渲染帧与固定 Tick 保存的控制和落地瞬时状态不会泄漏到错误上下文。</summary>
    public sealed class ActorRuntimeStateTests
    {
        /// <summary>验证控制拓扑代数切换会丢弃旧控制者尚未消费的 Pressed，同时保留新控制者的连续输入。</summary>
        [Test]
        public void ControlFrameBuffer_GenerationChangeDropsPreviousPressedButtons()
        {
            var buffer = new ActorControlFrameBuffer();
            buffer.Capture(new ControlFrame(10, 1, Vector2.left, Vector2.left, ControlButton.Attack, ControlButton.Attack));
            buffer.Capture(new ControlFrame(11, 2, Vector2.right, Vector2.up, ControlButton.None, ControlButton.None));

            Assert.That(buffer.TryConsume(true, out ControlFrame consumed), Is.True);
            Assert.That(consumed.PossessionGeneration, Is.EqualTo(2u));
            Assert.That(consumed.Move, Is.EqualTo(Vector2.right));
            Assert.That(consumed.Facing, Is.EqualTo(Vector2.up));
            Assert.That(consumed.PressedButtons, Is.EqualTo(ControlButton.None));
        }

        /// <summary>验证同一控制代数下多个渲染帧的 Pressed 会合并一次，而 Move 与 Facing 始终采用最新采样。</summary>
        [Test]
        public void ControlFrameBuffer_SameGenerationAccumulatesPressedAndConsumesThemOnce()
        {
            var buffer = new ActorControlFrameBuffer();
            buffer.Capture(new ControlFrame(20, 3, Vector2.left, Vector2.down, ControlButton.Attack, ControlButton.Attack));
            buffer.Capture(new ControlFrame(21, 3, Vector2.right, Vector2.up, ControlButton.Skill, ControlButton.Skill));

            Assert.That(buffer.TryConsume(true, out ControlFrame firstTick), Is.True);
            Assert.That(firstTick.Move, Is.EqualTo(Vector2.right));
            Assert.That(firstTick.Facing, Is.EqualTo(Vector2.up));
            Assert.That(firstTick.PressedButtons, Is.EqualTo(ControlButton.Attack | ControlButton.Skill));
            Assert.That(firstTick.HeldButtons, Is.EqualTo(ControlButton.Skill));
            Assert.That(buffer.TryConsume(true, out ControlFrame secondTick), Is.True);
            Assert.That(secondTick.PressedButtons, Is.EqualTo(ControlButton.None));
            Assert.That(secondTick.Move, Is.EqualTo(Vector2.right));
            Assert.That(secondTick.Facing, Is.EqualTo(Vector2.up));
        }

        /// <summary>验证 Pawn 失去控制帧会同时清除连续输入与尚未消费的瞬时按钮，重新接管后不会重放旧攻击。</summary>
        [Test]
        public void ControlFrameBuffer_ClearDropsAllPreviousControlPayload()
        {
            var buffer = new ActorControlFrameBuffer();
            buffer.Capture(new ControlFrame(30, 4, Vector2.one, Vector2.left, ControlButton.Attack, ControlButton.Attack));

            buffer.Clear(31);

            Assert.That(buffer.HasFrame, Is.False);
            Assert.That(buffer.TryConsume(true, out _), Is.False);
            buffer.Capture(new ControlFrame(32, 4, Vector2.zero, Vector2.zero, ControlButton.None, ControlButton.None));
            Assert.That(buffer.TryConsume(true, out ControlFrame reacquired), Is.True);
            Assert.That(reacquired.PressedButtons, Is.EqualTo(ControlButton.None));
        }

        /// <summary>验证 Input Capability 被封锁时不输出移动、朝向或动作，并主动丢弃期间收到的瞬时按钮。</summary>
        [Test]
        public void ControlFrameBuffer_InputCapabilityBlockDropsPressedInsteadOfDeferringIt()
        {
            var buffer = new ActorControlFrameBuffer();
            buffer.Capture(new ControlFrame(40, 5, Vector2.right, Vector2.left, ControlButton.Dodge, ControlButton.Dodge));

            Assert.That(buffer.TryConsume(false, out ControlFrame blockedFrame), Is.False);
            Assert.That(blockedFrame.HasAnyInput, Is.False);
            Assert.That(buffer.TryConsume(true, out ControlFrame restoredFrame), Is.True);
            Assert.That(restoredFrame.Move, Is.EqualTo(Vector2.right));
            Assert.That(restoredFrame.Facing, Is.EqualTo(Vector2.left));
            Assert.That(restoredFrame.PressedButtons, Is.EqualTo(ControlButton.None));
        }

        /// <summary>验证落地锁存即使遇到大于锁定时长的首个渲染帧，也会保证 Land 至少被选择一次。</summary>
        [Test]
        public void LandPresentationState_LargeFirstFrameStillPresentsLandOnce()
        {
            var state = new ActorLandPresentationState();
            state.Trigger(0.12f);

            Assert.That(state.ConsumeFrame(0.5f), Is.True);
            Assert.That(state.ConsumeFrame(0.01f), Is.False);
            Assert.That(state.IsActive, Is.False);
        }

        /// <summary>验证落地状态按渲染时间持续多个帧，而不是被随后执行的固定 Tick 数量覆盖。</summary>
        [Test]
        public void LandPresentationState_ConsumesRenderTimeUntilDurationExpires()
        {
            var state = new ActorLandPresentationState();
            state.Trigger(0.12f);

            Assert.That(state.ConsumeFrame(0.05f), Is.True);
            Assert.That(state.ConsumeFrame(0.05f), Is.True);
            Assert.That(state.ConsumeFrame(0.05f), Is.True);
            Assert.That(state.ConsumeFrame(0.05f), Is.False);
            Assert.That(state.IsActive, Is.False);
        }

        /// <summary>验证新的落地事件只会延长当前锁定，并且高优先级行为可以显式清除该状态。</summary>
        [Test]
        public void LandPresentationState_RetriggerExtendsAndClearCancelsLock()
        {
            var state = new ActorLandPresentationState();
            state.Trigger(0.1f);
            Assert.That(state.ConsumeFrame(0.06f), Is.True);

            state.Trigger(0.2f);

            Assert.That(state.ConsumeFrame(0.15f), Is.True);
            Assert.That(state.ConsumeFrame(0.04f), Is.True);
            Assert.That(state.ConsumeFrame(0.02f), Is.True);
            Assert.That(state.ConsumeFrame(0f), Is.False);
            state.Trigger(0.1f);
            state.Clear();
            Assert.That(state.ConsumeFrame(0f), Is.False);
        }
    }
}
