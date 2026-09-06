using System;
using System.Collections.Generic;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Animation = Spine.Animation;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>一段挂在剧情 Spine 轨道上的动画片段。</summary>
    [Serializable]
    public sealed class StorySpineClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] [Tooltip("Spine 骨骼数据中的动画名。")] private string animationName;
        [SerializeField] [Tooltip("片段时长超过动画长度时是否循环。")] private bool loop;
        [SerializeField] [Tooltip("采样速度倍率。")] private float speed = 1f;

        /// <summary>获取或设置动画名。</summary>
        public string AnimationName { get => animationName; set => animationName = value; }

        /// <summary>获取或设置是否循环。</summary>
        public bool Loop { get => loop; set => loop = value; }

        /// <summary>获取或设置采样速度倍率。</summary>
        public float Speed { get => speed; set => speed = value; }

        /// <inheritdoc />
        public ClipCaps clipCaps => ClipCaps.Looping | ClipCaps.SpeedMultiplier | ClipCaps.Extrapolation;

        /// <inheritdoc />
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<StorySpineBehaviour> playable = ScriptPlayable<StorySpineBehaviour>.Create(graph);
            StorySpineBehaviour behaviour = playable.GetBehaviour();
            behaviour.AnimationName = animationName;
            behaviour.Loop = loop;
            behaviour.Speed = Mathf.Max(0.01f, speed);
            return playable;
        }
    }

    /// <summary>剧情 Spine 片段的运行时参数载体。</summary>
    public sealed class StorySpineBehaviour : PlayableBehaviour
    {
        /// <summary>获取或设置动画名。</summary>
        public string AnimationName { get; set; }

        /// <summary>获取或设置是否循环。</summary>
        public bool Loop { get; set; }

        /// <summary>获取或设置采样速度倍率。</summary>
        public float Speed { get; set; } = 1f;
    }

    /// <summary>
    /// 剧情 Spine 轨道。
    /// <para>
    /// 轨道按当前时刻直接采样 Spine 动画（<see cref="SpineSampler"/>），不经过 AnimationState，
    /// 因此姿态是时刻的纯函数：可以在编辑器里任意 scrub，也可以在跳过时由 Evaluate 一次落到终态。
    /// 这正是设计规则 R1「Timeline 只承载可插值、可 Evaluate 的连续表现」在角色动画上的落点。
    /// </para>
    /// <para>
    /// 轨道绑定的 <see cref="SkeletonAnimation"/> 组件应当已被舞台接管禁用，否则其自身的
    /// LateUpdate 会与本轨道争抢同一副骨架。
    /// </para>
    /// </summary>
    [TrackColor(0.42f, 0.72f, 0.95f)]
    [TrackClipType(typeof(StorySpineClip))]
    [TrackBindingType(typeof(SkeletonAnimation))]
    public sealed class StorySpineTrack : TrackAsset
    {
        /// <inheritdoc />
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<StorySpineMixer>.Create(graph, inputCount);
        }
    }

    /// <summary>
    /// 剧情 Spine 轨道的混合器。
    /// 同一时刻取权重最高的片段进行采样；Spine 的跨动画混合由片段间的时间安排表达，
    /// 不引入额外的混合状态，以保证采样结果只由时刻决定。
    /// </summary>
    public sealed class StorySpineMixer : PlayableBehaviour
    {
        /// <summary>缓存按名称解析过的动画，避免每帧线性查找骨骼数据。</summary>
        private readonly Dictionary<string, Animation> cache = new Dictionary<string, Animation>(StringComparer.Ordinal);

        /// <summary>保存当前缓存对应的骨骼数据名，骨架切换时整体失效。</summary>
        private string cachedSkeletonName;

        /// <inheritdoc />
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            SkeletonAnimation skeleton = playerData as SkeletonAnimation;
            if (skeleton == null || skeleton.Skeleton == null) return;

            int inputCount = playable.GetInputCount();
            int bestInput = -1;
            float bestWeight = 0f;
            for (int index = 0; index < inputCount; index++)
            {
                float weight = playable.GetInputWeight(index);
                if (weight <= bestWeight) continue;
                bestWeight = weight;
                bestInput = index;
            }
            if (bestInput < 0) return;

            ScriptPlayable<StorySpineBehaviour> input = (ScriptPlayable<StorySpineBehaviour>)playable.GetInput(bestInput);
            StorySpineBehaviour behaviour = input.GetBehaviour();
            if (behaviour == null || string.IsNullOrEmpty(behaviour.AnimationName)) return;

            Animation animation = Resolve(skeleton, behaviour.AnimationName);
            if (animation == null) return;
            SpineSampler.Sample(skeleton, animation, (float)(input.GetTime() * behaviour.Speed), behaviour.Loop);
        }

        /// <summary>按名称解析动画并缓存结果；找不到时只报告一次以免刷屏。</summary>
        private Animation Resolve(SkeletonAnimation skeletonAnimation, string animationName)
        {
            string skeletonName = skeletonAnimation.Skeleton.Data != null ? skeletonAnimation.Skeleton.Data.Name : string.Empty;
            if (!string.Equals(skeletonName, cachedSkeletonName, StringComparison.Ordinal))
            {
                cache.Clear();
                cachedSkeletonName = skeletonName;
            }
            if (cache.TryGetValue(animationName, out Animation cached)) return cached;
            Animation animation = skeletonAnimation.Skeleton.Data != null ? skeletonAnimation.Skeleton.Data.FindAnimation(animationName) : null;
            if (animation == null) Debug.LogWarning($"[Narrative] Spine 骨架 '{skeletonName}' 不包含动画 '{animationName}'。");
            cache[animationName] = animation;
            return animation;
        }
    }
}
