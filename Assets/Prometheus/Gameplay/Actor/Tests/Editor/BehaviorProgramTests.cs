using System;
using NUnit.Framework;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>
    /// Verifies immutable program construction and simulation-clip validation.
    /// </summary>
    public sealed class BehaviorProgramTests
    {
        /// <summary>
        /// Verifies that the required hit-window, capability-block, gameplay-event, and motion clip kinds can coexist in one immutable program.
        /// </summary>
        [Test]
        public void Constructor_WithAllRequiredClipKinds_PreservesAuthoredOrderAndPayloads()
        {
            var clips = new SimulationClip[] { new HitWindowClip("hit", 1, 3, "sword"), new CapabilityBlockClip("block", 0, 4, ActorCapability.Move | ActorCapability.Rotate), new GameplayEventClip("event", 2, "spend-resource"), new MotionClip("motion", 0, 4, "forward") };
            var program = new BehaviorProgram("HeavyAttack", 4, clips);
            clips[0] = new MotionClip("replacement", 0, 1, "none");
            Assert.That(program.ProgramId, Is.EqualTo("HeavyAttack"));
            Assert.That(program.Channel, Is.EqualTo(BehaviorChannel.Action));
            Assert.That(program.DurationTicks, Is.EqualTo(4));
            Assert.That(program.SimulationClips.Count, Is.EqualTo(4));
            Assert.That(program.SimulationClips[0], Is.TypeOf<HitWindowClip>());
            Assert.That(program.SimulationClips[1], Is.TypeOf<CapabilityBlockClip>());
            Assert.That(program.SimulationClips[2], Is.TypeOf<GameplayEventClip>());
            Assert.That(program.SimulationClips[3], Is.TypeOf<MotionClip>());
            Assert.That(((HitWindowClip)program.SimulationClips[0]).HitboxId, Is.EqualTo("sword"));
            Assert.That(((CapabilityBlockClip)program.SimulationClips[1]).BlockedCapabilities, Is.EqualTo(ActorCapability.Move | ActorCapability.Rotate));
            Assert.That(((GameplayEventClip)program.SimulationClips[2]).EventId, Is.EqualTo("spend-resource"));
            Assert.That(((MotionClip)program.SimulationClips[3]).MotionId, Is.EqualTo("forward"));
        }

        /// <summary>
        /// Verifies that duplicate stable clip identifiers are rejected to keep runtime callback identity unambiguous.
        /// </summary>
        [Test]
        public void Constructor_WithDuplicateClipIds_ThrowsArgumentException()
        {
            var clips = new SimulationClip[] { new HitWindowClip("duplicate", 0, 1, "a"), new MotionClip("duplicate", 1, 2, "b") };
            Assert.Throws<ArgumentException>(() => new BehaviorProgram("Invalid", 2, clips));
        }

        /// <summary>
        /// Verifies that a clip cannot extend beyond the program duration.
        /// </summary>
        [Test]
        public void Constructor_WithClipBeyondDuration_ThrowsArgumentOutOfRangeException()
        {
            var clips = new SimulationClip[] { new HitWindowClip("hit", 0, 3, "a") };
            Assert.Throws<ArgumentOutOfRangeException>(() => new BehaviorProgram("Invalid", 2, clips));
        }

        /// <summary>
        /// Verifies that every simulation clip enforces a non-negative, non-empty half-open interval.
        /// </summary>
        [Test]
        public void SimulationClip_WithInvalidHalfOpenInterval_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionClip("negative", -1, 1, "motion"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionClip("empty", 1, 1, "motion"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MotionClip("reversed", 2, 1, "motion"));
        }

        /// <summary>
        /// Verifies exact deterministic Q16 conversion for common rational playback rates.
        /// </summary>
        [Test]
        public void RateFromRatio_WithCommonRates_ReturnsExpectedQ16Values()
        {
            Assert.That(BehaviorPhase.RateFromRatio(1, 2), Is.EqualTo(BehaviorPhase.One / 2));
            Assert.That(BehaviorPhase.RateFromRatio(1, 1), Is.EqualTo(BehaviorPhase.One));
            Assert.That(BehaviorPhase.RateFromRatio(2, 1), Is.EqualTo(BehaviorPhase.One * 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => BehaviorPhase.RateFromRatio(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BehaviorPhase.RateFromRatio(1, 0));
        }
    }
}
