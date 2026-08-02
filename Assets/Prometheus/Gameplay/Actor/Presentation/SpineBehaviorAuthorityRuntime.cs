using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>保存与 Spine 官方 SkeletonRootMotionBase 完全同序的根运动轴配置，使离线烘焙位移与旧资产调节参数保持一致。</summary>
    public readonly struct SpineRootMotionAxisSettings
    {
        /// <summary>创建根运动轴配置；换轴项使用尚未乘 RootMotionScale 的 Skeleton 空间位移，与 Spine 官方实现一致。</summary>
        public SpineRootMotionAxisSettings(bool transformPositionX, bool transformPositionY, float rootMotionScaleX, float rootMotionScaleY, float rootMotionTranslateXPerY, float rootMotionTranslateYPerX)
        {
            if (!IsFinite(rootMotionScaleX) || !IsFinite(rootMotionScaleY) || !IsFinite(rootMotionTranslateXPerY) || !IsFinite(rootMotionTranslateYPerX)) throw new ArgumentOutOfRangeException(nameof(rootMotionScaleX), "Spine root-motion axis settings must contain only finite values.");
            TransformPositionX = transformPositionX;
            TransformPositionY = transformPositionY;
            RootMotionScaleX = rootMotionScaleX;
            RootMotionScaleY = rootMotionScaleY;
            RootMotionTranslateXPerY = rootMotionTranslateXPerY;
            RootMotionTranslateYPerX = rootMotionTranslateYPerX;
        }

        /// <summary>获取是否把 Spine X 位移交给对象运动权威。</summary>
        public bool TransformPositionX { get; }

        /// <summary>获取是否把 Spine Y 位移交给对象运动权威。</summary>
        public bool TransformPositionY { get; }

        /// <summary>获取 Skeleton 空间 X 根运动缩放。</summary>
        public float RootMotionScaleX { get; }

        /// <summary>获取 Skeleton 空间 Y 根运动缩放。</summary>
        public float RootMotionScaleY { get; }

        /// <summary>获取每单位 Y 位移附加到 X 的换轴比例。</summary>
        public float RootMotionTranslateXPerY { get; }

        /// <summary>获取每单位 X 位移附加到 Y 的换轴比例。</summary>
        public float RootMotionTranslateYPerX { get; }

        /// <summary>创建不改变烘焙位移的默认配置。</summary>
        public static SpineRootMotionAxisSettings Default => new SpineRootMotionAxisSettings(true, true, 1f, 1f, 0f, 0f);

        /// <summary>从现有 Spine 根运动组件读取公开配置；组件可以保持禁用，行为模拟仍然复用其资产参数。</summary>
        public static SpineRootMotionAxisSettings From(SkeletonRootMotion rootMotion)
        {
            return rootMotion == null ? Default : new SpineRootMotionAxisSettings(rootMotion.transformPositionX, rootMotion.transformPositionY, rootMotion.rootMotionScaleX, rootMotion.rootMotionScaleY, rootMotion.rootMotionTranslateXPerY, rootMotion.rootMotionTranslateYPerX);
        }

        /// <summary>判断浮点数是否可以安全进入运动换算。</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>提供不依赖场景对象的 Spine 行为相位采样和根运动空间换算，便于客户端与离线工具共享同一数学语义。</summary>
    public static class SpineBehaviorAuthorityMath
    {
        /// <summary>把权威 BehaviorPhase 与渲染插值映射为一个 Spine TrackEntry 的绝对 TrackTime；结果完全不依赖渲染帧增量时间。</summary>
        public static float ResolveTrackTime(BehaviorPhase phase, float interpolationAlpha, int cueStartTick, int cueEndTick, int behaviorDurationTicks, float animationDurationSeconds, bool loop, int tickRate)
        {
            if (phase.RawValue < 0L || phase.RateRaw <= 0) throw new ArgumentOutOfRangeException(nameof(phase), phase, "Behavior phase must contain a non-negative value and a positive rate.");
            if (!IsFinite(interpolationAlpha) || interpolationAlpha < 0f || interpolationAlpha > 1f) throw new ArgumentOutOfRangeException(nameof(interpolationAlpha), interpolationAlpha, "Presentation interpolation alpha must be finite and inside [0, 1].");
            if (cueStartTick < 0 || cueStartTick > behaviorDurationTicks) throw new ArgumentOutOfRangeException(nameof(cueStartTick), cueStartTick, "Spine cue start tick must be inside the behavior duration.");
            if (behaviorDurationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(behaviorDurationTicks), behaviorDurationTicks, "Behavior duration must be positive.");
            if (!IsFinite(animationDurationSeconds) || animationDurationSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(animationDurationSeconds), animationDurationSeconds, "Animation duration must be finite and non-negative.");
            if (tickRate <= 0) throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "Presentation tick rate must be positive.");
            int effectiveEndTick = cueEndTick > cueStartTick ? Mathf.Min(cueEndTick, behaviorDurationTicks) : behaviorDurationTicks;
            double interpolatedRaw = phase.RawValue + phase.RateRaw * (double)interpolationAlpha;
            double behaviorTick = Math.Min(behaviorDurationTicks, interpolatedRaw / BehaviorPhase.One);
            double localTick = Math.Max(0d, Math.Min(effectiveEndTick - cueStartTick, behaviorTick - cueStartTick));
            if (loop) return (float)(localTick / tickRate);
            int cueDurationTicks = effectiveEndTick - cueStartTick;
            return cueDurationTicks <= 0 || animationDurationSeconds <= 0f ? 0f : (float)(animationDurationSeconds * localTick / cueDurationTicks);
        }

        /// <summary>按照 SkeletonRootMotionBase.GetSkeletonSpaceMovementDelta 的原始顺序，把 hips TranslateTimeline 位移转换为 Actor 局部位移。</summary>
        public static Vector3 ConvertBakedRootMotion(Vector3 bakedBoneLocalDisplacement, Vector2 skeletonScale, Vector2 parentBoneScale, SpineRootMotionAxisSettings settings)
        {
            if (!IsFinite(bakedBoneLocalDisplacement.x) || !IsFinite(bakedBoneLocalDisplacement.y) || !IsFinite(bakedBoneLocalDisplacement.z)) throw new ArgumentOutOfRangeException(nameof(bakedBoneLocalDisplacement), bakedBoneLocalDisplacement, "Baked Spine root motion must contain only finite values.");
            if (!IsFinite(skeletonScale.x) || !IsFinite(skeletonScale.y) || !IsFinite(parentBoneScale.x) || !IsFinite(parentBoneScale.y)) throw new ArgumentOutOfRangeException(nameof(skeletonScale), skeletonScale, "Spine skeleton and parent-bone scales must contain only finite values.");
            float skeletonDeltaX = bakedBoneLocalDisplacement.x * skeletonScale.x * parentBoneScale.x;
            float skeletonDeltaY = bakedBoneLocalDisplacement.y * skeletonScale.y * parentBoneScale.y;
            float rootMotionTranslationX = settings.RootMotionTranslateXPerY * skeletonDeltaY;
            float rootMotionTranslationY = settings.RootMotionTranslateYPerX * skeletonDeltaX;
            float resolvedX = settings.TransformPositionX ? skeletonDeltaX * settings.RootMotionScaleX + rootMotionTranslationX : 0f;
            float resolvedY = settings.TransformPositionY ? skeletonDeltaY * settings.RootMotionScaleY + rootMotionTranslationY : 0f;
            return new Vector3(resolvedX, resolvedY, bakedBoneLocalDisplacement.z);
        }

        /// <summary>判断浮点数是否可以安全用于表现采样。</summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>冻结行为拥有的 Spine Track，并在每个表现帧用 BehaviorPhase 显式 Seek；同时以非侵入方式复刻官方根运动骨骼抵消逻辑。</summary>
    public sealed class SpineBehaviorAuthorityRuntime : IDisposable
    {
        private readonly SkeletonAnimation skeletonAnimation;
        private readonly SkeletonRootMotion rootMotionSettingsSource;
        private readonly int tickRate;
        private readonly Bone rootMotionBone;
        private readonly List<Bone> topLevelBones = new List<Bone>();
        private readonly List<AuthoritativeTrack> authoritativeTracks = new List<AuthoritativeTrack>();
        private readonly Vector2 initialRootMotionOffset;
        private int behaviorDurationTicks;
        private bool behaviorActive;
        private bool poseCompensationEnabled;
        private bool disposed;

        /// <summary>创建单个 SkeletonAnimation 的行为表现权威；根骨名称由迁移资产明确传入，避免依赖插件保护字段。</summary>
        public SpineBehaviorAuthorityRuntime(SkeletonAnimation skeletonAnimation, SkeletonRootMotion rootMotionSettingsSource, int tickRate, string rootMotionBoneName)
        {
            this.skeletonAnimation = skeletonAnimation != null ? skeletonAnimation : throw new ArgumentNullException(nameof(skeletonAnimation));
            this.rootMotionSettingsSource = rootMotionSettingsSource;
            this.tickRate = tickRate > 0 ? tickRate : throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "Spine authority tick rate must be positive.");
            if (string.IsNullOrWhiteSpace(rootMotionBoneName)) throw new ArgumentException("Spine root-motion bone name cannot be empty.", nameof(rootMotionBoneName));
            skeletonAnimation.Initialize(false);
            Skeleton skeleton = skeletonAnimation.Skeleton ?? throw new InvalidOperationException($"SkeletonAnimation '{skeletonAnimation.name}' cannot initialize its Skeleton data.");
            rootMotionBone = skeleton.FindBone(rootMotionBoneName);
            initialRootMotionOffset = rootMotionBone == null ? Vector2.zero : new Vector2(rootMotionBone.Data.X, rootMotionBone.Data.Y);
            if (rootMotionBone != null) foreach (Bone bone in skeleton.Bones) if (bone.Parent == null) topLevelBones.Add(bone);
            skeletonAnimation.UpdateLocal += HandleUpdateLocal;
        }

        /// <summary>获取当前是否存在一个由 BehaviorPhase 驱动的行为表现。</summary>
        public bool HasActiveBehavior => behaviorActive;

        /// <summary>获取当前是否会在动画应用后抵消已经提取到对象运动中的 hips 位移。</summary>
        public bool IsPoseCompensationEnabled => poseCompensationEnabled;

        /// <summary>获取当前 Skeleton 是否包含资产指定的根运动骨骼。</summary>
        public bool SupportsRootMotion => rootMotionBone != null;

        /// <summary>开始新的行为相位上下文并清除上一行为遗留的 Track 所有权。</summary>
        public void BeginBehavior(int durationTicks)
        {
            ThrowIfDisposed();
            if (durationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks), durationTicks, "Spine behavior duration must be positive.");
            authoritativeTracks.Clear();
            behaviorDurationTicks = durationTicks;
            behaviorActive = true;
            poseCompensationEnabled = false;
        }

        /// <summary>登记一个行为 Cue 创建的 TrackEntry 并冻结其自由时间推进；混合时间仍由 Spine AnimationState 正常更新。</summary>
        public void RegisterTrack(TrackEntry trackEntry, int cueStartTick, int cueEndTick, bool loop)
        {
            ThrowIfDisposed();
            if (!behaviorActive) throw new InvalidOperationException("A Spine track can only be registered inside an active behavior.");
            if (trackEntry == null) throw new ArgumentNullException(nameof(trackEntry));
            if (cueStartTick < 0 || cueStartTick > behaviorDurationTicks) throw new ArgumentOutOfRangeException(nameof(cueStartTick), cueStartTick, "Spine cue start tick must be inside the behavior duration.");
            trackEntry.TimeScale = 0f;
            authoritativeTracks.Add(new AuthoritativeTrack(trackEntry, cueStartTick, cueEndTick, loop));
        }

        /// <summary>注销已经结束或被其他系统接管的 TrackEntry，避免后续相位 Seek 修改失去所有权的轨道。</summary>
        public void UnregisterTrack(TrackEntry trackEntry)
        {
            if (trackEntry == null) return;
            for (int index = authoritativeTracks.Count - 1; index >= 0; index--) if (ReferenceEquals(authoritativeTracks[index].TrackEntry, trackEntry)) authoritativeTracks.RemoveAt(index);
        }

        /// <summary>用最近完成的模拟相位和渲染插值显式校正所有行为轨道，并立即以零增量应用姿势，消除脚本执行顺序造成的一帧延迟。</summary>
        public void Present(BehaviorPhase phase, float interpolationAlpha)
        {
            ThrowIfDisposed();
            if (!behaviorActive) return;
            for (int index = 0; index < authoritativeTracks.Count; index++)
            {
                AuthoritativeTrack track = authoritativeTracks[index];
                TrackEntry entry = track.TrackEntry;
                float animationDuration = entry.Animation == null ? 0f : entry.Animation.Duration;
                entry.TrackTime = SpineBehaviorAuthorityMath.ResolveTrackTime(phase, interpolationAlpha, track.CueStartTick, track.CueEndTick, behaviorDurationTicks, animationDuration, track.Loop, tickRate);
            }
            skeletonAnimation.Update(0f);
        }

        /// <summary>启用或关闭根运动姿势抵消；只有已经由模拟 MotionClip 消费烘焙位移的行为才应启用。</summary>
        public void SetPoseCompensation(bool enabled)
        {
            ThrowIfDisposed();
            if (enabled && rootMotionBone == null) throw new InvalidOperationException($"SkeletonAnimation '{skeletonAnimation.name}' does not contain the configured root-motion bone.");
            poseCompensationEnabled = enabled && behaviorActive;
        }

        /// <summary>把离线烘焙的 hips 局部位移转换为当前朝向、父骨缩放和 RootMotionScale 对应的 Actor 局部位移。</summary>
        public Vector3 ConvertBakedRootMotion(Vector3 bakedBoneLocalDisplacement)
        {
            ThrowIfDisposed();
            if (rootMotionBone == null) throw new InvalidOperationException($"SkeletonAnimation '{skeletonAnimation.name}' cannot convert baked root motion because its configured root-motion bone is missing.");
            Skeleton skeleton = skeletonAnimation.Skeleton;
            Vector2 parentBoneScale = GetParentBoneScale();
            return SpineBehaviorAuthorityMath.ConvertBakedRootMotion(bakedBoneLocalDisplacement, new Vector2(skeleton.ScaleX, skeleton.ScaleY), parentBoneScale, SpineRootMotionAxisSettings.From(rootMotionSettingsSource));
        }

        /// <summary>结束行为相位上下文；Track 的实际清理仍由拥有 Cue 的 ActorPresentationRuntime 执行。</summary>
        public void EndBehavior()
        {
            if (disposed) return;
            authoritativeTracks.Clear();
            behaviorDurationTicks = 0;
            behaviorActive = false;
            poseCompensationEnabled = false;
        }

        /// <summary>解除 SkeletonAnimation 回调并清空所有行为状态；重复释放保持幂等。</summary>
        public void Dispose()
        {
            if (disposed) return;
            EndBehavior();
            skeletonAnimation.UpdateLocal -= HandleUpdateLocal;
            disposed = true;
        }

        /// <summary>在 Spine 应用动画后、计算世界骨骼前复刻 SkeletonRootMotionBase.ClearEffectiveBoneOffsets，防止对象移动与骨骼平移叠加。</summary>
        private void HandleUpdateLocal(ISkeletonAnimation animatedSkeleton)
        {
            if (!behaviorActive || !poseCompensationEnabled) return;
            SpineRootMotionAxisSettings settings = SpineRootMotionAxisSettings.From(rootMotionSettingsSource);
            Vector2 parentBoneScale = GetParentBoneScale();
            for (int index = 0; index < topLevelBones.Count; index++)
            {
                Bone topLevelBone = topLevelBones[index];
                if (ReferenceEquals(topLevelBone, rootMotionBone))
                {
                    if (settings.TransformPositionX) topLevelBone.X = 0f;
                    if (settings.TransformPositionY) topLevelBone.Y = 0f;
                    continue;
                }
                if (settings.TransformPositionX) topLevelBone.X = (initialRootMotionOffset.x - rootMotionBone.X) * parentBoneScale.x;
                if (settings.TransformPositionY) topLevelBone.Y = (initialRootMotionOffset.y - rootMotionBone.Y) * parentBoneScale.y;
            }
        }

        /// <summary>按照官方实现从 hips 的直接父骨一路累乘到顶层，保留负缩放产生的方向反转。</summary>
        private Vector2 GetParentBoneScale()
        {
            Vector2 parentBoneScale = Vector2.one;
            if (rootMotionBone == null) return parentBoneScale;
            Bone scaleBone = rootMotionBone;
            while ((scaleBone = scaleBone.Parent) != null)
            {
                parentBoneScale.x *= scaleBone.ScaleX;
                parentBoneScale.y *= scaleBone.ScaleY;
            }
            return parentBoneScale;
        }

        /// <summary>阻止已经释放的 Spine 行为权威继续访问可能已回收的 Skeleton 对象。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SpineBehaviorAuthorityRuntime));
        }

        /// <summary>保存一个行为 Cue 对 Spine TrackEntry 的相位映射，不拥有 TrackEntry 的释放生命周期。</summary>
        private readonly struct AuthoritativeTrack
        {
            /// <summary>创建一个只读轨道相位映射。</summary>
            public AuthoritativeTrack(TrackEntry trackEntry, int cueStartTick, int cueEndTick, bool loop)
            {
                TrackEntry = trackEntry;
                CueStartTick = cueStartTick;
                CueEndTick = cueEndTick;
                Loop = loop;
            }

            /// <summary>获取由 ActorPresentationRuntime 创建并拥有的轨道条目。</summary>
            public TrackEntry TrackEntry { get; }

            /// <summary>获取 Cue 在行为相位中的开始 Tick。</summary>
            public int CueStartTick { get; }

            /// <summary>获取 Cue 的排除式结束 Tick；零表示持续到行为结束。</summary>
            public int CueEndTick { get; }

            /// <summary>获取轨道是否按动画自身时长循环。</summary>
            public bool Loop { get; }
        }
    }
}
