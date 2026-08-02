using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Defines an immutable, reusable behavior program for the single action channel.
    /// </summary>
    public sealed class BehaviorProgram
    {
        private readonly ReadOnlyCollection<SimulationClip> _simulationClips;

        /// <summary>
        /// Initializes a new behavior program and copies the supplied clip sequence in authored callback order.
        /// </summary>
        /// <param name="programId">The stable behavior-program identifier.</param>
        /// <param name="durationTicks">The positive duration measured in whole behavior ticks.</param>
        /// <param name="simulationClips">The simulation clips to execute; their identifiers must be unique within this program.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="programId"/> is null, empty, or whitespace, or when clip identifiers are duplicated.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="simulationClips"/> or one of its elements is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="durationTicks"/> is not positive or a clip extends beyond the program duration.</exception>
        public BehaviorProgram(string programId, int durationTicks, IEnumerable<SimulationClip> simulationClips)
        {
            if (string.IsNullOrWhiteSpace(programId))
            {
                throw new ArgumentException("A behavior program requires a non-empty stable identifier.", nameof(programId));
            }

            if (durationTicks <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(durationTicks), durationTicks, "A behavior program duration must be positive.");
            }

            if (simulationClips == null)
            {
                throw new ArgumentNullException(nameof(simulationClips));
            }

            var clips = new List<SimulationClip>();
            var clipIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (SimulationClip clip in simulationClips)
            {
                if (clip == null)
                {
                    throw new ArgumentNullException(nameof(simulationClips), "A behavior program cannot contain a null simulation clip.");
                }

                if (clip.EndTick > durationTicks)
                {
                    throw new ArgumentOutOfRangeException(nameof(simulationClips), clip.EndTick, $"Clip '{clip.ClipId}' ends after behavior program '{programId}'.");
                }

                if (!clipIds.Add(clip.ClipId))
                {
                    throw new ArgumentException($"Behavior program '{programId}' contains duplicate clip identifier '{clip.ClipId}'.", nameof(simulationClips));
                }

                clips.Add(clip);
            }

            ProgramId = programId;
            DurationTicks = durationTicks;
            _simulationClips = new ReadOnlyCollection<SimulationClip>(clips);
        }

        /// <summary>
        /// Gets the stable behavior-program identifier.
        /// </summary>
        public string ProgramId { get; }

        /// <summary>
        /// Gets the positive duration measured in whole behavior ticks.
        /// </summary>
        public int DurationTicks { get; }

        /// <summary>
        /// Gets the single channel occupied by this behavior program.
        /// </summary>
        public BehaviorChannel Channel => BehaviorChannel.Action;

        /// <summary>
        /// Gets the immutable simulation clips in authored callback order.
        /// </summary>
        public IReadOnlyList<SimulationClip> SimulationClips => _simulationClips;
    }
}
