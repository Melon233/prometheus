using System;
using NUnit.Framework;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>
    /// Verifies the pure capability lease semantics without depending on Unity runtime objects.
    /// </summary>
    [TestFixture]
    public sealed class CoreCapabilityRegistryTests
    {
        /// <summary>
        /// Verifies that overlapping leases reference-count each capability independently.
        /// </summary>
        [Test]
        public void OverlappingLeasesKeepCapabilitiesBlockedUntilTheLastLeaseIsReleased()
        {
            var registry = new CapabilityRegistry(ActorCapability.All);
            var firstHandle = registry.AcquireBlock(new object(), ActorCapability.Move | ActorCapability.Rotate);
            var secondHandle = registry.AcquireBlock(new object(), ActorCapability.Move);

            Assert.That(registry.HasAny(ActorCapability.Move | ActorCapability.Rotate), Is.False);
            Assert.That(registry.BlockedCapabilities, Is.EqualTo(ActorCapability.Move | ActorCapability.Rotate));
            Assert.That(registry.Release(firstHandle), Is.True);
            Assert.That(registry.HasAll(ActorCapability.Rotate), Is.True);
            Assert.That(registry.HasAny(ActorCapability.Move), Is.False);
            Assert.That(registry.Release(secondHandle), Is.True);
            Assert.That(registry.HasAll(ActorCapability.Move | ActorCapability.Rotate), Is.True);
            registry.Dispose();
        }

        /// <summary>
        /// Verifies that a handle is scoped to its issuing registry and can release its lease only once.
        /// </summary>
        [Test]
        public void ReleaseRejectsForeignAndPreviouslyReleasedHandles()
        {
            var firstRegistry = new CapabilityRegistry(ActorCapability.Move);
            var secondRegistry = new CapabilityRegistry(ActorCapability.Move);
            var handle = firstRegistry.AcquireBlock(new object(), ActorCapability.Move);

            Assert.That(handle.IsValid, Is.True);
            Assert.That(secondRegistry.Release(handle), Is.False);
            Assert.That(firstRegistry.IsActive(handle), Is.True);
            Assert.That(firstRegistry.Release(handle), Is.True);
            Assert.That(firstRegistry.Release(handle), Is.False);
            Assert.That(firstRegistry.IsActive(handle), Is.False);
            firstRegistry.Dispose();
            secondRegistry.Dispose();
        }

        /// <summary>
        /// Verifies that grouped cleanup uses owner reference identity instead of overridden value equality.
        /// </summary>
        [Test]
        public void ReleaseAllUsesExactOwnerReferenceIdentity()
        {
            var registry = new CapabilityRegistry(ActorCapability.Move | ActorCapability.Rotate);
            var firstOwner = new ValueEqualOwner(7);
            var secondOwner = new ValueEqualOwner(7);
            var firstOwnerMoveHandle = registry.AcquireBlock(firstOwner, ActorCapability.Move);
            var firstOwnerRotateHandle = registry.AcquireBlock(firstOwner, ActorCapability.Rotate);
            var secondOwnerMoveHandle = registry.AcquireBlock(secondOwner, ActorCapability.Move);

            Assert.That(firstOwner.Equals(secondOwner), Is.True);
            Assert.That(registry.ReleaseAll(firstOwner), Is.EqualTo(2));
            Assert.That(registry.IsActive(firstOwnerMoveHandle), Is.False);
            Assert.That(registry.IsActive(firstOwnerRotateHandle), Is.False);
            Assert.That(registry.IsActive(secondOwnerMoveHandle), Is.True);
            Assert.That(registry.HasAll(ActorCapability.Rotate), Is.True);
            Assert.That(registry.HasAny(ActorCapability.Move), Is.False);
            Assert.That(registry.ReleaseAll(secondOwner), Is.EqualTo(1));
            Assert.That(registry.HasAll(ActorCapability.Move | ActorCapability.Rotate), Is.True);
            registry.Dispose();
        }

        /// <summary>
        /// Verifies that disposal clears leases, remains idempotent, and rejects subsequent acquisition.
        /// </summary>
        [Test]
        public void DisposeClearsAllLeasesAndStopsNewAcquisitions()
        {
            var owner = new object();
            var registry = new CapabilityRegistry(ActorCapability.Move);
            var handle = registry.AcquireBlock(owner, ActorCapability.Move);

            registry.Dispose();
            registry.Dispose();

            Assert.That(registry.IsDisposed, Is.True);
            Assert.That(registry.AvailableCapabilities, Is.EqualTo(ActorCapability.None));
            Assert.That(registry.BlockedCapabilities, Is.EqualTo(ActorCapability.None));
            Assert.That(registry.IsActive(handle), Is.False);
            Assert.That(registry.Release(handle), Is.False);
            Assert.That(registry.ReleaseAll(owner), Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => registry.AcquireBlock(owner, ActorCapability.Move));
        }

        /// <summary>
        /// Verifies that invalid blocking masks cannot create ambiguous or undeclared leases.
        /// </summary>
        [Test]
        public void AcquireBlockRejectsEmptyAndUndeclaredCapabilityMasks()
        {
            var registry = new CapabilityRegistry(ActorCapability.All);
            var undeclaredCapability = (ActorCapability)(1 << 30);

            Assert.Throws<ArgumentOutOfRangeException>(() => registry.AcquireBlock(new object(), ActorCapability.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => registry.AcquireBlock(new object(), undeclaredCapability));
            registry.Dispose();
        }

        /// <summary>
        /// Supplies distinct owner references that intentionally compare equal by value.
        /// </summary>
        private sealed class ValueEqualOwner
        {
            private readonly int value;

            internal ValueEqualOwner(int value)
            {
                this.value = value;
            }

            public override bool Equals(object obj)
            {
                return obj is ValueEqualOwner other && value == other.value;
            }

            public override int GetHashCode()
            {
                return value;
            }
        }
    }
}
