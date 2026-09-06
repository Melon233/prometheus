using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Spine;
using Spine.Unity;
using UnityEngine;
using Animation = Spine.Animation;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// Spine 骨骼的按时间采样工具。
    /// <para>
    /// 剧情动画不经过 <c>AnimationState</c>，而是直接调用 <see cref="Animation.Apply"/> 按绝对时刻求出骨架姿态。
    /// 这样采样结果是时间的纯函数：可以任意 scrub、可以在 <c>Settle</c> 中一次性落到终态，
    /// 也不会与战斗动画模块的优先级仲裁产生任何耦合。
    /// </para>
    /// </summary>
    public static class SpineSampler
    {
        /// <summary>按名称查找一段 Spine 动画；找不到时给出可诊断的错误。</summary>
        public static Animation RequireAnimation(SkeletonAnimation skeletonAnimation, string animationName)
        {
            if (skeletonAnimation == null) throw new ArgumentNullException(nameof(skeletonAnimation));
            if (string.IsNullOrWhiteSpace(animationName)) throw new ArgumentException("Spine animation name cannot be empty.", nameof(animationName));
            SkeletonData data = skeletonAnimation.Skeleton != null ? skeletonAnimation.Skeleton.Data : null;
            if (data == null) throw new InvalidOperationException($"Spine skeleton on '{skeletonAnimation.name}' is not initialized.");
            Animation animation = data.FindAnimation(animationName);
            if (animation == null) throw new InvalidOperationException($"Spine skeleton '{data.Name}' does not contain animation '{animationName}'.");
            return animation;
        }

        /// <summary>把动画在指定时刻的姿态写入骨架并重建网格。</summary>
        /// <param name="skeletonAnimation">目标骨骼组件；其自身的 AnimationState 应当已被舞台接管禁用。</param>
        /// <param name="animation">要采样的动画。</param>
        /// <param name="time">采样时刻，单位秒。</param>
        /// <param name="loop">超出动画长度后是否循环。</param>
        public static void Sample(SkeletonAnimation skeletonAnimation, Animation animation, float time, bool loop)
        {
            if (skeletonAnimation == null || animation == null) return;
            Skeleton skeleton = skeletonAnimation.Skeleton;
            if (skeleton == null) return;
            float sampleTime = Normalize(time, animation.Duration, loop);
            // MixBlend.Setup 让每次采样都从 Setup Pose 出发，使结果只由时刻决定，与采样历史无关。
            skeleton.SetToSetupPose();
            animation.Apply(skeleton, sampleTime, sampleTime, loop, null, 1f, MixBlend.Setup, MixDirection.In);
            skeleton.UpdateWorldTransform();
            // 组件被舞台禁用后不会自行重建网格，这里显式驱动一次。
            skeletonAnimation.LateUpdate();
        }

        /// <summary>把采样时刻规范到动画时长范围内。</summary>
        public static float Normalize(float time, float duration, bool loop)
        {
            if (duration <= 0f) return 0f;
            if (time <= 0f) return 0f;
            if (!loop) return Mathf.Min(time, duration);
            return Mathf.Repeat(time, duration);
        }
    }

    /// <summary>
    /// 角色 Spine 动画播放动作。
    /// 循环动画的终态定义为动画的第一帧姿态，非循环动画的终态是最后一帧，两者都是时刻的纯函数。
    /// </summary>
    public sealed class SpineAnimAction : StoryAction
    {
        private readonly ActorRef actor;
        private readonly string animationName;
        private readonly bool loop;
        private readonly float speed;

        /// <summary>创建一个 Spine 动画播放动作。</summary>
        /// <param name="actor">目标角色。</param>
        /// <param name="animationName">Spine 动画名。</param>
        /// <param name="loop">是否循环；循环动画不会阻塞节拍推进。</param>
        /// <param name="speed">播放速度倍率。</param>
        public SpineAnimAction(ActorRef actor, string animationName, bool loop = false, float speed = 1f)
        {
            if (string.IsNullOrWhiteSpace(animationName)) throw new ArgumentException("Spine animation name cannot be empty.", nameof(animationName));
            this.actor = actor;
            this.animationName = animationName;
            this.loop = loop;
            this.speed = Mathf.Max(0.01f, speed);
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            ActorHandle handle = stage.RequireActor(actor);
            SkeletonAnimation skeleton = handle.Skeleton;
            if (skeleton == null) throw new InvalidOperationException($"Narrative actor '{actor}' has no SkeletonAnimation; SpineAnimAction cannot drive it.");
            Animation animation = SpineSampler.RequireAnimation(skeleton, animationName);
            // 同一角色只保留一条动画通道：开启新动画会自动中止该角色上一段仍在播放的动画。
            CancellationToken channel = stage.BeginActorAnimation(actor, cancellationToken);
            float elapsed = 0f;
            try
            {
                while (loop || elapsed < animation.Duration)
                {
                    channel.ThrowIfCancellationRequested();
                    SpineSampler.Sample(skeleton, animation, elapsed, loop);
                    await UniTask.Yield(PlayerLoopTiming.Update, channel);
                    if (!handle.IsAlive) return;
                    elapsed += Time.deltaTime * speed;
                }
            }
            catch (OperationCanceledException)
            {
                // 被同一角色的下一段动画顶掉属于正常替换，不向上传播；只有外层取消才继续抛出。
                if (cancellationToken.IsCancellationRequested) throw;
                return;
            }
            SpineSampler.Sample(skeleton, animation, animation.Duration, false);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            StageScope stage = context.RequireStage();
            if (!stage.TryGetActor(actor, out ActorHandle handle) || handle.Skeleton == null) return;
            Animation animation = SpineSampler.RequireAnimation(handle.Skeleton, animationName);
            SpineSampler.Sample(handle.Skeleton, animation, loop ? 0f : animation.Duration, loop);
        }
    }

    /// <summary>
    /// 场景物体的原生动画片段播放动作。
    /// 使用 <see cref="AnimationClip.SampleAnimation"/> 按时刻采样，同样是时间的纯函数，不需要 Animator Controller。
    /// 片段必须在 StageSpec.Preload 中声明，否则同步的落终态无法取得资源。
    /// </summary>
    public sealed class PropAnimAction : StoryAction
    {
        private readonly ActorRef prop;
        private readonly string clipLocation;
        private readonly float speed;

        /// <summary>创建一个物体动画播放动作。</summary>
        /// <param name="prop">目标场景物体。</param>
        /// <param name="clipLocation">AnimationClip 的资源地址；必须已在舞台预加载列表中。</param>
        /// <param name="speed">播放速度倍率。</param>
        public PropAnimAction(ActorRef prop, string clipLocation, float speed = 1f)
        {
            if (string.IsNullOrWhiteSpace(clipLocation)) throw new ArgumentException("Animation clip location cannot be empty.", nameof(clipLocation));
            this.prop = prop;
            this.clipLocation = clipLocation;
            this.speed = Mathf.Max(0.01f, speed);
        }

        /// <inheritdoc />
        public override async UniTask PlayAsync(StoryContext context, CancellationToken cancellationToken)
        {
            StageScope stage = context.RequireStage();
            ActorHandle handle = stage.RequireActor(prop);
            AnimationClip clip = await stage.Services.RequireAssets().LoadAsync<AnimationClip>(clipLocation, cancellationToken);
            if (clip == null) throw new InvalidOperationException($"Animation clip '{clipLocation}' could not be loaded.");
            float elapsed = 0f;
            while (elapsed < clip.length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                clip.SampleAnimation(handle.GameObject, elapsed);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                if (!handle.IsAlive) return;
                elapsed += Time.deltaTime * speed;
            }
            clip.SampleAnimation(handle.GameObject, clip.length);
        }

        /// <inheritdoc />
        public override void Settle(StoryContext context)
        {
            StageScope stage = context.RequireStage();
            if (!stage.TryGetActor(prop, out ActorHandle handle)) return;
            if (!stage.Services.RequireAssets().TryGet(clipLocation, out AnimationClip clip))
            {
                throw new InvalidOperationException($"Animation clip '{clipLocation}' must be declared in StageSpec.Preload so that skipping can apply its final pose synchronously.");
            }
            clip.SampleAnimation(handle.GameObject, clip.length);
        }
    }
}
