using System;
using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证 Controller、Pawn、分领域租约、稳定优先级和单帧唯一采样协议。</summary>
    public sealed class ActorControlTests
    {
        /// <summary>验证同一帧重复准备不会再次采样控制器，并能把完整租约负载路由给 Pawn。</summary>
        [Test]
        public void PrepareFrame_SamplesEachControllerOnceAndRoutesOwnedScopes()
        {
            PossessionSystem system = new PossessionSystem();
            RecordingController controller = new RecordingController(1, new Vector2(1f, 0.5f), ControlButton.Jump | ControlButton.Attack, ControlButton.Attack);
            system.RegisterController(controller);
            system.RegisterPawn(10);
            system.AcquireLease(new ControlLeaseRequest(1, 10, ControlScope.All, 0));
            Assert.That(system.PrepareFrame(100, 0.016f), Is.True);
            Assert.That(system.PrepareFrame(100, 0.016f), Is.False);
            Assert.That(controller.SampleCount, Is.EqualTo(1));
            Assert.That(system.TryGetControlFrame(10, out ControlFrame frame), Is.True);
            Assert.That(frame.Move, Is.EqualTo(new Vector2(1f, 0.5f)));
            Assert.That(frame.Facing, Is.EqualTo(new Vector2(1f, 0.5f)));
            Assert.That((frame.PressedButtons & ControlButton.Jump) != 0, Is.True);
            Assert.That((frame.PressedButtons & ControlButton.Attack) != 0, Is.True);
            Assert.That((frame.HeldButtons & ControlButton.Attack) != 0, Is.True);
            Assert.That(frame.EffectiveScopes, Is.EqualTo(ControlScope.All));
            system.Dispose();
        }

        /// <summary>验证仅注册但没有任何租约的 Pawn 不会收到伪造空控制帧，使 ActorRuntime 能够可靠切换到 AI 后备控制。</summary>
        [Test]
        public void PrepareFrame_RegisteredPawnWithoutLeaseDoesNotPublishPossessedFrame()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new RecordingController(1, Vector2.zero, ControlButton.None, ControlButton.None));
            system.RegisterPawn(11);

            system.PrepareFrame(1, 0.016f);

            Assert.That(system.TryGetControlFrame(11, out _), Is.False);
            Assert.That(system.HasEffectiveControl(11, ControlScope.Locomotion | ControlScope.Action), Is.False);
            system.AcquireLease(new ControlLeaseRequest(1, 11, ControlScope.All, 0));
            system.PrepareFrame(2, 0.016f);
            Assert.That(system.TryGetControlFrame(11, out ControlFrame possessedFrame), Is.True);
            Assert.That(system.HasEffectiveControl(11, ControlScope.Locomotion | ControlScope.Action), Is.True);
            Assert.That(possessedFrame.HasAnyInput, Is.False);
            Assert.That(possessedFrame.EffectiveScopes, Is.EqualTo(ControlScope.All));
            system.Dispose();
        }

        /// <summary>验证高优先级租约只抢占自己的领域，释放后由低优先级租约在下一帧恢复。</summary>
        [Test]
        public void HigherPriorityLease_PreemptsOnlyItsScopeAndReleaseRestoresFallback()
        {
            PossessionSystem system = new PossessionSystem();
            RecordingController fallback = new RecordingController(1, Vector2.right, ControlButton.Attack, ControlButton.Attack);
            RecordingController overrideController = new RecordingController(2, Vector2.left, ControlButton.Jump, ControlButton.None);
            system.RegisterController(fallback);
            system.RegisterController(overrideController);
            system.RegisterPawn(20);
            system.AcquireLease(new ControlLeaseRequest(1, 20, ControlScope.Locomotion | ControlScope.Facing | ControlScope.Action, 0));
            ControlLeaseHandle overrideHandle = system.AcquireLease(new ControlLeaseRequest(2, 20, ControlScope.Locomotion, 100));
            system.PrepareFrame(1, 0.016f);
            Assert.That(system.TryGetControlFrame(20, out ControlFrame overriddenFrame), Is.True);
            Assert.That(overriddenFrame.Move, Is.EqualTo(Vector2.left));
            Assert.That(overriddenFrame.Facing, Is.EqualTo(Vector2.right));
            Assert.That((overriddenFrame.PressedButtons & ControlButton.Jump) != 0, Is.True);
            Assert.That((overriddenFrame.PressedButtons & ControlButton.Attack) != 0, Is.True);
            Assert.That(system.ReleaseLease(overrideHandle), Is.True);
            Assert.That(system.ReleaseLease(overrideHandle), Is.False);
            system.PrepareFrame(2, 0.016f);
            Assert.That(system.TryGetControlFrame(20, out ControlFrame restoredFrame), Is.True);
            Assert.That(restoredFrame.Move, Is.EqualTo(Vector2.right));
            Assert.That((restoredFrame.PressedButtons & ControlButton.Jump) != 0, Is.False);
            system.Dispose();
        }

        /// <summary>验证相同优先级下更早申请的租约稳定获胜，而不是依赖字典枚举顺序。</summary>
        [Test]
        public void EqualPriorityLeases_EarlierAcquisitionWinsDeterministically()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new RecordingController(1, Vector2.up, ControlButton.None, ControlButton.None));
            system.RegisterController(new RecordingController(2, Vector2.down, ControlButton.None, ControlButton.None));
            system.RegisterPawn(30);
            system.AcquireLease(new ControlLeaseRequest(1, 30, ControlScope.Locomotion, 5));
            system.AcquireLease(new ControlLeaseRequest(2, 30, ControlScope.Locomotion, 5));
            system.PrepareFrame(1, 0.016f);
            Assert.That(system.TryGetEffectiveController(30, ControlScope.Locomotion, out int controllerId), Is.True);
            Assert.That(controllerId, Is.EqualTo(1));
            Assert.That(system.TryGetControlFrame(30, out ControlFrame frame), Is.True);
            Assert.That(frame.Move, Is.EqualTo(Vector2.up));
            system.Dispose();
        }

        /// <summary>验证注销 Pawn 会清除控制帧和全部指向它的租约，旧句柄不能误释放其他对象。</summary>
        [Test]
        public void UnregisterPawn_RemovesFramesAndInvalidatesItsLeases()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new RecordingController(1, Vector2.one, ControlButton.None, ControlButton.None));
            system.RegisterPawn(40);
            ControlLeaseHandle handle = system.AcquireLease(new ControlLeaseRequest(1, 40, ControlScope.All, 0));
            system.PrepareFrame(1, 0.016f);
            Assert.That(system.UnregisterPawn(40), Is.True);
            Assert.That(system.UnregisterPawn(40), Is.False);
            Assert.That(system.TryGetControlFrame(40, out _), Is.False);
            Assert.That(system.ReleaseLease(handle), Is.False);
            system.Dispose();
        }

        /// <summary>验证 Entity 更新阶段发生的租约变更不会删除已经发布的 Pawn 帧，并统一在下一次准备帧生效。</summary>
        [Test]
        public void LeaseMutation_PreservesPublishedFrameUntilNextPrepare()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new RecordingController(1, Vector2.right, ControlButton.Attack, ControlButton.Attack));
            system.RegisterController(new RecordingController(2, Vector2.left, ControlButton.Jump, ControlButton.None));
            system.RegisterPawn(50);
            system.AcquireLease(new ControlLeaseRequest(1, 50, ControlScope.All, 0));
            system.PrepareFrame(1, 0.016f);
            Assert.That(system.TryGetControlFrame(50, out ControlFrame publishedFrame), Is.True);
            Assert.That(publishedFrame.Move, Is.EqualTo(Vector2.right));
            system.AcquireLease(new ControlLeaseRequest(2, 50, ControlScope.Locomotion, 100));
            Assert.That(system.TryGetControlFrame(50, out ControlFrame preservedFrame), Is.True);
            Assert.That(preservedFrame.Move, Is.EqualTo(Vector2.right));
            system.PrepareFrame(2, 0.016f);
            Assert.That(system.TryGetControlFrame(50, out ControlFrame nextFrame), Is.True);
            Assert.That(nextFrame.Move, Is.EqualTo(Vector2.left));
            system.Dispose();
        }

        /// <summary>验证最终帧只声明真实获胜的控制领域，使 ActorRuntime 能让未接管领域继续由后备 AI 驱动。</summary>
        [Test]
        public void PartialLease_PublishesExactEffectiveScopes()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new RecordingController(1, Vector2.left, ControlButton.Attack | ControlButton.Jump, ControlButton.Attack));
            system.RegisterPawn(60);
            system.AcquireLease(new ControlLeaseRequest(1, 60, ControlScope.Facing, 10));

            system.PrepareFrame(1, 0.016f);

            Assert.That(system.TryGetControlFrame(60, out ControlFrame frame), Is.True);
            Assert.That(frame.EffectiveScopes, Is.EqualTo(ControlScope.Facing));
            Assert.That(frame.Move, Is.EqualTo(Vector2.zero));
            Assert.That(frame.Facing, Is.EqualTo(Vector2.left));
            Assert.That(frame.PressedButtons, Is.EqualTo(ControlButton.None));
            system.Dispose();
        }

        /// <summary>验证注销本地控制器会释放全部租约，并允许使用同一稳定编号注册重建后的控制器。</summary>
        [Test]
        public void UnregisterController_ReleasesLeasesAndAllowsStableIdReuse()
        {
            PossessionSystem system = new PossessionSystem();
            system.RegisterController(new RecordingController(1, Vector2.right, ControlButton.None, ControlButton.None));
            system.RegisterPawn(70);
            ControlLeaseHandle staleHandle = system.AcquireLease(new ControlLeaseRequest(1, 70, ControlScope.All, 0));

            Assert.That(system.UnregisterController(1), Is.True);
            Assert.That(system.ReleaseLease(staleHandle), Is.False);
            Assert.That(system.HasEffectiveControl(70, ControlScope.All), Is.False);
            Assert.DoesNotThrow(() => system.RegisterController(new RecordingController(1, Vector2.left, ControlButton.None, ControlButton.None)));
            Assert.DoesNotThrow(() => system.AcquireLease(new ControlLeaseRequest(1, 70, ControlScope.All, 0)));
            system.Dispose();
        }

        /// <summary>提供可记录采样次数和固定输入负载的纯测试控制器，不向正式运行时注入测试入口。</summary>
        private sealed class RecordingController : IControllerRuntime
        {
            /// <summary>固定移动输入。</summary>
            private readonly Vector2 move;

            /// <summary>固定瞬时按钮。</summary>
            private readonly ControlButton pressed;

            /// <summary>固定保持按钮。</summary>
            private readonly ControlButton held;

            /// <summary>创建一个固定输出的测试控制器。</summary>
            internal RecordingController(int controllerId, Vector2 move, ControlButton pressed, ControlButton held)
            {
                ControllerId = controllerId;
                this.move = move;
                this.pressed = pressed;
                this.held = held;
            }

            /// <inheritdoc />
            public int ControllerId { get; }

            /// <summary>获取 Sample 被调用的总次数。</summary>
            internal int SampleCount { get; private set; }

            /// <inheritdoc />
            public ControlFrame Sample(ControllerSampleContext context)
            {
                SampleCount++;
                return new ControlFrame(context.FrameId, 0, move, move, pressed, held);
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}
