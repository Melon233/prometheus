using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus
{
    /// <summary>定义由单个 AnimationLine 在指定时间直接发布的强类型玩法命令，避免角色动画库保存共享事件名。</summary>
    public enum AnimationLineEventCommand
    {
        /// <summary>表示普通自定义事件，不产生强类型玩法命令。</summary>
        None = 0,
        /// <summary>开启当前动作上下文已经选择的碰撞盒。</summary>
        EnableHitbox = 1,
        /// <summary>关闭当前动作 Logic 绑定的全部碰撞盒。</summary>
        DisableHitbox = 2
    }

    /// <summary>
    /// Describes one custom Spine event or typed gameplay command keyed at an animation time in seconds.
    /// </summary>
    [Serializable]
    public sealed class AnimationLineEvent
    {
        [SerializeField, Min(0f)] private float time;
        [SerializeField] private string eventName = "event";
        [SerializeField] private int intValue;
        [SerializeField] private float floatValue;
        [SerializeField] private string stringValue = string.Empty;
        [SerializeField] private AnimationLineEventCommand command;

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

        /// <summary>获取该标记配置的强类型玩法命令；None 表示继续使用普通事件名和载荷。</summary>
        public AnimationLineEventCommand Command => command;

        /// <summary>获取 Inspector 和时间轴使用的稳定显示名，玩法命令不依赖普通事件名。</summary>
        public string DisplayName => command == AnimationLineEventCommand.None ? eventName : command.ToString();

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
            command = AnimationLineEventCommand.None;
        }

        /// <summary>创建一个不依赖字符串名称的强类型 AnimationLine 玩法命令。</summary>
        public AnimationLineEvent(float time, AnimationLineEventCommand eventCommand)
        {
            this.time = Mathf.Max(0f, time);
            eventName = string.Empty;
            intValue = 0;
            floatValue = 0f;
            stringValue = string.Empty;
            command = eventCommand;
        }

        /// <summary>
        /// Normalizes serialized values after an Inspector edit.
        /// </summary>
        internal void Normalize(float duration)
        {
            time = Mathf.Clamp(time, 0f, Mathf.Max(0f, duration));
            eventName = eventName == null ? string.Empty : eventName.Trim();
            stringValue = stringValue ?? string.Empty;
            if (!Enum.IsDefined(typeof(AnimationLineEventCommand), command)) command = AnimationLineEventCommand.None;
        }
    }

    /// <summary>描述绑定到既有动画事件时间点的一个 FMOD 音频事件。</summary>
    [Serializable]
    public sealed class AnimationLineAudioBinding
    {
        [SerializeField, Min(0f)] private float time;
        [SerializeField] private FmodAudioEvent audioEvent;

        /// <summary>创建一个使用指定轨道时间和 FMOD 音频事件的绑定。</summary>
        public AnimationLineAudioBinding(float time, FmodAudioEvent audioEvent)
        {
            this.time = Mathf.Max(0f, time);
            this.audioEvent = audioEvent;
        }

        /// <summary>获取音频事件在动画中的触发时间。</summary>
        public float Time => time;

        /// <summary>获取需要触发的生成式 FMOD 音频事件枚举。</summary>
        public FmodAudioEvent AudioEvent => audioEvent;

        /// <summary>把序列化时间约束到当前动画范围内。</summary>
        internal void Normalize(float duration)
        {
            time = Mathf.Clamp(time, 0f, Mathf.Max(0f, duration));
        }
    }

    /// <summary>
    /// Wraps an AnimationReferenceAsset and merges Unity-authored markers into a cloned Spine EventTimeline without mutating the imported source animation.
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Animation/Animation Line", fileName = "AnimationLine")]
    public sealed class AnimationLine : ScriptableObject
    {
        /// <summary>定义注入 Spine EventTimeline 的内部命令标记名，业务代码不得直接比较该传输名称。</summary>
        internal const string CommandMarkerEventName = "__prometheus_animation_command";

        [SerializeField] private AnimationSemantic semantic;
        [SerializeField] private AnimationReferenceAsset animationReferenceAsset;
        [SerializeField] private List<AnimationLineEvent> events = new List<AnimationLineEvent>();
        [SerializeField] private List<AnimationLineAudioBinding> audioBindings = new List<AnimationLineAudioBinding>();

        [NonSerialized] private Spine.Animation runtimeAnimation;

        /// <summary>获取该 AnimationLine 对外暴露的稳定动画语义。</summary>
        public AnimationSemantic Semantic => semantic;

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

        /// <summary>获取绑定到既有轨道时间的 FMOD 音频事件列表。</summary>
        public IReadOnlyList<AnimationLineAudioBinding> AudioBindings => audioBindings;

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
            AppendAudioBindings(mergedEvents, sourceAnimation.Duration);
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

        /// <summary>在指定时间插入一个强类型玩法命令，并保持序列化列表按时间排序。</summary>
        public void InsertCommand(float time, AnimationLineEventCommand command)
        {
            if (command == AnimationLineEventCommand.None) return;
            events.Add(new AnimationLineEvent(time, command));
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

        /// <summary>在指定既有轨道时间插入一个 FMOD 音频事件绑定，并保持序列化列表按时间排序。</summary>
        public void InsertAudioBinding(float time, FmodAudioEvent audioEvent)
        {
            if (audioEvent == FmodAudioEvent.None) return;
            if (audioBindings == null) audioBindings = new List<AnimationLineAudioBinding>();
            audioBindings.Add(new AnimationLineAudioBinding(time, audioEvent));
            NormalizeEvents();
        }

        /// <summary>删除排序后指定位置的 FMOD 音频事件绑定。</summary>
        public void RemoveAudioBindingAt(int index)
        {
            if (audioBindings == null || index < 0 || index >= audioBindings.Count) return;
            audioBindings.RemoveAt(index);
            InvalidateRuntimeAnimation();
        }

        /// <summary>
        /// Revalidates marker bounds and rebuilds the runtime animation on the next request.
        /// </summary>
        public void NormalizeEvents()
        {
            float duration = Duration;
            if (events == null) events = new List<AnimationLineEvent>();
            events.RemoveAll(item => item == null);
            for (int i = 0; i < events.Count; i++) events[i].Normalize(duration);
            events.Sort(CompareMarkersByTime);
            if (audioBindings == null) audioBindings = new List<AnimationLineAudioBinding>();
            audioBindings.RemoveAll(item => item == null);
            for (int i = 0; i < audioBindings.Count; i++) audioBindings[i].Normalize(duration);
            audioBindings.Sort(CompareAudioBindingsByTime);
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
        /// Assigns the imported Spine animation wrapped by this asset; editor migration tools use this entry instead of writing private serialized state through reflection.
        /// </summary>
        public void SetAnimationReference(AnimationReferenceAsset sourceAnimation)
        {
            animationReferenceAsset = sourceAnimation;
            NormalizeEvents();
        }

        /// <summary>设置稳定动画语义；编辑器迁移工具使用该入口，运行时只读取结果。</summary>
        public void SetSemantic(AnimationSemantic value)
        {
            semantic = value;
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
        /// Converts serialized Unity markers and typed commands into native Spine event objects.
        /// </summary>
        private void AppendCustomEvents(List<Spine.Event> destination, float duration)
        {
            for (int i = 0; i < events.Count; i++)
            {
                AnimationLineEvent marker = events[i];
                if (marker == null || marker.Command == AnimationLineEventCommand.None && string.IsNullOrWhiteSpace(marker.EventName)) continue;
                float eventTime = Mathf.Clamp(marker.Time, 0f, Mathf.Max(0f, duration));
                string runtimeEventName = marker.Command == AnimationLineEventCommand.None ? marker.EventName : CommandMarkerEventName;
                int runtimeIntValue = marker.Command == AnimationLineEventCommand.None ? marker.IntValue : (int)marker.Command;
                EventData eventData = new EventData(runtimeEventName) { Int = runtimeIntValue, Float = marker.FloatValue, String = marker.StringValue };
                Spine.Event spineEvent = new Spine.Event(eventTime, eventData) { Int = runtimeIntValue, Float = marker.FloatValue, String = marker.StringValue };
                destination.Add(spineEvent);
            }
        }

        /// <summary>识别内部时间轴命令标记并解析合法枚举；普通 Spine 事件和未知载荷保持未消费状态。</summary>
        internal static bool TryResolveCommand(Spine.Event animationEvent, out AnimationLineEventCommand command)
        {
            command = AnimationLineEventCommand.None;
            if (animationEvent == null || animationEvent.Data == null || !string.Equals(animationEvent.Data.Name, CommandMarkerEventName, StringComparison.Ordinal)) return false;
            AnimationLineEventCommand resolvedCommand = (AnimationLineEventCommand)animationEvent.Int;
            if (resolvedCommand == AnimationLineEventCommand.None || !Enum.IsDefined(typeof(AnimationLineEventCommand), resolvedCommand)) return false;
            command = resolvedCommand;
            return true;
        }

        /// <summary>把 FMOD 音频绑定转换为保留名称的 Spine 事件，使循环、序列和混合播放共享原生时间调度。</summary>
        private void AppendAudioBindings(List<Spine.Event> destination, float duration)
        {
            if (audioBindings == null) return;
            for (int i = 0; i < audioBindings.Count; i++)
            {
                AnimationLineAudioBinding binding = audioBindings[i];
                if (binding == null || binding.AudioEvent == FmodAudioEvent.None) continue;
                float eventTime = Mathf.Clamp(binding.Time, 0f, Mathf.Max(0f, duration));
                int audioEventValue = (int)binding.AudioEvent;
                EventData eventData = new EventData(FmodAudioRuntime.AnimationMarkerEventName) { Int = audioEventValue };
                Spine.Event spineEvent = new Spine.Event(eventTime, eventData) { Int = audioEventValue };
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

        /// <summary>保持序列化音频绑定按触发时间稳定排序。</summary>
        private static int CompareAudioBindingsByTime(AnimationLineAudioBinding left, AnimationLineAudioBinding right)
        {
            return left.Time.CompareTo(right.Time);
        }
    }
}
