using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Identifies one capability-blocking lease without exposing its implementation token.
    /// </summary>
    public readonly struct CapabilityBlockHandle : IEquatable<CapabilityBlockHandle>
    {
        private readonly CapabilityRegistry registry;
        private readonly long token;

        internal CapabilityBlockHandle(CapabilityRegistry registry, long token)
        {
            this.registry = registry;
            this.token = token;
        }

        /// <summary>
        /// Gets whether this value identifies a lease source; it does not indicate that the lease is still active.
        /// </summary>
        public bool IsValid => registry != null && token > 0L;

        internal CapabilityRegistry Registry => registry;

        internal long Token => token;

        /// <summary>
        /// Determines whether this handle identifies the same lease as another handle.
        /// </summary>
        /// <param name="other">The handle to compare.</param>
        /// <returns><see langword="true"/> when both handles identify the same lease; otherwise, <see langword="false"/>.</returns>
        public bool Equals(CapabilityBlockHandle other)
        {
            return ReferenceEquals(registry, other.registry) && token == other.token;
        }

        /// <summary>
        /// Determines whether the supplied object is an equal capability-block handle.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns><see langword="true"/> when the object is an equal handle; otherwise, <see langword="false"/>.</returns>
        public override bool Equals(object obj)
        {
            return obj is CapabilityBlockHandle other && Equals(other);
        }

        /// <summary>
        /// Returns a hash code based on the issuing registry identity and the opaque lease token.
        /// </summary>
        /// <returns>The handle hash code.</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((registry == null ? 0 : RuntimeHelpers.GetHashCode(registry)) * 397) ^ token.GetHashCode();
            }
        }

        /// <summary>
        /// Determines whether two handles identify the same lease.
        /// </summary>
        /// <param name="left">The left handle.</param>
        /// <param name="right">The right handle.</param>
        /// <returns><see langword="true"/> when the handles are equal; otherwise, <see langword="false"/>.</returns>
        public static bool operator ==(CapabilityBlockHandle left, CapabilityBlockHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two handles identify different leases.
        /// </summary>
        /// <param name="left">The left handle.</param>
        /// <param name="right">The right handle.</param>
        /// <returns><see langword="true"/> when the handles are different; otherwise, <see langword="false"/>.</returns>
        public static bool operator !=(CapabilityBlockHandle left, CapabilityBlockHandle right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Tracks an actor's default capabilities and reference-counted blocking leases on the gameplay simulation thread.
    /// </summary>
    public sealed class CapabilityRegistry : IDisposable
    {
        private readonly Dictionary<long, BlockRecord> blocksByToken = new Dictionary<long, BlockRecord>();
        private readonly Dictionary<object, HashSet<long>> blockTokensByOwner = new Dictionary<object, HashSet<long>>(ReferenceEqualityComparer.Instance);
        private readonly int[] blockCounts = new int[32];
        private ActorCapability blockedCapabilities;
        private long nextToken = 1L;
        private bool isDisposed;

        /// <summary>
        /// Initializes a capability registry with the capabilities naturally supported by its actor.
        /// </summary>
        /// <param name="defaultCapabilities">The actor capabilities available while no active lease blocks them.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the mask contains an undeclared capability bit.</exception>
        public CapabilityRegistry(ActorCapability defaultCapabilities)
        {
            ValidateDeclaredCapabilities(defaultCapabilities, nameof(defaultCapabilities));
            DefaultCapabilities = defaultCapabilities;
        }

        /// <summary>
        /// Gets the immutable capability mask configured for the actor.
        /// </summary>
        public ActorCapability DefaultCapabilities { get; }

        /// <summary>
        /// Gets the union of capabilities blocked by active leases, or <see cref="ActorCapability.None"/> after disposal.
        /// </summary>
        public ActorCapability BlockedCapabilities => isDisposed ? ActorCapability.None : blockedCapabilities;

        /// <summary>
        /// Gets the currently available default capabilities, or <see cref="ActorCapability.None"/> after disposal.
        /// </summary>
        public ActorCapability AvailableCapabilities => isDisposed ? ActorCapability.None : DefaultCapabilities & ~blockedCapabilities;

        /// <summary>
        /// Gets whether this registry has released all leases and stopped accepting new leases.
        /// </summary>
        public bool IsDisposed => isDisposed;

        /// <summary>
        /// Determines whether every requested capability is currently available.
        /// </summary>
        /// <param name="capabilities">The capability mask to test.</param>
        /// <returns><see langword="true"/> when the registry is active and every requested capability is available; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the mask contains an undeclared capability bit.</exception>
        public bool HasAll(ActorCapability capabilities)
        {
            ValidateDeclaredCapabilities(capabilities, nameof(capabilities));
            return !isDisposed && (AvailableCapabilities & capabilities) == capabilities;
        }

        /// <summary>
        /// Determines whether at least one requested capability is currently available.
        /// </summary>
        /// <param name="capabilities">The capability mask to test.</param>
        /// <returns><see langword="true"/> when the registry is active and at least one requested capability is available; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the mask contains an undeclared capability bit.</exception>
        public bool HasAny(ActorCapability capabilities)
        {
            ValidateDeclaredCapabilities(capabilities, nameof(capabilities));
            return !isDisposed && (AvailableCapabilities & capabilities) != ActorCapability.None;
        }

        /// <summary>
        /// Acquires an independent lease that blocks the supplied capabilities until the lease or its owner is released.
        /// </summary>
        /// <param name="owner">The reference-identity owner used for grouped cleanup.</param>
        /// <param name="capabilities">One or more declared capabilities to block.</param>
        /// <returns>An opaque handle that can release only this lease.</returns>
        /// <exception cref="ObjectDisposedException">Thrown when the registry has already been disposed.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the mask is empty or contains an undeclared capability bit.</exception>
        public CapabilityBlockHandle AcquireBlock(object owner, ActorCapability capabilities)
        {
            ThrowIfDisposed();
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            ValidateDeclaredCapabilities(capabilities, nameof(capabilities));
            if (capabilities == ActorCapability.None)
            {
                throw new ArgumentOutOfRangeException(nameof(capabilities), capabilities, "A blocking lease must contain at least one capability.");
            }

            if (nextToken == long.MaxValue)
            {
                throw new InvalidOperationException("The capability lease token space has been exhausted.");
            }

            var token = nextToken++;
            if (!blockTokensByOwner.TryGetValue(owner, out var ownerTokens))
            {
                ownerTokens = new HashSet<long>();
                blockTokensByOwner.Add(owner, ownerTokens);
            }

            blocksByToken.Add(token, new BlockRecord(owner, capabilities));
            ownerTokens.Add(token);
            IncrementBlockCounts(capabilities);
            return new CapabilityBlockHandle(this, token);
        }

        /// <summary>
        /// Releases the lease identified by a handle.
        /// </summary>
        /// <param name="handle">The lease handle returned by this registry.</param>
        /// <returns><see langword="true"/> when an active lease was released; <see langword="false"/> for an invalid, foreign, previously released, or disposed handle.</returns>
        public bool Release(CapabilityBlockHandle handle)
        {
            if (isDisposed || !handle.IsValid || !ReferenceEquals(handle.Registry, this) || !blocksByToken.TryGetValue(handle.Token, out var record))
            {
                return false;
            }

            blocksByToken.Remove(handle.Token);
            if (blockTokensByOwner.TryGetValue(record.Owner, out var ownerTokens))
            {
                ownerTokens.Remove(handle.Token);
                if (ownerTokens.Count == 0)
                {
                    blockTokensByOwner.Remove(record.Owner);
                }
            }

            DecrementBlockCounts(record.Capabilities);
            return true;
        }

        /// <summary>
        /// Releases every active lease owned by the exact supplied object reference.
        /// </summary>
        /// <param name="owner">The lease owner reference to release.</param>
        /// <returns>The number of active leases released, or zero after disposal or when the owner has no leases.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="owner"/> is <see langword="null"/>.</exception>
        public int ReleaseAll(object owner)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            if (isDisposed || !blockTokensByOwner.TryGetValue(owner, out var ownerTokens))
            {
                return 0;
            }

            var tokens = new long[ownerTokens.Count];
            ownerTokens.CopyTo(tokens);
            var releasedCount = 0;
            for (var index = 0; index < tokens.Length; index++)
            {
                if (Release(new CapabilityBlockHandle(this, tokens[index])))
                {
                    releasedCount++;
                }
            }

            return releasedCount;
        }

        /// <summary>
        /// Determines whether a handle still identifies an active lease in this registry.
        /// </summary>
        /// <param name="handle">The lease handle to inspect.</param>
        /// <returns><see langword="true"/> only when the handle belongs to this registry and remains active.</returns>
        public bool IsActive(CapabilityBlockHandle handle)
        {
            return !isDisposed && handle.IsValid && ReferenceEquals(handle.Registry, this) && blocksByToken.ContainsKey(handle.Token);
        }

        /// <summary>
        /// Releases all active leases and permanently stops this registry from accepting new leases.
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            blocksByToken.Clear();
            blockTokensByOwner.Clear();
            Array.Clear(blockCounts, 0, blockCounts.Length);
            blockedCapabilities = ActorCapability.None;
        }

        private static void ValidateDeclaredCapabilities(ActorCapability capabilities, string parameterName)
        {
            if ((capabilities & ~ActorCapability.All) != ActorCapability.None)
            {
                throw new ArgumentOutOfRangeException(parameterName, capabilities, "The capability mask contains one or more undeclared bits.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (isDisposed)
            {
                throw new ObjectDisposedException(nameof(CapabilityRegistry));
            }
        }

        private void IncrementBlockCounts(ActorCapability capabilities)
        {
            for (var bitIndex = 0; bitIndex < blockCounts.Length; bitIndex++)
            {
                var capability = (ActorCapability)(1 << bitIndex);
                if ((capabilities & capability) == ActorCapability.None)
                {
                    continue;
                }

                blockCounts[bitIndex]++;
                blockedCapabilities |= capability;
            }
        }

        private void DecrementBlockCounts(ActorCapability capabilities)
        {
            for (var bitIndex = 0; bitIndex < blockCounts.Length; bitIndex++)
            {
                var capability = (ActorCapability)(1 << bitIndex);
                if ((capabilities & capability) == ActorCapability.None)
                {
                    continue;
                }

                blockCounts[bitIndex]--;
                if (blockCounts[bitIndex] == 0)
                {
                    blockedCapabilities &= ~capability;
                }
            }
        }

        private sealed class BlockRecord
        {
            internal BlockRecord(object owner, ActorCapability capabilities)
            {
                Owner = owner;
                Capabilities = capabilities;
            }

            internal object Owner { get; }

            internal ActorCapability Capabilities { get; }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            private ReferenceEqualityComparer()
            {
            }

            public new bool Equals(object left, object right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }
    }
}
