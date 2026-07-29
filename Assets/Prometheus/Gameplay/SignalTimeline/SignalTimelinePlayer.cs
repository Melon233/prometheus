using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus
{
    [Obsolete]
    /// <summary>
    /// Plays a SignalTimelineAsset. It has no knowledge of animation, VFX, audio or combat;
    /// all work is delegated through SignalDispatcher.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SignalDispatcher))]
    public sealed class SignalTimelinePlayer : MonoBehaviour
    {
        private struct ScheduledSignal
        {
            public Signal Signal;
            public int OriginalIndex;
        }

        [SerializeField] private SignalDispatcher _dispatcher;
        [SerializeField] private bool _useUnscaledTime;
        [SerializeField, Min(0.01f)] private float _playbackSpeed = 1f;

        private Coroutine _playingCoroutine;
        private int _playVersion;

        public SignalTimelineAsset CurrentTimeline { get; private set; }
        public bool IsPlaying => _playingCoroutine != null;

        public event Action<SignalTimelineAsset> TimelineStarted;
        public event Action<SignalTimelineAsset> TimelineCompleted;
        public event Action<SignalTimelineAsset> TimelineStopped;

        private void Reset()
        {
            _dispatcher = GetComponent<SignalDispatcher>();
        }

        private void Awake()
        {
            if (_dispatcher == null)
                _dispatcher = GetComponent<SignalDispatcher>();
        }

        private void OnDisable()
        {
            Stop();
        }

        public void Play(SignalTimelineAsset timeline)
        {
            Play(timeline, null, null);
        }

        public void Play(SignalTimelineAsset timeline, GameObject target)
        {
            Play(timeline, target, null);
        }

        public void Play(SignalTimelineAsset timeline, GameObject target, object userData)
        {
            PlayWithContext(timeline, new SignalContext(gameObject, target, userData));
        }

        public void PlayWithContext(SignalTimelineAsset timeline, SignalContext context)
        {
            if (timeline == null)
            {
                Debug.LogWarning("Cannot play a null Signal Timeline.", this);
                return;
            }

            if (_dispatcher == null)
                _dispatcher = GetComponent<SignalDispatcher>();

            if (_dispatcher == null)
            {
                Debug.LogError("SignalTimelinePlayer requires a SignalDispatcher.", this);
                return;
            }

            Stop();
            _dispatcher.RebuildHandlers();

            CurrentTimeline = timeline;
            context = context ?? new SignalContext(gameObject);
            context.Timeline = timeline;
            context.TimelineTime = 0f;

            var playVersion = ++_playVersion;
            _playingCoroutine = StartCoroutine(PlayRoutine(timeline, context, playVersion));
            TimelineStarted?.Invoke(timeline);
        }

        public void Stop()
        {
            _playVersion++;

            var stoppedTimeline = CurrentTimeline;
            if (_playingCoroutine != null)
                StopCoroutine(_playingCoroutine);

            _playingCoroutine = null;
            CurrentTimeline = null;

            if (stoppedTimeline != null)
                TimelineStopped?.Invoke(stoppedTimeline);
        }

        private IEnumerator PlayRoutine(SignalTimelineAsset timeline, SignalContext context, int playVersion)
        {
            var scheduledSignals = BuildSchedule(timeline);
            var nextSignalIndex = 0;
            var elapsed = 0f;

            while (nextSignalIndex < scheduledSignals.Count &&
                   scheduledSignals[nextSignalIndex].Signal.Time <= elapsed)
            {
                Dispatch(scheduledSignals[nextSignalIndex].Signal, context);
                nextSignalIndex++;

                if (playVersion != _playVersion)
                    yield break;
            }

            while (elapsed < timeline.Duration)
            {
                yield return null;

                if (playVersion != _playVersion)
                    yield break;

                var deltaTime = _useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                elapsed = Mathf.Min(timeline.Duration, elapsed + deltaTime * Mathf.Max(0.01f, _playbackSpeed));

                while (nextSignalIndex < scheduledSignals.Count &&
                       scheduledSignals[nextSignalIndex].Signal.Time <= elapsed)
                {
                    Dispatch(scheduledSignals[nextSignalIndex].Signal, context);
                    nextSignalIndex++;

                    if (playVersion != _playVersion)
                        yield break;
                }
            }

            if (playVersion != _playVersion)
                yield break;

            _playingCoroutine = null;
            CurrentTimeline = null;
            TimelineCompleted?.Invoke(timeline);
        }

        private void Dispatch(Signal signal, SignalContext context)
        {
            // The declared time is stable even when a frame crosses more than one signal.
            context.TimelineTime = signal.Time;
            _dispatcher.Dispatch(signal, context);
        }

        private static List<ScheduledSignal> BuildSchedule(SignalTimelineAsset timeline)
        {
            var scheduledSignals = new List<ScheduledSignal>();
            var signals = timeline.Signals;

            for (var i = 0; i < signals.Count; i++)
            {
                var signal = signals[i];
                if (signal == null || signal.Time > timeline.Duration)
                    continue;

                scheduledSignals.Add(new ScheduledSignal
                {
                    Signal = signal,
                    OriginalIndex = i
                });
            }

            scheduledSignals.Sort((left, right) =>
            {
                var timeComparison = left.Signal.Time.CompareTo(right.Signal.Time);
                return timeComparison != 0
                    ? timeComparison
                    : left.OriginalIndex.CompareTo(right.OriginalIndex);
            });

            return scheduledSignals;
        }
    }
}