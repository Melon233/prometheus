using System;
using System.Collections.Generic;
using Spine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    /// <summary>定义主动画轨道上的播放所有者，所有者用于让对应 Logic 只停止自己启动的动画。</summary>
    public enum AnimationOwner
    {
        None,
        Idle,
        GroundMove,
        AirMove,
        Landing,
        Dodge,
        PlayerAction,
        EnemyAction,
        HitReaction,
        Death
    }

    /// <summary>定义主动画轨道的抢占优先级；数值越大越能打断当前动画。</summary>
    public enum AnimationPriority
    {
        Idle = 0,
        Landing = 100,
        Locomotion = 200,
        Airborne = 300,
        Attack = 400,
        SpecialAttack = 450,
        Dodge = 500,
        Skill = 600,
        Ultimate = 700,
        HitReaction = 800,
        Death = 1000
    }

    /// <summary>描述一次动画会话的唯一结束原因，Logic 可以据此区分自然完成和被高优先级动画打断。</summary>
    public enum AnimationEndReason
    {
        Completed,
        Interrupted,
        Stopped,
        Disposed
    }

    /// <summary>封装一次主轨动画或动画序列，集中管理 Spine 回调并保证结束通知只发布一次。</summary>
    public sealed class AnimationPlayback
    {
        private readonly SpineComponent ownerComponent;
        private readonly List<TrackEntry> entries = new List<TrackEntry>(2);
        private TrackEntry finalEntry;
        private bool finished;

        /// <summary>创建一个尚未封口的动画会话；只有 SpineComponent 可以构造会话。</summary>
        internal AnimationPlayback(SpineComponent ownerComponent, AnimationSemantic semantic, AnimationOwner owner, AnimationPriority priority, int trackIndex, int version)
        {
            this.ownerComponent = ownerComponent;
            Semantic = semantic;
            Owner = owner;
            Priority = priority;
            TrackIndex = trackIndex;
            Version = version;
        }

        /// <summary>当任意序列片段触发 Spine Event 时发布，并由持有该会话的 Logic 处理玩法行为。</summary>
        public event Action<AnimationPlayback, Spine.Event> EventReceived;

        /// <summary>在自然完成、被抢占、主动停止或组件释放时恰好发布一次。</summary>
        public event Action<AnimationPlayback, AnimationEndReason> Finished;

        /// <summary>获取本次播放最终片段使用的稳定动画语义。</summary>
        public AnimationSemantic Semantic { get; }

        /// <summary>获取本次播放的语义所有者。</summary>
        public AnimationOwner Owner { get; }

        /// <summary>获取本次播放占用主轨道时使用的优先级。</summary>
        public AnimationPriority Priority { get; }

        /// <summary>获取本次播放所在的 Spine Track。</summary>
        public int TrackIndex { get; }

        /// <summary>获取用于排除旧回调的组件内会话版本。</summary>
        internal int Version { get; }

        /// <summary>获取序列最后一个 TrackEntry；单段动画时它就是唯一的 TrackEntry。</summary>
        public TrackEntry FinalEntry => finalEntry;

        /// <summary>获取最终片段的动画时长；配置无效时返回零。</summary>
        public float Duration => finalEntry == null || finalEntry.Animation == null ? 0f : finalEntry.Animation.Duration;

        /// <summary>获取本会话是否仍然拥有对应轨道。</summary>
        public bool IsActive => !finished && ownerComponent != null && ownerComponent.IsPlaybackActive(this);

        /// <summary>将一个 Spine TrackEntry 纳入会话并统一转发事件。</summary>
        internal void AddEntry(TrackEntry entry)
        {
            if (entry == null) return;
            entries.Add(entry);
            entry.Event += OnTrackEvent;
        }

        /// <summary>指定最终片段，并只为非循环序列注册自然完成通知。</summary>
        internal void Seal(TrackEntry entry, bool loop)
        {
            finalEntry = entry;
            if (!loop && finalEntry != null) finalEntry.Complete += OnFinalEntryComplete;
        }

        /// <summary>从所有 Spine TrackEntry 对称移除内部回调，防止旧会话继续访问已释放的 Logic。</summary>
        internal void DetachTrackCallbacks()
        {
            for (int index = 0; index < entries.Count; index++)
            {
                TrackEntry entry = entries[index];
                if (entry != null) entry.Event -= OnTrackEvent;
            }
            if (finalEntry != null) finalEntry.Complete -= OnFinalEntryComplete;
        }

        /// <summary>完成会话并发布一次结束原因；重复调用保持幂等。</summary>
        internal void Finish(AnimationEndReason reason)
        {
            if (finished) return;
            finished = true;
            DetachTrackCallbacks();
            Action<AnimationPlayback, AnimationEndReason> callback = Finished;
            Finished = null;
            EventReceived = null;
            callback?.Invoke(this, reason);
        }

        /// <summary>将 Spine Event 转发给当前会话订阅者，已经结束的会话会忽略迟到事件。</summary>
        private void OnTrackEvent(TrackEntry entry, Spine.Event animationEvent)
        {
            if (finished || animationEvent == null) return;
            EventReceived?.Invoke(this, animationEvent);
        }

        /// <summary>把最终 TrackEntry 的自然完成交给 SpineComponent，由组件原子释放优先级所有权。</summary>
        private void OnFinalEntryComplete(TrackEntry entry)
        {
            if (finished || !ReferenceEquals(entry, finalEntry)) return;
            ownerComponent?.CompletePlayback(this);
        }
    }
}
