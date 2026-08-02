using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>
    /// Verifies deterministic behavior execution, cleanup, rate control, re-entry, and handle isolation.
    /// </summary>
    public sealed class BehaviorControllerTests
    {
        /// <summary>
        /// Verifies that normal completion exits every clip once before emitting the behavior-ended callback.
        /// </summary>
        [Test]
        public void Step_WhenProgramCompletes_ExitsEveryClipExactlyOnceBeforeEnding()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Attack", 3, new SimulationClip[] { new HitWindowClip("hit", 0, 2, "weapon"), new MotionClip("motion", 1, 3, "lunge") });
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle handle), Is.True);
                Assert.That(handle.IsValid, Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.False);
                Assert.That(controller.IsActive, Is.False);
            }

            CollectionAssert.AreEqual(new[] { "Started:Attack:0", "Enter:hit:0", "Sample:hit:0", "Enter:motion:1", "Sample:hit:1", "Sample:motion:1", "Exit:hit:2:Completed", "Sample:motion:2", "Exit:motion:3:Completed", "Ended:Attack:3:Completed" }, sink.Events);
            Assert.That(sink.ExitCounts["hit"], Is.EqualTo(1));
            Assert.That(sink.ExitCounts["motion"], Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that cancellation performs reverse-activation cleanup and repeated cancellation produces no duplicate exit callbacks.
        /// </summary>
        [Test]
        public void Cancel_WhenClipsAreActive_ExitsInReverseActivationOrderExactlyOnce()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Hold", 10, new SimulationClip[] { new CapabilityBlockClip("block", 0, 10, ActorCapability.Move), new HitWindowClip("hit", 0, 10, "body") });
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle handle), Is.True);
                Assert.That(controller.Cancel(handle), Is.True);
                Assert.That(controller.Cancel(handle), Is.False);
            }

            CollectionAssert.AreEqual(new[] { "Started:Hold:0", "Enter:block:0", "Enter:hit:0", "Exit:hit:0:Cancelled", "Exit:block:0:Cancelled", "Ended:Hold:0:Cancelled" }, sink.Events);
            Assert.That(sink.ExitCounts["hit"], Is.EqualTo(1));
            Assert.That(sink.ExitCounts["block"], Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that an occupied action channel rejects re-entry while a completed or cancelled channel can start a fresh instance.
        /// </summary>
        [Test]
        public void TryStart_WhenReentered_RejectsWhileActiveAndCreatesFreshHandleAfterEnd()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Action", 1, Array.Empty<SimulationClip>());
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle firstHandle), Is.True);
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle rejectedHandle), Is.False);
                Assert.That(rejectedHandle.IsValid, Is.False);
                Assert.That(controller.Step(), Is.False);
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle secondHandle), Is.True);
                Assert.That(secondHandle, Is.Not.EqualTo(firstHandle));
                Assert.That(secondHandle.InstanceId, Is.GreaterThan(firstHandle.InstanceId));
                Assert.That(controller.Cancel(secondHandle), Is.True);
            }

            CollectionAssert.AreEqual(new[] { "Started:Action:0", "Ended:Action:1:Completed", "Started:Action:0", "Ended:Action:0:Cancelled" }, sink.Events);
        }

        /// <summary>
        /// Verifies half-open boundaries and deterministic exit-before-enter ordering for multiple overlapping windows.
        /// </summary>
        [Test]
        public void Step_WithMultipleWindows_UsesHalfOpenIntervalsAndStableBoundaryOrdering()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Combo", 3, new SimulationClip[] { new HitWindowClip("hit-a", 0, 1, "a"), new HitWindowClip("hit-b", 1, 3, "b"), new GameplayEventClip("event", 1, "commit"), new CapabilityBlockClip("block", 0, 2, ActorCapability.Move | ActorCapability.Rotate) });
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out _), Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.False);
            }

            int exitFirstWindow = sink.Events.IndexOf("Exit:hit-a:1:Completed");
            int enterSecondWindow = sink.Events.IndexOf("Enter:hit-b:1");
            int enterEvent = sink.Events.IndexOf("Enter:event:1");
            Assert.That(exitFirstWindow, Is.GreaterThanOrEqualTo(0));
            Assert.That(enterSecondWindow, Is.GreaterThan(exitFirstWindow));
            Assert.That(enterEvent, Is.GreaterThan(enterSecondWindow));
            Assert.That(sink.SampleCounts["hit-a"], Is.EqualTo(1));
            Assert.That(sink.SampleCounts["hit-b"], Is.EqualTo(2));
            Assert.That(sink.SampleCounts["event"], Is.EqualTo(1));
            Assert.That(sink.ExitCounts["block"], Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a half-speed Q16 rate requires two simulation steps per behavior tick and retains fractional phase snapshots.
        /// </summary>
        [Test]
        public void Step_WithHalfSpeedRate_AdvancesUsingQ16FractionalPhase()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Slow", 2, new SimulationClip[] { new MotionClip("motion", 0, 2, "slow") });
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.RateFromRatio(1, 2), out BehaviorHandle handle), Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.TryGetPhase(handle, out BehaviorPhase halfTick), Is.True);
                Assert.That(halfTick.Tick, Is.EqualTo(0));
                Assert.That(halfTick.FractionRaw, Is.EqualTo(BehaviorPhase.One / 2));
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.False);
            }

            CollectionAssert.AreEqual(new[] { 0, 0, 1, 1 }, sink.SampleTicks["motion"]);
        }

        /// <summary>
        /// Verifies that a double-speed Q16 rate samples every crossed whole behavior tick without skipping a short window.
        /// </summary>
        [Test]
        public void Step_WithDoubleSpeedRate_SamplesEveryCrossedTick()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Fast", 4, new SimulationClip[] { new HitWindowClip("hit", 1, 2, "fast-window"), new MotionClip("motion", 0, 4, "dash") });
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.RateFromRatio(2, 1), out _), Is.True);
                Assert.That(controller.Step(), Is.True);
                Assert.That(controller.Step(), Is.False);
            }

            CollectionAssert.AreEqual(new[] { 1 }, sink.SampleTicks["hit"]);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, sink.SampleTicks["motion"]);
            Assert.That(sink.ExitCounts["hit"], Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that a stale handle cannot cancel a newer behavior instance that reuses the same channel.
        /// </summary>
        [Test]
        public void Cancel_WithStaleHandle_DoesNotAffectNewerInstance()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Repeat", 2, new SimulationClip[] { new CapabilityBlockClip("block", 0, 2, ActorCapability.BasicAttack) });
            using (var controller = new BehaviorController(sink))
            {
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle staleHandle), Is.True);
                Assert.That(controller.Cancel(staleHandle), Is.True);
                Assert.That(controller.TryStart(program, BehaviorPhase.One, out BehaviorHandle currentHandle), Is.True);
                Assert.That(controller.Cancel(staleHandle), Is.False);
                Assert.That(controller.IsActive, Is.True);
                Assert.That(controller.ActiveHandle, Is.EqualTo(currentHandle));
                Assert.That(controller.Cancel(currentHandle), Is.True);
            }

            Assert.That(sink.EndReasons, Is.EqualTo(new[] { BehaviorEndReason.Cancelled, BehaviorEndReason.Cancelled }));
            Assert.That(sink.ExitCounts["block"], Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that disposal cleans up active clips once, is idempotent, and prevents future mutation.
        /// </summary>
        [Test]
        public void Dispose_WhenBehaviorIsActive_CleansUpOnceAndRejectsFutureMutation()
        {
            var sink = new RecordingSink();
            var program = new BehaviorProgram("Dispose", 5, new SimulationClip[] { new MotionClip("motion", 0, 5, "root-motion") });
            var controller = new BehaviorController(sink);
            Assert.That(controller.TryStart(program, BehaviorPhase.One, out _), Is.True);
            controller.Dispose();
            controller.Dispose();
            Assert.That(sink.ExitCounts["motion"], Is.EqualTo(1));
            Assert.That(sink.EndReasons, Is.EqualTo(new[] { BehaviorEndReason.Disposed }));
            Assert.Throws<ObjectDisposedException>(() => controller.Step());
        }

        private sealed class RecordingSink : IBehaviorSimulationSink
        {
            public readonly List<string> Events = new List<string>();
            public readonly Dictionary<string, int> ExitCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, int> SampleCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly Dictionary<string, List<int>> SampleTicks = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            public readonly List<BehaviorEndReason> EndReasons = new List<BehaviorEndReason>();

            public void OnBehaviorStarted(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase)
            {
                Events.Add($"Started:{program.ProgramId}:{phase.Tick}");
            }

            public void OnClipEntered(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
            {
                Events.Add($"Enter:{clip.ClipId}:{phase.Tick}");
            }

            public void OnClipSampled(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
            {
                Events.Add($"Sample:{clip.ClipId}:{phase.Tick}");
                Increment(SampleCounts, clip.ClipId);
                if (!SampleTicks.TryGetValue(clip.ClipId, out List<int> ticks))
                {
                    ticks = new List<int>();
                    SampleTicks.Add(clip.ClipId, ticks);
                }

                ticks.Add(phase.Tick);
            }

            public void OnClipExited(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase, BehaviorEndReason reason)
            {
                Events.Add($"Exit:{clip.ClipId}:{phase.Tick}:{reason}");
                Increment(ExitCounts, clip.ClipId);
            }

            public void OnBehaviorEnded(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase, BehaviorEndReason reason)
            {
                Events.Add($"Ended:{program.ProgramId}:{phase.Tick}:{reason}");
                EndReasons.Add(reason);
            }

            private static void Increment(IDictionary<string, int> values, string key)
            {
                values.TryGetValue(key, out int value);
                values[key] = value + 1;
            }
        }
    }
}
