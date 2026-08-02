using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证统一固定 Tick 系统的确定性分块、稳定顺序、积压、动态注销、异常和释放策略。</summary>
    public sealed class BehaviorActorSimulationSystemTests
    {
        /// <summary>验证相同总时间采用不同帧分块时产生完全相同的 Tick 序列与剩余时间。</summary>
        [Test]
        public void OnBeforeEntityUpdate_WithEquivalentTimeChunks_ProducesEquivalentSimulation()
        {
            var firstParticipant = new RecordingParticipant(1);
            var secondParticipant = new RecordingParticipant(1);
            using (var firstSystem = new ActorSimulationSystem(20, 100))
            using (var secondSystem = new ActorSimulationSystem(20, 100))
            {
                Assert.That(firstSystem.RegisterParticipant(firstParticipant), Is.True);
                Assert.That(secondSystem.RegisterParticipant(secondParticipant), Is.True);
                firstSystem.OnBeforeEntityUpdate(0.25f);
                secondSystem.OnBeforeEntityUpdate(0.10f);
                secondSystem.OnBeforeEntityUpdate(0.05f);
                secondSystem.OnBeforeEntityUpdate(0.10f);
                CollectionAssert.AreEqual(firstParticipant.SimulatedTicks, secondParticipant.SimulatedTicks);
                CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5 }, firstParticipant.SimulatedTicks);
                Assert.That(firstSystem.CurrentTick, Is.EqualTo(secondSystem.CurrentTick));
                Assert.That(firstSystem.AccumulatedTimeSeconds, Is.EqualTo(secondSystem.AccumulatedTimeSeconds).Within(0.000001d));
            }
        }

        /// <summary>验证模拟与表现均严格按照 SimulationId 升序执行，与注册顺序无关。</summary>
        [Test]
        public void StepAndPresent_WithOutOfOrderRegistration_UseStableSimulationIdOrder()
        {
            var callbackOrder = new List<string>();
            var system = new ActorSimulationSystem(60, 4);
            var participant30 = new RecordingParticipant(30, callbackOrder);
            var participant10 = new RecordingParticipant(10, callbackOrder);
            var participant20 = new RecordingParticipant(20, callbackOrder);
            try
            {
                system.RegisterParticipant(participant30);
                system.RegisterParticipant(participant10);
                system.RegisterParticipant(participant20);
                system.StepOneTick();
                system.OnUpdate(0.02f);
                CollectionAssert.AreEqual(new[] { "Simulate:10:1", "Simulate:20:1", "Simulate:30:1", "Present:10", "Present:20", "Present:30" }, callbackOrder);
            }
            finally
            {
                system.Dispose();
            }
        }

        /// <summary>验证多阶段参与者严格执行全体意图、全体运动、全体结算顺序，低编号对象不能在高编号对象移动前提前查询命中。</summary>
        [Test]
        public void StepOneTick_WithPhasedParticipants_CompletesEachGlobalPhaseBeforeNextPhase()
        {
            var callbackOrder = new List<string>();
            using (var system = new ActorSimulationSystem())
            {
                system.RegisterParticipant(new PhasedRecordingParticipant(20, callbackOrder));
                system.RegisterParticipant(new PhasedRecordingParticipant(10, callbackOrder));

                system.StepOneTick();

                CollectionAssert.AreEqual(new[] { "Prepare:10:1", "Prepare:20:1", "Motion:10:1", "Motion:20:1", "Resolve:10:1", "Resolve:20:1", "Commit:10:1", "Commit:20:1" }, callbackOrder);
            }
        }

        /// <summary>验证卡帧限步只限制本次推进数量，未消费的真实时间会在后续零增量帧继续执行。</summary>
        [Test]
        public void OnBeforeEntityUpdate_WhenFrameExceedsStepLimit_RetainsAndConsumesBacklog()
        {
            var participant = new RecordingParticipant(1);
            using (var system = new ActorSimulationSystem(10, 2))
            {
                system.RegisterParticipant(participant);
                system.OnBeforeEntityUpdate(0.55f);
                Assert.That(system.CurrentTick, Is.EqualTo(2));
                Assert.That(system.LastFrameStepCount, Is.EqualTo(2));
                Assert.That(system.PendingTickCount, Is.EqualTo(3));
                Assert.That(system.InterpolationAlpha, Is.EqualTo(1f));
                system.OnUpdate(0.016f);
                Assert.That(participant.PresentationSamples.Count, Is.EqualTo(1));
                Assert.That(participant.PresentationSamples[0].InterpolationAlpha, Is.EqualTo(1f));
                system.OnBeforeEntityUpdate(0f);
                Assert.That(system.CurrentTick, Is.EqualTo(4));
                Assert.That(system.PendingTickCount, Is.EqualTo(1));
                system.OnBeforeEntityUpdate(0f);
                Assert.That(system.CurrentTick, Is.EqualTo(5));
                Assert.That(system.LastFrameStepCount, Is.EqualTo(1));
                Assert.That(system.PendingTickCount, Is.Zero);
                Assert.That(system.AccumulatedTimeSeconds, Is.EqualTo(0.05d).Within(0.000001d));
                Assert.That(system.InterpolationAlpha, Is.EqualTo(0.5f).Within(0.00001f));
                CollectionAssert.AreEqual(new long[] { 1, 2, 3, 4, 5 }, participant.SimulatedTicks);
            }
        }

        /// <summary>验证同对象重复注册幂等、不同对象的编号冲突失败，以及重复注销安全返回 false。</summary>
        [Test]
        public void RegisterAndUnregister_WithDuplicates_ApplyDocumentedConflictPolicy()
        {
            var firstParticipant = new RecordingParticipant(7);
            var conflictingParticipant = new RecordingParticipant(7);
            using (var system = new ActorSimulationSystem())
            {
                Assert.That(system.RegisterParticipant(firstParticipant), Is.True);
                Assert.That(system.RegisterParticipant(firstParticipant), Is.False);
                Assert.Throws<InvalidOperationException>(() => system.RegisterParticipant(conflictingParticipant));
                Assert.That(system.ParticipantCount, Is.EqualTo(1));
                Assert.That(system.UnregisterParticipant(7), Is.True);
                Assert.That(system.UnregisterParticipant(7), Is.False);
                Assert.That(system.RegisterParticipant(conflictingParticipant), Is.True);
                Assert.That(system.ParticipantCount, Is.EqualTo(1));
            }
        }

        /// <summary>验证参与者可以在模拟和表现回调中立即注销尚未执行的对象，且不会破坏稳定快照。</summary>
        [Test]
        public void UnregisterParticipant_DuringDispatch_SkipsPendingCallbacksSafely()
        {
            var callbackOrder = new List<string>();
            var system = new ActorSimulationSystem(60, 4);
            var firstParticipant = new RecordingParticipant(1, callbackOrder);
            var secondParticipant = new RecordingParticipant(2, callbackOrder);
            var thirdParticipant = new RecordingParticipant(3, callbackOrder);
            firstParticipant.SimulateAction = (_, __) => system.UnregisterParticipant(2);
            firstParticipant.PresentAction = (_, __) => system.UnregisterParticipant(3);
            try
            {
                system.RegisterParticipant(thirdParticipant);
                system.RegisterParticipant(secondParticipant);
                system.RegisterParticipant(firstParticipant);
                system.StepOneTick();
                CollectionAssert.AreEqual(new[] { "Simulate:1:1", "Simulate:3:1" }, callbackOrder);
                Assert.That(system.ParticipantCount, Is.EqualTo(2));
                callbackOrder.Clear();
                system.OnUpdate(0.016f);
                CollectionAssert.AreEqual(new[] { "Present:1" }, callbackOrder);
                Assert.That(system.ParticipantCount, Is.EqualTo(1));
            }
            finally
            {
                system.Dispose();
            }
        }

        /// <summary>验证 Tick 后置订阅者只在全部参与者完成结算后执行，使 EffectRuntime 可以安全共享 Actor 固定模拟时钟。</summary>
        [Test]
        public void StepOneTick_NotifiesPostResolutionSubscribersAfterEveryActorResolved()
        {
            ActorSimulationSystem system = new ActorSimulationSystem();
            List<string> order = new List<string>();
            system.RegisterParticipant(new PhasedRecordingParticipant(2, order));
            system.RegisterParticipant(new PhasedRecordingParticipant(1, order));
            system.SimulationTickCompleted += (tick, dt) => order.Add($"post:{tick}:{dt:F4}");

            system.StepOneTick();

            CollectionAssert.AreEqual(new[] { "Prepare:1:1", "Prepare:2:1", "Motion:1:1", "Motion:2:1", "Resolve:1:1", "Resolve:2:1", "Commit:1:1", "Commit:2:1", "post:1:0.0167" }, order);
            system.Dispose();
        }

        /// <summary>验证参与者异常不会阻止同一 Tick 中后续对象执行，Tick 只提交一次，并可在移除故障对象后继续推进。</summary>
        [Test]
        public void StepOneTick_WhenParticipantThrows_ContinuesStableDispatchAndCommitsTickOnce()
        {
            var callbackOrder = new List<string>();
            var failingParticipant = new RecordingParticipant(1, callbackOrder);
            var healthyParticipant = new RecordingParticipant(2, callbackOrder);
            failingParticipant.SimulateAction = (_, __) => throw new InvalidOperationException("simulation failure");
            using (var system = new ActorSimulationSystem())
            {
                system.RegisterParticipant(failingParticipant);
                system.RegisterParticipant(healthyParticipant);
                AggregateException exception = Assert.Throws<AggregateException>(() => system.StepOneTick());
                Assert.That(exception.InnerExceptions.Count, Is.EqualTo(1));
                CollectionAssert.AreEqual(new[] { "Simulate:1:1", "Simulate:2:1" }, callbackOrder);
                Assert.That(system.CurrentTick, Is.EqualTo(1));
                Assert.That(system.UnregisterParticipant(1), Is.True);
                system.StepOneTick();
                Assert.That(system.CurrentTick, Is.EqualTo(2));
                CollectionAssert.AreEqual(new long[] { 1, 2 }, healthyParticipant.SimulatedTicks);
            }
        }

        /// <summary>验证 OnBeforeEntityUpdate 遇到参与者异常时消费当前 Tick 时间，但保留尚未处理的积压。</summary>
        [Test]
        public void OnBeforeEntityUpdate_WhenParticipantThrows_RetainsUnprocessedBacklog()
        {
            var failingParticipant = new RecordingParticipant(1);
            failingParticipant.SimulateAction = (_, __) => throw new InvalidOperationException("simulation failure");
            using (var system = new ActorSimulationSystem(10, 4))
            {
                system.RegisterParticipant(failingParticipant);
                Assert.Throws<AggregateException>(() => system.OnBeforeEntityUpdate(0.35f));
                Assert.That(system.CurrentTick, Is.EqualTo(1));
                Assert.That(system.LastFrameStepCount, Is.EqualTo(1));
                Assert.That(system.PendingTickCount, Is.EqualTo(2));
                Assert.That(system.AccumulatedTimeSeconds, Is.EqualTo(0.25d).Within(0.000001d));
            }
        }

        /// <summary>验证表现异常会在完整稳定分发后聚合抛出，健康参与者仍然收到相同插值参数。</summary>
        [Test]
        public void OnUpdate_WhenParticipantThrows_ContinuesPresentationAndAggregatesFailure()
        {
            var failingParticipant = new RecordingParticipant(1);
            var healthyParticipant = new RecordingParticipant(2);
            failingParticipant.PresentAction = (_, __) => throw new InvalidOperationException("presentation failure");
            using (var system = new ActorSimulationSystem(10, 4))
            {
                system.RegisterParticipant(failingParticipant);
                system.RegisterParticipant(healthyParticipant);
                system.OnBeforeEntityUpdate(0.05f);
                AggregateException exception = Assert.Throws<AggregateException>(() => system.OnUpdate(0.016f));
                Assert.That(exception.InnerExceptions.Count, Is.EqualTo(1));
                Assert.That(healthyParticipant.PresentationSamples.Count, Is.EqualTo(1));
                Assert.That(healthyParticipant.PresentationSamples[0].FrameDeltaTime, Is.EqualTo(0.016f));
                Assert.That(healthyParticipant.PresentationSamples[0].InterpolationAlpha, Is.EqualTo(0.5f).Within(0.00001f));
            }
        }

        /// <summary>验证 Dispose 幂等清理注册表与积压，生命周期回调随后安全空转，而正式操作拒绝继续执行。</summary>
        [Test]
        public void Dispose_WhenCalledRepeatedly_IsIdempotentAndStopsFutureSimulation()
        {
            var participant = new RecordingParticipant(1);
            var system = new ActorSimulationSystem(10, 2);
            system.RegisterParticipant(participant);
            system.OnBeforeEntityUpdate(0.05f);
            system.Dispose();
            system.Dispose();
            Assert.That(system.IsDisposed, Is.True);
            Assert.That(system.ParticipantCount, Is.Zero);
            Assert.That(system.AccumulatedTimeSeconds, Is.Zero);
            system.OnBeforeEntityUpdate(1f);
            system.OnUpdate(1f);
            Assert.That(participant.SimulatedTicks, Is.Empty);
            Assert.That(participant.PresentationSamples, Is.Empty);
            Assert.Throws<ObjectDisposedException>(() => system.StepOneTick());
            Assert.Throws<ObjectDisposedException>(() => system.RegisterParticipant(participant));
            Assert.Throws<ObjectDisposedException>(() => system.UnregisterParticipant(1));
        }

        /// <summary>记录测试所需的固定 Tick 和表现回调数据，并允许注入回调行为。</summary>
        private sealed class RecordingParticipant : IActorSimulationParticipant
        {
            /// <summary>保存跨参与者共享的回调顺序；不需要顺序验证时为空。</summary>
            private readonly IList<string> callbackOrder;

            /// <summary>创建一个记录型参与者。</summary>
            public RecordingParticipant(long simulationId, IList<string> callbackOrder = null)
            {
                SimulationId = simulationId;
                this.callbackOrder = callbackOrder;
            }

            /// <inheritdoc />
            public long SimulationId { get; }

            /// <inheritdoc />
            public bool IsSimulationActive { get; set; } = true;

            /// <summary>获取已经收到的模拟 Tick 编号。</summary>
            public List<long> SimulatedTicks { get; } = new List<long>();

            /// <summary>获取已经收到的表现参数。</summary>
            public List<PresentationSample> PresentationSamples { get; } = new List<PresentationSample>();

            /// <summary>获取或设置记录完成后执行的模拟扩展行为。</summary>
            public Action<long, float> SimulateAction { get; set; }

            /// <summary>获取或设置记录完成后执行的表现扩展行为。</summary>
            public Action<float, float> PresentAction { get; set; }

            /// <inheritdoc />
            public void SimulateTick(long tick, float fixedDeltaTime)
            {
                SimulatedTicks.Add(tick);
                callbackOrder?.Add($"Simulate:{SimulationId}:{tick}");
                SimulateAction?.Invoke(tick, fixedDeltaTime);
            }

            /// <inheritdoc />
            public void Present(float frameDeltaTime, float interpolationAlpha)
            {
                PresentationSamples.Add(new PresentationSample(frameDeltaTime, interpolationAlpha));
                callbackOrder?.Add($"Present:{SimulationId}");
                PresentAction?.Invoke(frameDeltaTime, interpolationAlpha);
            }
        }

        /// <summary>记录四阶段 Tick 回调顺序，并在旧单阶段入口被错误调用时立即失败。</summary>
        private sealed class PhasedRecordingParticipant : IActorPhasedSimulationParticipant
        {
            /// <summary>保存跨参与者共享的阶段回调序列。</summary>
            private readonly IList<string> callbackOrder;

            /// <summary>创建指定稳定模拟编号的多阶段记录参与者。</summary>
            public PhasedRecordingParticipant(long simulationId, IList<string> callbackOrder)
            {
                SimulationId = simulationId;
                this.callbackOrder = callbackOrder;
            }

            /// <inheritdoc />
            public long SimulationId { get; }

            /// <inheritdoc />
            public bool IsSimulationActive => true;

            /// <inheritdoc />
            public void PrepareSimulationTick(long tick, float fixedDeltaTime)
            {
                callbackOrder.Add($"Prepare:{SimulationId}:{tick}");
            }

            /// <inheritdoc />
            public void ApplySimulationMotion(long tick, float fixedDeltaTime)
            {
                callbackOrder.Add($"Motion:{SimulationId}:{tick}");
            }

            /// <inheritdoc />
            public void ResolveSimulationTick(long tick, float fixedDeltaTime)
            {
                callbackOrder.Add($"Resolve:{SimulationId}:{tick}");
            }

            /// <inheritdoc />
            public void CommitSimulationTick(long tick, float fixedDeltaTime)
            {
                callbackOrder.Add($"Commit:{SimulationId}:{tick}");
            }

            /// <inheritdoc />
            public void SimulateTick(long tick, float fixedDeltaTime)
            {
                Assert.Fail("Phased participants must not receive the legacy SimulateTick callback from ActorSimulationSystem.");
            }

            /// <inheritdoc />
            public void Present(float frameDeltaTime, float interpolationAlpha)
            {
            }
        }

        /// <summary>保存一次表现回调的帧时间与插值系数。</summary>
        private readonly struct PresentationSample
        {
            /// <summary>创建一次表现参数快照。</summary>
            public PresentationSample(float frameDeltaTime, float interpolationAlpha)
            {
                FrameDeltaTime = frameDeltaTime;
                InterpolationAlpha = interpolationAlpha;
            }

            /// <summary>获取回调收到的帧增量时间。</summary>
            public float FrameDeltaTime { get; }

            /// <summary>获取回调收到的插值系数。</summary>
            public float InterpolationAlpha { get; }
        }
    }
}
