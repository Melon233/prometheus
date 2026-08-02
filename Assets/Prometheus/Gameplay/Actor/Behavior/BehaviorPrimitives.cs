using System;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Identifies the behavior channel owned by a behavior controller.
    /// </summary>
    public enum BehaviorChannel
    {
        /// <summary>
        /// Represents the single mutually exclusive action channel.
        /// </summary>
        Action = 0
    }

    /// <summary>
    /// Identifies the semantic kind of a simulation clip.
    /// </summary>
    public enum SimulationClipKind
    {
        /// <summary>
        /// Represents an interval in which a hit window is active.
        /// </summary>
        HitWindow = 0,

        /// <summary>
        /// Represents an interval in which one or more actor capabilities are blocked.
        /// </summary>
        CapabilityBlock = 1,

        /// <summary>
        /// Represents a deterministic gameplay event scheduled for one simulation tick.
        /// </summary>
        GameplayEvent = 2,

        /// <summary>
        /// Represents an interval in which authored motion is applied.
        /// </summary>
        Motion = 3
    }

    /// <summary>
    /// Identifies the reason an active behavior stopped.
    /// </summary>
    public enum BehaviorEndReason
    {
        /// <summary>
        /// Indicates that the behavior reached its authored duration.
        /// </summary>
        Completed = 0,

        /// <summary>
        /// Indicates that the behavior was explicitly cancelled.
        /// </summary>
        Cancelled = 1,

        /// <summary>
        /// Indicates that the owning controller was disposed.
        /// </summary>
        Disposed = 2
    }

    /// <summary>
    /// Identifies one concrete execution of a behavior program.
    /// </summary>
    public readonly struct BehaviorHandle : IEquatable<BehaviorHandle>
    {
        private readonly long _instanceId;
        private readonly BehaviorChannel _channel;

        internal BehaviorHandle(long instanceId, BehaviorChannel channel)
        {
            _instanceId = instanceId;
            _channel = channel;
        }

        /// <summary>
        /// Gets the monotonically increasing instance identifier assigned by the controller.
        /// </summary>
        public long InstanceId => _instanceId;

        /// <summary>
        /// Gets the channel occupied by this behavior instance.
        /// </summary>
        public BehaviorChannel Channel => _channel;

        /// <summary>
        /// Gets a value indicating whether this handle identifies a behavior instance.
        /// </summary>
        public bool IsValid => _instanceId > 0;

        /// <summary>
        /// Determines whether this handle identifies the same behavior instance as another handle.
        /// </summary>
        /// <param name="other">The handle to compare with this handle.</param>
        /// <returns><see langword="true"/> when both handles identify the same instance and channel; otherwise, <see langword="false"/>.</returns>
        public bool Equals(BehaviorHandle other)
        {
            return _instanceId == other._instanceId && _channel == other._channel;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is BehaviorHandle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (_instanceId.GetHashCode() * 397) ^ (int)_channel;
            }
        }

        /// <summary>
        /// Determines whether two handles identify the same behavior instance.
        /// </summary>
        /// <param name="left">The first handle to compare.</param>
        /// <param name="right">The second handle to compare.</param>
        /// <returns><see langword="true"/> when the handles are equal; otherwise, <see langword="false"/>.</returns>
        public static bool operator ==(BehaviorHandle left, BehaviorHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two handles identify different behavior instances.
        /// </summary>
        /// <param name="left">The first handle to compare.</param>
        /// <param name="right">The second handle to compare.</param>
        /// <returns><see langword="true"/> when the handles are different; otherwise, <see langword="false"/>.</returns>
        public static bool operator !=(BehaviorHandle left, BehaviorHandle right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return IsValid ? $"{_channel}:{_instanceId}" : "Invalid";
        }
    }

    /// <summary>
    /// Describes an immutable Q16 behavior phase snapshot.
    /// </summary>
    public readonly struct BehaviorPhase : IEquatable<BehaviorPhase>
    {
        /// <summary>
        /// Defines the number of fractional bits in the Q16 representation.
        /// </summary>
        public const int FractionBits = 16;

        /// <summary>
        /// Defines the raw Q16 value that represents a rate of one behavior tick per simulation step.
        /// </summary>
        public const int One = 1 << FractionBits;

        /// <summary>
        /// Defines the mask used to extract the fractional portion of a raw Q16 value.
        /// </summary>
        public const int FractionMask = One - 1;

        internal BehaviorPhase(long rawValue, int rateRaw)
        {
            RawValue = rawValue;
            RateRaw = rateRaw;
        }

        /// <summary>
        /// Gets the complete elapsed phase in raw Q16 units.
        /// </summary>
        public long RawValue { get; }

        /// <summary>
        /// Gets the current whole behavior tick.
        /// </summary>
        public int Tick => checked((int)(RawValue >> FractionBits));

        /// <summary>
        /// Gets the fractional Q16 portion within the current behavior tick.
        /// </summary>
        public int FractionRaw => (int)(RawValue & FractionMask);

        /// <summary>
        /// Gets the configured behavior playback rate in raw Q16 units per simulation step.
        /// </summary>
        public int RateRaw { get; }

        /// <summary>
        /// Converts a positive rational rate to its deterministic raw Q16 representation using integer truncation.
        /// </summary>
        /// <param name="numerator">The positive numerator of the rate.</param>
        /// <param name="denominator">The positive denominator of the rate.</param>
        /// <returns>The positive raw Q16 rate.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when either argument is not positive, the represented rate is smaller than one raw Q16 unit, or the result exceeds <see cref="int.MaxValue"/>.</exception>
        public static int RateFromRatio(int numerator, int denominator)
        {
            if (numerator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "The rate numerator must be positive.");
            }

            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "The rate denominator must be positive.");
            }

            long rawRate = (long)numerator * One / denominator;
            if (rawRate <= 0 || rawRate > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(numerator), numerator, "The represented Q16 rate must fit in a positive Int32 value.");
            }

            return (int)rawRate;
        }

        /// <summary>
        /// Determines whether this phase equals another phase snapshot.
        /// </summary>
        /// <param name="other">The phase snapshot to compare with this phase.</param>
        /// <returns><see langword="true"/> when both snapshots contain the same phase and rate; otherwise, <see langword="false"/>.</returns>
        public bool Equals(BehaviorPhase other)
        {
            return RawValue == other.RawValue && RateRaw == other.RateRaw;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is BehaviorPhase other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (RawValue.GetHashCode() * 397) ^ RateRaw;
            }
        }

        /// <summary>
        /// Determines whether two phase snapshots are equal.
        /// </summary>
        /// <param name="left">The first phase snapshot to compare.</param>
        /// <param name="right">The second phase snapshot to compare.</param>
        /// <returns><see langword="true"/> when the snapshots are equal; otherwise, <see langword="false"/>.</returns>
        public static bool operator ==(BehaviorPhase left, BehaviorPhase right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two phase snapshots are different.
        /// </summary>
        /// <param name="left">The first phase snapshot to compare.</param>
        /// <param name="right">The second phase snapshot to compare.</param>
        /// <returns><see langword="true"/> when the snapshots are different; otherwise, <see langword="false"/>.</returns>
        public static bool operator !=(BehaviorPhase left, BehaviorPhase right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Tick={Tick}, Fraction={FractionRaw}/{One}, RateRaw={RateRaw}";
        }
    }

    /// <summary>
    /// Receives deterministic behavior lifecycle and simulation-clip callbacks without depending on a presentation framework.
    /// Implementations must not throw exceptions or synchronously call mutating methods on the emitting <see cref="BehaviorController"/>.
    /// </summary>
    public interface IBehaviorSimulationSink
    {
        /// <summary>
        /// Handles the start of a behavior instance before any of its tick-zero clips enter.
        /// </summary>
        /// <param name="handle">The handle of the behavior instance.</param>
        /// <param name="program">The immutable program being executed.</param>
        /// <param name="phase">The initial phase snapshot.</param>
        void OnBehaviorStarted(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase);

        /// <summary>
        /// Handles a clip entering its half-open active interval.
        /// </summary>
        /// <param name="handle">The handle of the behavior instance.</param>
        /// <param name="clip">The clip that entered.</param>
        /// <param name="phase">The phase snapshot at the interval boundary.</param>
        void OnClipEntered(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase);

        /// <summary>
        /// Handles a simulation sample for a clip that is active at the reported phase.
        /// </summary>
        /// <param name="handle">The handle of the behavior instance.</param>
        /// <param name="clip">The active clip being sampled.</param>
        /// <param name="phase">The phase snapshot used for this sample.</param>
        void OnClipSampled(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase);

        /// <summary>
        /// Handles a clip leaving its active interval or being forcefully cleaned up with its owning behavior.
        /// </summary>
        /// <param name="handle">The handle of the behavior instance.</param>
        /// <param name="clip">The clip that exited.</param>
        /// <param name="phase">The phase snapshot at which the clip exited.</param>
        /// <param name="reason">The reason the clip stopped being active.</param>
        void OnClipExited(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase, BehaviorEndReason reason);

        /// <summary>
        /// Handles the end of a behavior instance after every active clip has exited exactly once.
        /// </summary>
        /// <param name="handle">The handle of the behavior instance.</param>
        /// <param name="program">The immutable program that stopped executing.</param>
        /// <param name="phase">The final phase snapshot.</param>
        /// <param name="reason">The reason the behavior ended.</param>
        void OnBehaviorEnded(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase, BehaviorEndReason reason);
    }
}
