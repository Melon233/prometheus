using System;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Defines an immutable deterministic simulation interval using <c>[startTick, endTick)</c> semantics.
    /// </summary>
    public abstract class SimulationClip
    {
        /// <summary>
        /// Initializes a new simulation clip.
        /// </summary>
        /// <param name="clipId">The stable identifier that is unique within one behavior program.</param>
        /// <param name="startTick">The inclusive start tick.</param>
        /// <param name="endTick">The exclusive end tick.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="clipId"/> is null, empty, or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the interval is not a valid non-negative half-open interval.</exception>
        protected SimulationClip(string clipId, int startTick, int endTick)
        {
            if (string.IsNullOrWhiteSpace(clipId))
            {
                throw new ArgumentException("A simulation clip requires a non-empty stable identifier.", nameof(clipId));
            }

            if (startTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(startTick), startTick, "A simulation clip cannot start before tick zero.");
            }

            if (endTick <= startTick)
            {
                throw new ArgumentOutOfRangeException(nameof(endTick), endTick, "A simulation clip must end after its start tick.");
            }

            ClipId = clipId;
            StartTick = startTick;
            EndTick = endTick;
        }

        /// <summary>
        /// Gets the stable identifier that is unique within one behavior program.
        /// </summary>
        public string ClipId { get; }

        /// <summary>
        /// Gets the inclusive start tick.
        /// </summary>
        public int StartTick { get; }

        /// <summary>
        /// Gets the exclusive end tick.
        /// </summary>
        public int EndTick { get; }

        /// <summary>
        /// Gets the semantic kind of this clip.
        /// </summary>
        public abstract SimulationClipKind Kind { get; }
    }

    /// <summary>
    /// Defines an interval in which a named hit window is active.
    /// </summary>
    public sealed class HitWindowClip : SimulationClip
    {
        /// <summary>
        /// Initializes a new hit-window clip.
        /// </summary>
        /// <param name="clipId">The stable identifier that is unique within one behavior program.</param>
        /// <param name="startTick">The inclusive start tick.</param>
        /// <param name="endTick">The exclusive end tick.</param>
        /// <param name="hitboxId">The stable hitbox or hit-query profile identifier interpreted by the simulation sink.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="hitboxId"/> is null, empty, or whitespace.</exception>
        public HitWindowClip(string clipId, int startTick, int endTick, string hitboxId) : base(clipId, startTick, endTick)
        {
            if (string.IsNullOrWhiteSpace(hitboxId))
            {
                throw new ArgumentException("A hit-window clip requires a non-empty hitbox identifier.", nameof(hitboxId));
            }

            HitboxId = hitboxId;
        }

        /// <inheritdoc/>
        public override SimulationClipKind Kind => SimulationClipKind.HitWindow;

        /// <summary>
        /// Gets the stable hitbox or hit-query profile identifier interpreted by the simulation sink.
        /// </summary>
        public string HitboxId { get; }
    }

    /// <summary>
    /// Defines an interval in which selected actor capabilities are blocked.
    /// </summary>
    public sealed class CapabilityBlockClip : SimulationClip
    {
        /// <summary>
        /// Initializes a new capability-block clip.
        /// </summary>
        /// <param name="clipId">The stable identifier that is unique within one behavior program.</param>
        /// <param name="startTick">The inclusive start tick.</param>
        /// <param name="endTick">The exclusive end tick.</param>
        /// <param name="blockedCapabilities">The non-empty capability mask blocked during the interval.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="blockedCapabilities"/> is empty or contains undefined bits.</exception>
        public CapabilityBlockClip(string clipId, int startTick, int endTick, ActorCapability blockedCapabilities) : base(clipId, startTick, endTick)
        {
            if (blockedCapabilities == ActorCapability.None || (blockedCapabilities & ~ActorCapability.All) != ActorCapability.None)
            {
                throw new ArgumentOutOfRangeException(nameof(blockedCapabilities), blockedCapabilities, "A capability-block clip requires a non-empty mask containing only defined capabilities.");
            }

            BlockedCapabilities = blockedCapabilities;
        }

        /// <inheritdoc/>
        public override SimulationClipKind Kind => SimulationClipKind.CapabilityBlock;

        /// <summary>
        /// Gets the capability mask blocked during the interval.
        /// </summary>
        public ActorCapability BlockedCapabilities { get; }
    }

    /// <summary>
    /// Defines a deterministic gameplay event that is active for exactly one behavior tick.
    /// </summary>
    public sealed class GameplayEventClip : SimulationClip
    {
        /// <summary>
        /// Initializes a new one-tick gameplay-event clip.
        /// </summary>
        /// <param name="clipId">The stable identifier that is unique within one behavior program.</param>
        /// <param name="tick">The inclusive tick at which the event enters and can be sampled.</param>
        /// <param name="eventId">The stable event identifier interpreted by the simulation sink.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="eventId"/> is null, empty, or whitespace.</exception>
        public GameplayEventClip(string clipId, int tick, string eventId) : base(clipId, tick, checked(tick + 1))
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                throw new ArgumentException("A gameplay-event clip requires a non-empty event identifier.", nameof(eventId));
            }

            EventId = eventId;
        }

        /// <inheritdoc/>
        public override SimulationClipKind Kind => SimulationClipKind.GameplayEvent;

        /// <summary>
        /// Gets the stable event identifier interpreted by the simulation sink.
        /// </summary>
        public string EventId { get; }
    }

    /// <summary>
    /// Defines an interval in which a named authored-motion profile is applied.
    /// </summary>
    public sealed class MotionClip : SimulationClip
    {
        /// <summary>
        /// Initializes a new motion clip.
        /// </summary>
        /// <param name="clipId">The stable identifier that is unique within one behavior program.</param>
        /// <param name="startTick">The inclusive start tick.</param>
        /// <param name="endTick">The exclusive end tick.</param>
        /// <param name="motionId">The stable motion profile identifier interpreted by the simulation sink.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="motionId"/> is null, empty, or whitespace.</exception>
        public MotionClip(string clipId, int startTick, int endTick, string motionId) : base(clipId, startTick, endTick)
        {
            if (string.IsNullOrWhiteSpace(motionId))
            {
                throw new ArgumentException("A motion clip requires a non-empty motion identifier.", nameof(motionId));
            }

            MotionId = motionId;
        }

        /// <inheritdoc/>
        public override SimulationClipKind Kind => SimulationClipKind.Motion;

        /// <summary>
        /// Gets the stable motion profile identifier interpreted by the simulation sink.
        /// </summary>
        public string MotionId { get; }
    }
}
