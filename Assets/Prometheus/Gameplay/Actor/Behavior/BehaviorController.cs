using System;
using System.Collections.Generic;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// Executes one mutually exclusive action-channel behavior and emits pure simulation callbacks through an injected sink.
    /// </summary>
    public sealed class BehaviorController : IDisposable
    {
        private readonly IBehaviorSimulationSink _sink;
        private readonly List<int> _activationOrder = new List<int>();
        private BehaviorProgram _activeProgram;
        private BehaviorHandle _activeHandle;
        private bool[] _activeClips = Array.Empty<bool>();
        private long _phaseRaw;
        private int _rateRaw;
        private long _nextInstanceId;
        private bool _isDisposed;
        private bool _isEmitting;

        /// <summary>
        /// Initializes a new action-channel behavior controller.
        /// </summary>
        /// <param name="sink">The non-null sink that consumes all behavior and clip callbacks.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sink"/> is null.</exception>
        public BehaviorController(IBehaviorSimulationSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>
        /// Gets the single channel owned by this controller.
        /// </summary>
        public BehaviorChannel Channel => BehaviorChannel.Action;

        /// <summary>
        /// Gets a value indicating whether an action behavior is currently active.
        /// </summary>
        public bool IsActive => _activeProgram != null;

        /// <summary>
        /// Gets the active behavior handle, or an invalid default handle when the controller is idle.
        /// </summary>
        public BehaviorHandle ActiveHandle => _activeHandle;

        /// <summary>
        /// Gets the active immutable program, or <see langword="null"/> when the controller is idle.
        /// </summary>
        public BehaviorProgram ActiveProgram => _activeProgram;

        /// <summary>
        /// Attempts to start a behavior program at a positive deterministic Q16 playback rate.
        /// </summary>
        /// <param name="program">The immutable behavior program to start.</param>
        /// <param name="rateRaw">The positive playback rate in raw Q16 behavior ticks per simulation step.</param>
        /// <param name="handle">Receives the new instance handle on success or an invalid default handle when the channel is occupied.</param>
        /// <returns><see langword="true"/> when the program starts; <see langword="false"/> when another action behavior already occupies the channel.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="program"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="rateRaw"/> is not positive.</exception>
        /// <exception cref="InvalidOperationException">Thrown when called synchronously from a sink callback or when the instance-identifier range is exhausted.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this controller has been disposed.</exception>
        public bool TryStart(BehaviorProgram program, int rateRaw, out BehaviorHandle handle)
        {
            ThrowIfDisposed();
            ThrowIfEmitting();
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            if (rateRaw <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rateRaw), rateRaw, "A behavior playback rate must be positive.");
            }

            if (_activeProgram != null)
            {
                handle = default;
                return false;
            }

            if (_nextInstanceId == long.MaxValue)
            {
                throw new InvalidOperationException("The behavior-controller instance-identifier range is exhausted.");
            }

            _nextInstanceId++;
            _activeProgram = program;
            _activeHandle = new BehaviorHandle(_nextInstanceId, BehaviorChannel.Action);
            _activeClips = new bool[program.SimulationClips.Count];
            _activationOrder.Clear();
            _phaseRaw = 0;
            _rateRaw = rateRaw;
            handle = _activeHandle;
            EmitBehaviorStarted(program, CurrentPhase);
            EnterClipsAtTick(0);
            return true;
        }

        /// <summary>
        /// Advances the active behavior by one simulation step at its configured Q16 rate.
        /// Every whole behavior tick crossed in a fast step is sampled in chronological order, while a sub-tick rate samples the current interval once per simulation step.
        /// </summary>
        /// <returns><see langword="true"/> when a behavior remains active after the step; otherwise, <see langword="false"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown when called synchronously from a sink callback.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this controller has been disposed.</exception>
        public bool Step()
        {
            ThrowIfDisposed();
            ThrowIfEmitting();
            if (_activeProgram == null)
            {
                return false;
            }

            long durationRaw = (long)_activeProgram.DurationTicks << BehaviorPhase.FractionBits;
            long targetRaw = _phaseRaw + _rateRaw;
            if (targetRaw > durationRaw)
            {
                targetRaw = durationRaw;
            }

            int currentTick = checked((int)(_phaseRaw >> BehaviorPhase.FractionBits));
            int targetTick = checked((int)(targetRaw >> BehaviorPhase.FractionBits));
            if (currentTick == targetTick)
            {
                _phaseRaw = targetRaw;
                SampleActiveClips();
                return true;
            }

            while (_activeProgram != null && currentTick < targetTick)
            {
                SampleActiveClips();
                currentTick++;
                _phaseRaw = (long)currentTick << BehaviorPhase.FractionBits;
                ExitClipsAtTick(currentTick, BehaviorEndReason.Completed);
                if (currentTick >= _activeProgram.DurationTicks)
                {
                    EndActiveBehavior(BehaviorEndReason.Completed);
                    break;
                }

                EnterClipsAtTick(currentTick);
            }

            if (_activeProgram != null)
            {
                _phaseRaw = targetRaw;
            }

            return _activeProgram != null;
        }

        /// <summary>
        /// Cancels the active behavior only when the supplied handle still identifies that exact instance.
        /// </summary>
        /// <param name="handle">The handle returned by the successful start operation.</param>
        /// <returns><see langword="true"/> when the matching active behavior is cancelled; otherwise, <see langword="false"/> for an invalid, stale, or inactive handle.</returns>
        /// <exception cref="InvalidOperationException">Thrown when called synchronously from a sink callback.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when this controller has been disposed.</exception>
        public bool Cancel(BehaviorHandle handle)
        {
            ThrowIfDisposed();
            ThrowIfEmitting();
            if (_activeProgram == null || !handle.IsValid || handle != _activeHandle)
            {
                return false;
            }

            EndActiveBehavior(BehaviorEndReason.Cancelled);
            return true;
        }

        /// <summary>
        /// Attempts to retrieve the current phase for the exact active behavior instance represented by a handle.
        /// </summary>
        /// <param name="handle">The behavior handle whose phase is requested.</param>
        /// <param name="phase">Receives the current phase when the handle is active or the default phase otherwise.</param>
        /// <returns><see langword="true"/> when the handle identifies the active behavior; otherwise, <see langword="false"/>.</returns>
        public bool TryGetPhase(BehaviorHandle handle, out BehaviorPhase phase)
        {
            if (_activeProgram != null && handle.IsValid && handle == _activeHandle)
            {
                phase = CurrentPhase;
                return true;
            }

            phase = default;
            return false;
        }

        /// <summary>
        /// Disposes the controller, forcefully exits every active clip exactly once, and ends any active behavior with <see cref="BehaviorEndReason.Disposed"/>.
        /// Repeated calls are safe and produce no additional callbacks.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when called synchronously from a sink callback.</exception>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            ThrowIfEmitting();
            try
            {
                if (_activeProgram != null)
                {
                    EndActiveBehavior(BehaviorEndReason.Disposed);
                }
            }
            finally
            {
                ClearActiveState();
                _isDisposed = true;
            }
        }

        private BehaviorPhase CurrentPhase => new BehaviorPhase(_phaseRaw, _rateRaw);

        private void EnterClipsAtTick(int tick)
        {
            IReadOnlyList<SimulationClip> clips = _activeProgram.SimulationClips;
            for (int index = 0; index < clips.Count; index++)
            {
                if (clips[index].StartTick == tick)
                {
                    EnterClip(index);
                }
            }
        }

        private void EnterClip(int index)
        {
            if (_activeClips[index])
            {
                throw new InvalidOperationException($"Simulation clip '{_activeProgram.SimulationClips[index].ClipId}' attempted to enter more than once.");
            }

            _activeClips[index] = true;
            _activationOrder.Add(index);
            EmitClipEntered(_activeProgram.SimulationClips[index], CurrentPhase);
        }

        private void SampleActiveClips()
        {
            BehaviorPhase phase = CurrentPhase;
            IReadOnlyList<SimulationClip> clips = _activeProgram.SimulationClips;
            for (int index = 0; index < clips.Count; index++)
            {
                if (_activeClips[index])
                {
                    EmitClipSampled(clips[index], phase);
                }
            }
        }

        private void ExitClipsAtTick(int tick, BehaviorEndReason reason)
        {
            IReadOnlyList<SimulationClip> clips = _activeProgram.SimulationClips;
            for (int index = 0; index < clips.Count; index++)
            {
                if (_activeClips[index] && clips[index].EndTick == tick)
                {
                    ExitClip(index, reason);
                }
            }
        }

        private void ExitClip(int index, BehaviorEndReason reason)
        {
            if (!_activeClips[index])
            {
                return;
            }

            SimulationClip clip = _activeProgram.SimulationClips[index];
            BehaviorPhase phase = CurrentPhase;
            _activeClips[index] = false;
            EmitClipExited(clip, phase, reason);
        }

        private void EndActiveBehavior(BehaviorEndReason reason)
        {
            BehaviorProgram program = _activeProgram;
            BehaviorHandle handle = _activeHandle;
            BehaviorPhase phase = CurrentPhase;
            for (int activationIndex = _activationOrder.Count - 1; activationIndex >= 0; activationIndex--)
            {
                int clipIndex = _activationOrder[activationIndex];
                if (_activeClips[clipIndex])
                {
                    ExitClip(clipIndex, reason);
                }
            }

            ClearActiveState();
            EmitBehaviorEnded(handle, program, phase, reason);
        }

        private void ClearActiveState()
        {
            _activeProgram = null;
            _activeHandle = default;
            _activeClips = Array.Empty<bool>();
            _activationOrder.Clear();
            _phaseRaw = 0;
            _rateRaw = 0;
        }

        private void EmitBehaviorStarted(BehaviorProgram program, BehaviorPhase phase)
        {
            _isEmitting = true;
            try
            {
                _sink.OnBehaviorStarted(_activeHandle, program, phase);
            }
            finally
            {
                _isEmitting = false;
            }
        }

        private void EmitClipEntered(SimulationClip clip, BehaviorPhase phase)
        {
            _isEmitting = true;
            try
            {
                _sink.OnClipEntered(_activeHandle, clip, phase);
            }
            finally
            {
                _isEmitting = false;
            }
        }

        private void EmitClipSampled(SimulationClip clip, BehaviorPhase phase)
        {
            _isEmitting = true;
            try
            {
                _sink.OnClipSampled(_activeHandle, clip, phase);
            }
            finally
            {
                _isEmitting = false;
            }
        }

        private void EmitClipExited(SimulationClip clip, BehaviorPhase phase, BehaviorEndReason reason)
        {
            _isEmitting = true;
            try
            {
                _sink.OnClipExited(_activeHandle, clip, phase, reason);
            }
            finally
            {
                _isEmitting = false;
            }
        }

        private void EmitBehaviorEnded(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase, BehaviorEndReason reason)
        {
            _isEmitting = true;
            try
            {
                _sink.OnBehaviorEnded(handle, program, phase, reason);
            }
            finally
            {
                _isEmitting = false;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(BehaviorController));
            }
        }

        private void ThrowIfEmitting()
        {
            if (_isEmitting)
            {
                throw new InvalidOperationException("A behavior controller cannot be mutated synchronously from one of its sink callbacks.");
            }
        }
    }
}
