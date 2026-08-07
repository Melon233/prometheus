using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>
    /// Describes one custom Spine event keyed at an animation time in seconds.
    /// </summary>
    [Serializable]
    public sealed class AnimationLineEvent
    {
        [SerializeField, Min(0f)] private float time;
        [SerializeField] private string eventName = "event";
        [SerializeField] private int intValue;
        [SerializeField] private float floatValue;
        [SerializeField] private string stringValue = string.Empty;

        /// <summary>
        /// Gets the event time in seconds.
        /// </summary>
        public float Time => time;

        /// <summary>
        /// Gets the event name delivered through Spine.Event.Data.Name.
        /// </summary>
        public string EventName => eventName;

        /// <summary>
        /// Gets the integer payload delivered through Spine.Event.Int.
        /// </summary>
        public int IntValue => intValue;

        /// <summary>
        /// Gets the floating-point payload delivered through Spine.Event.Float.
        /// </summary>
        public float FloatValue => floatValue;

        /// <summary>
        /// Gets the text payload delivered through Spine.Event.String.
        /// </summary>
        public string StringValue => stringValue;

        /// <summary>
        /// Creates a serializable animation event marker.
        /// </summary>
        public AnimationLineEvent(float time, string eventName, int intValue, float floatValue, string stringValue)
        {
            this.time = Mathf.Max(0f, time);
            this.eventName = eventName ?? string.Empty;
            this.intValue = intValue;
            this.floatValue = floatValue;
            this.stringValue = stringValue ?? string.Empty;
        }

        /// <summary>
        /// Normalizes serialized values after an Inspector edit.
        /// </summary>
        internal void Normalize(float duration)
        {
            time = Mathf.Clamp(time, 0f, Mathf.Max(0f, duration));
            eventName = eventName == null ? string.Empty : eventName.Trim();
            stringValue = stringValue ?? string.Empty;
        }
    }

    /// <summary>
    /// Wraps an AnimationReferenceAsset and merges Unity-authored markers into a cloned Spine EventTimeline without mutating the imported source animation.
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Animation/Animation Line", fileName = "AnimationLine")]
    public sealed class AnimationLine : ScriptableObject
    {
        [SerializeField] private AnimationReferenceAsset animationReferenceAsset;
        [SerializeField] private List<AnimationLineEvent> events = new List<AnimationLineEvent>();

        [NonSerialized] private Spine.Animation runtimeAnimation;

        /// <summary>
        /// Gets the wrapped Spine animation reference.
        /// </summary>
        public AnimationReferenceAsset AnimationReferenceAsset => animationReferenceAsset;

        /// <summary>
        /// Gets the source animation duration in seconds.
        /// </summary>
        public float Duration
        {
            get
            {
                Spine.Animation sourceAnimation = GetSourceAnimation();
                return sourceAnimation == null ? 0f : sourceAnimation.Duration;
            }
        }

        /// <summary>
        /// Gets the serialized custom event markers.
        /// </summary>
        public IReadOnlyList<AnimationLineEvent> Events => events;

        /// <summary>
        /// Returns the source animation with all imported and custom events merged into one Spine EventTimeline.
        /// </summary>
        public Spine.Animation GetRuntimeAnimation()
        {
            if (runtimeAnimation != null) return runtimeAnimation;
            Spine.Animation sourceAnimation = GetSourceAnimation();
            if (sourceAnimation == null) return null;
            List<Spine.Event> mergedEvents = CollectSourceEvents(sourceAnimation);
            AppendCustomEvents(mergedEvents, sourceAnimation.Duration);
            if (mergedEvents.Count == 0)
            {
                runtimeAnimation = sourceAnimation;
                return runtimeAnimation;
            }
            mergedEvents.Sort(CompareEventsByTime);
            ExposedList<Timeline> timelines = CopyNonEventTimelines(sourceAnimation);
            EventTimeline eventTimeline = new EventTimeline(mergedEvents.Count);
            for (int i = 0; i < mergedEvents.Count; i++) eventTimeline.SetFrame(i, mergedEvents[i]);
            timelines.Add(eventTimeline);
            runtimeAnimation = new Spine.Animation(sourceAnimation.Name, timelines, sourceAnimation.Duration);
            return runtimeAnimation;
        }

        /// <summary>
        /// Inserts a new marker and keeps the serialized list ordered by time.
        /// </summary>
        public void InsertEvent(float time, string eventName, int intValue = 0, float floatValue = 0f, string stringValue = "")
        {
            events.Add(new AnimationLineEvent(time, eventName, intValue, floatValue, stringValue));
            NormalizeEvents();
        }

        /// <summary>
        /// Removes the marker at the requested sorted index.
        /// </summary>
        public void RemoveEventAt(int index)
        {
            if (index < 0 || index >= events.Count) return;
            events.RemoveAt(index);
            InvalidateRuntimeAnimation();
        }

        /// <summary>
        /// Revalidates marker bounds and rebuilds the runtime animation on the next request.
        /// </summary>
        public void NormalizeEvents()
        {
            float duration = Duration;
            events.RemoveAll(item => item == null);
            for (int i = 0; i < events.Count; i++) events[i].Normalize(duration);
            events.Sort(CompareMarkersByTime);
            InvalidateRuntimeAnimation();
        }

        /// <summary>
        /// Clears the cached clone after an Inspector or source asset change.
        /// </summary>
        public void InvalidateRuntimeAnimation()
        {
            runtimeAnimation = null;
        }

        /// <summary>
        /// Allows AnimationLine to be passed directly to Spine APIs that accept Spine.Animation.
        /// </summary>
        public static implicit operator Spine.Animation(AnimationLine animationLine)
        {
            return animationLine == null ? null : animationLine.GetRuntimeAnimation();
        }

        /// <summary>
        /// Invalidates the non-serialized runtime clone after a domain reload.
        /// </summary>
        private void OnEnable()
        {
            InvalidateRuntimeAnimation();
        }

        /// <summary>
        /// Keeps authored markers valid whenever Unity deserializes or edits this asset.
        /// </summary>
        private void OnValidate()
        {
            NormalizeEvents();
        }

        /// <summary>
        /// Resolves the imported Spine animation without changing the source asset.
        /// </summary>
        private Spine.Animation GetSourceAnimation()
        {
            return animationReferenceAsset == null ? null : animationReferenceAsset.Animation;
        }

        /// <summary>
        /// Copies source event objects so imported Spine events remain active in the wrapped animation.
        /// </summary>
        private static List<Spine.Event> CollectSourceEvents(Spine.Animation sourceAnimation)
        {
            List<Spine.Event> sourceEvents = new List<Spine.Event>();
            ExposedList<Timeline> sourceTimelines = sourceAnimation.Timelines;
            for (int timelineIndex = 0; timelineIndex < sourceTimelines.Count; timelineIndex++)
            {
                EventTimeline sourceEventTimeline = sourceTimelines.Items[timelineIndex] as EventTimeline;
                if (sourceEventTimeline == null) continue;
                Spine.Event[] timelineEvents = sourceEventTimeline.Events;
                for (int eventIndex = 0; eventIndex < timelineEvents.Length; eventIndex++)
                {
                    if (timelineEvents[eventIndex] != null) sourceEvents.Add(timelineEvents[eventIndex]);
                }
            }
            return sourceEvents;
        }

        /// <summary>
        /// Converts serialized Unity markers into native Spine event objects.
        /// </summary>
        private void AppendCustomEvents(List<Spine.Event> destination, float duration)
        {
            for (int i = 0; i < events.Count; i++)
            {
                AnimationLineEvent marker = events[i];
                if (marker == null || string.IsNullOrWhiteSpace(marker.EventName)) continue;
                float eventTime = Mathf.Clamp(marker.Time, 0f, Mathf.Max(0f, duration));
                EventData eventData = new EventData(marker.EventName) { Int = marker.IntValue, Float = marker.FloatValue, String = marker.StringValue };
                Spine.Event spineEvent = new Spine.Event(eventTime, eventData) { Int = marker.IntValue, Float = marker.FloatValue, String = marker.StringValue };
                destination.Add(spineEvent);
            }
        }

        /// <summary>
        /// Copies every non-event timeline by reference because imported timelines are immutable at runtime.
        /// </summary>
        private static ExposedList<Timeline> CopyNonEventTimelines(Spine.Animation sourceAnimation)
        {
            ExposedList<Timeline> sourceTimelines = sourceAnimation.Timelines;
            ExposedList<Timeline> copiedTimelines = new ExposedList<Timeline>(sourceTimelines.Count + 1);
            for (int i = 0; i < sourceTimelines.Count; i++)
            {
                Timeline timeline = sourceTimelines.Items[i];
                if (!(timeline is EventTimeline)) copiedTimelines.Add(timeline);
            }
            return copiedTimelines;
        }

        /// <summary>
        /// Orders runtime event keys as required by Spine.EventTimeline.
        /// </summary>
        private static int CompareEventsByTime(Spine.Event left, Spine.Event right)
        {
            return left.Time.CompareTo(right.Time);
        }

        /// <summary>
        /// Keeps serialized marker rows ordered by their authored time.
        /// </summary>
        private static int CompareMarkersByTime(AnimationLineEvent left, AnimationLineEvent right)
        {
            return left.Time.CompareTo(right.Time);
        }
    }
}
