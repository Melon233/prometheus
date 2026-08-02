using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// 将固定 Tick 产生的一次性落地事件锁存为按渲染时间消费的短表现状态，使同一渲染帧内执行多个固定 Tick 时仍然不会丢失 Land。
    /// </summary>
    public sealed class ActorLandPresentationState
    {
        private float remainingSeconds;

        /// <summary>获取落地表现当前是否仍应占用基础动画通道。</summary>
        public bool IsActive => remainingSeconds > 0f;

        /// <summary>锁存一次落地事件；重复触发只会延长而不会缩短已经存在的表现窗口。</summary>
        public void Trigger(float durationSeconds)
        {
            if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) || durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Land presentation duration must be finite and positive.");
            remainingSeconds = Mathf.Max(remainingSeconds, durationSeconds);
        }

        /// <summary>消费一个渲染帧并返回本帧是否应展示 Land；先判定后扣时保证大帧间隔下也至少展示一次。</summary>
        public bool ConsumeFrame(float frameDeltaTime)
        {
            if (float.IsNaN(frameDeltaTime) || float.IsInfinity(frameDeltaTime) || frameDeltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(frameDeltaTime), frameDeltaTime, "Presentation frame delta time must be finite and non-negative.");
            if (remainingSeconds <= 0f) return false;
            remainingSeconds = Mathf.Max(0f, remainingSeconds - frameDeltaTime);
            return true;
        }

        /// <summary>立即清除落地锁定，用于更高优先级行为接管或运行时释放。</summary>
        public void Clear()
        {
            remainingSeconds = 0f;
        }
    }

    /// <summary>
    /// 将权威 Behavior 的稳定编号和 Tick 相位解释为 Spine、音效、VFX、镜头与表现事件；任何表现回调都不能反向结束模拟行为。
    /// </summary>
    public sealed class ActorPresentationRuntime : IDisposable
    {
        private readonly ActorAuthoringComponent authoring;
        private readonly SpineComponent spineComponent;
        private readonly CameraDirectorSystem cameraDirector;
        private readonly int tickRate;
        private readonly SpineBehaviorAuthorityRuntime spineAuthorityRuntime;
        private readonly List<ActiveCue> activeCues = new List<ActiveCue>();
        private readonly ActorLandPresentationState landPresentationState = new ActorLandPresentationState();
        private ActorBehaviorDefinition activeBehavior;
        private ActorPresentationVariantDefinition activeVariant;
        private BehaviorHandle activeHandle;
        private int lastProcessedTick = -1;
        private AnimationReferenceAsset currentLocomotionAnimation;
        private bool disposed;
        private const float DefaultLandPresentationSeconds = 0.12f;

        /// <summary>
        /// 创建一个只属于单个 GameplayObject 的客户端表现运行时。
        /// </summary>
        public ActorPresentationRuntime(ActorAuthoringComponent authoring, SpineComponent spineComponent, CameraDirectorSystem cameraDirector, int tickRate)
        {
            this.authoring = authoring != null ? authoring : throw new ArgumentNullException(nameof(authoring));
            this.spineComponent = spineComponent != null ? spineComponent : throw new ArgumentNullException(nameof(spineComponent));
            this.cameraDirector = cameraDirector;
            this.tickRate = tickRate > 0 ? tickRate : throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "Presentation tick rate must be positive.");
            if (spineComponent.spineAnimator == null) throw new InvalidOperationException($"Actor '{authoring.name}' requires an initialized SkeletonAnimation before creating its presentation runtime.");
            spineAuthorityRuntime = new SpineBehaviorAuthorityRuntime(spineComponent.spineAnimator, spineComponent.GetComponent<SkeletonRootMotion>(), this.tickRate, "hips");
        }

        /// <summary>表现时间轴触发 PresentationEvent Cue 时发布稳定事件编号。</summary>
        public event Action<string> PresentationEvent;

        /// <summary>获取当前表现是否由一个权威行为实例占用。</summary>
        public bool HasActiveBehavior => activeBehavior != null;

        /// <summary>启动一个行为实例的指定表现 Variant，并立即处理 Tick 0 Cue。</summary>
        public void BeginBehavior(BehaviorHandle handle, ActorBehaviorDefinition behavior, string variantId, int rateRaw)
        {
            ThrowIfDisposed();
            if (!handle.IsValid) throw new ArgumentException("Presentation requires a valid behavior handle.", nameof(handle));
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));
            if (rateRaw <= 0) throw new ArgumentOutOfRangeException(nameof(rateRaw), rateRaw, "Presentation behavior rate must be positive.");
            if (!behavior.TryGetPresentationVariant(variantId, out ActorPresentationVariantDefinition variant)) throw new InvalidOperationException($"Behavior '{behavior.BehaviorId}' does not contain presentation variant '{variantId}'.");
            EndBehaviorInternal();
            landPresentationState.Clear();
            activeHandle = handle;
            activeBehavior = behavior;
            activeVariant = variant;
            lastProcessedTick = -1;
            spineAuthorityRuntime.BeginBehavior(behavior.DurationTicks);
            AdvanceToTick(0);
        }

        /// <summary>用权威 BehaviorPhase 和渲染插值显式采样行为 Spine 轨道；frameDeltaTime 只参与参数校验，绝不驱动行为动画时间。</summary>
        public void PresentBehavior(float frameDeltaTime, float interpolationAlpha, BehaviorPhase activePhase)
        {
            ThrowIfDisposed();
            if (float.IsNaN(frameDeltaTime) || float.IsInfinity(frameDeltaTime) || frameDeltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(frameDeltaTime), frameDeltaTime, "Presentation frame delta time must be finite and non-negative.");
            if (activeBehavior == null) return;
            AdvanceToTick(Mathf.Min(activePhase.Tick, activeBehavior.DurationTicks));
            spineAuthorityRuntime.Present(activePhase, interpolationAlpha);
        }

        /// <summary>把 Spine 离线烘焙的 hips 位移转换为当前 Actor 朝向与旧 RootMotion 参数对应的局部位移。</summary>
        public Vector3 ConvertBakedRootMotion(Vector3 bakedBoneLocalDisplacement)
        {
            ThrowIfDisposed();
            return spineAuthorityRuntime.ConvertBakedRootMotion(bakedBoneLocalDisplacement);
        }

        /// <summary>控制动画姿势是否抵消已经由 MotionClip 提取并交给对象运动权威的 hips 平移。</summary>
        public void SetRootMotionPoseCompensation(bool enabled)
        {
            ThrowIfDisposed();
            spineAuthorityRuntime.SetPoseCompensation(enabled);
        }

        /// <summary>按顺序处理从上一相位到指定行为 Tick 之间的所有 Cue 边界，保证高倍速不会跳过短 Cue。</summary>
        public void AdvanceToTick(int behaviorTick)
        {
            ThrowIfDisposed();
            if (activeBehavior == null) return;
            int clampedTick = Mathf.Clamp(behaviorTick, 0, activeBehavior.DurationTicks);
            if (clampedTick < lastProcessedTick) throw new ArgumentOutOfRangeException(nameof(behaviorTick), behaviorTick, "Presentation behavior ticks must be monotonic.");
            for (int tick = lastProcessedTick + 1; tick <= clampedTick; tick++)
            {
                ExitCuesAtTick(tick);
                EnterCuesAtTick(tick);
            }
            lastProcessedTick = clampedTick;
        }

        /// <summary>结束与句柄完全匹配的行为表现，并只清理由当前运行时仍然拥有的 Track 和镜头请求。</summary>
        public bool EndBehavior(BehaviorHandle handle)
        {
            ThrowIfDisposed();
            if (activeBehavior == null || handle != activeHandle) return false;
            EndBehaviorInternal();
            return true;
        }

        /// <summary>锁存固定 Tick 产生的落地事件；更高优先级行为占用表现通道时不会排队播放过期 Land。</summary>
        public void NotifyLanded()
        {
            ThrowIfDisposed();
            if (activeBehavior != null) return;
            AnimationReferenceAsset landAnimation = authoring.Definition.LocomotionPresentation.Land;
            if (landAnimation == null) return;
            float authoredDuration = landAnimation.Animation == null ? 0f : landAnimation.Animation.Duration;
            landPresentationState.Trigger(Mathf.Max(DefaultLandPresentationSeconds, authoredDuration));
            currentLocomotionAnimation = null;
        }

        /// <summary>当行为通道空闲时根据运动快照和已锁存的落地状态选择并播放基础 Spine 状态动画。</summary>
        public void PresentLocomotion(ActorMotionSnapshot motion, bool sprinting, float frameDeltaTime)
        {
            ThrowIfDisposed();
            if (activeBehavior != null)
            {
                landPresentationState.Clear();
                return;
            }
            ActorLocomotionPresentationDefinition locomotion = authoring.Definition.LocomotionPresentation;
            AnimationReferenceAsset desiredAnimation;
            if (locomotion.Land != null && landPresentationState.ConsumeFrame(frameDeltaTime)) desiredAnimation = locomotion.Land;
            else if (!motion.IsGrounded) desiredAnimation = motion.Velocity.y >= 0f ? locomotion.Jump : locomotion.Fall;
            else if (new Vector2(motion.Velocity.x, motion.Velocity.z).sqrMagnitude > 0.01f) desiredAnimation = sprinting && locomotion.Sprint != null ? locomotion.Sprint : locomotion.Move;
            else desiredAnimation = locomotion.Idle;
            bool holdsCompletedLandPose = desiredAnimation == locomotion.Land;
            if (desiredAnimation == null || desiredAnimation == currentLocomotionAnimation && (holdsCompletedLandPose || spineComponent.IsPlaying(desiredAnimation, 0))) return;
            currentLocomotionAnimation = desiredAnimation;
            spineComponent.Play(desiredAnimation, desiredAnimation == locomotion.Idle || desiredAnimation == locomotion.Move || desiredAnimation == locomotion.Sprint || desiredAnimation == locomotion.Jump || desiredAnimation == locomotion.Fall, 0, locomotion.MixDuration);
        }

        /// <summary>释放全部行为 Cue、镜头请求和事件订阅；重复调用保持幂等。</summary>
        public void Dispose()
        {
            if (disposed) return;
            EndBehaviorInternal();
            spineAuthorityRuntime.Dispose();
            PresentationEvent = null;
            currentLocomotionAnimation = null;
            landPresentationState.Clear();
            disposed = true;
        }

        /// <summary>处理一个 Tick 上结束的持续 Cue，顺序先于同 Tick 新 Cue 进入。</summary>
        private void ExitCuesAtTick(int tick)
        {
            for (int index = activeCues.Count - 1; index >= 0; index--)
            {
                ActiveCue activeCue = activeCues[index];
                if (activeCue.Definition.EndTick <= activeCue.Definition.StartTick || activeCue.Definition.EndTick != tick) continue;
                CleanupCue(activeCue);
                activeCues.RemoveAt(index);
            }
        }

        /// <summary>按资产顺序触发一个 Tick 上开始的全部客户端表现 Cue。</summary>
        private void EnterCuesAtTick(int tick)
        {
            IReadOnlyList<ActorPresentationCueDefinition> cues = activeVariant.Cues;
            for (int index = 0; index < cues.Count; index++)
            {
                ActorPresentationCueDefinition cue = cues[index];
                if (cue == null || cue.StartTick != tick) continue;
                ActiveCue activeCue = TriggerCue(cue);
                if (activeCue != null && (cue.EndTick > cue.StartTick || cue.Kind == ActorPresentationCueKind.SpineAnimation || cue.Kind == ActorPresentationCueKind.Camera)) activeCues.Add(activeCue);
            }
        }

        /// <summary>把一个资产 Cue 路由到对应客户端适配器。</summary>
        private ActiveCue TriggerCue(ActorPresentationCueDefinition cue)
        {
            switch (cue.Kind)
            {
                case ActorPresentationCueKind.SpineAnimation:
                    if (cue.Animation == null) throw new InvalidOperationException($"Presentation cue '{cue.CueId}' requires a Spine animation.");
                    TrackEntry entry = spineComponent.Play(cue.Animation, cue.Loop, cue.SpineTrack, cue.MixIn);
                    spineAuthorityRuntime.RegisterTrack(entry, cue.StartTick, cue.EndTick, cue.Loop);
                    return new ActiveCue(cue, entry, default);
                case ActorPresentationCueKind.Audio:
                    if (cue.AudioClip != null) AudioKit.Instance.Play(cue.AudioClip);
                    return null;
                case ActorPresentationCueKind.Vfx:
                    if (!authoring.TryPlayVfx(cue.BindingId)) Debug.LogWarning($"Actor '{authoring.name}' cannot resolve VFX binding '{cue.BindingId}' for cue '{cue.CueId}'.");
                    return null;
                case ActorPresentationCueKind.Camera:
                    if (cameraDirector == null || authoring.CameraSubject == null || cue.CameraProfile == null) return null;
                    CameraRequestHandle cameraHandle = cameraDirector.PushRequest(new CameraRequest(activeHandle.InstanceId, authoring.CameraSubject, cue.CameraProfile, cue.CameraPriority));
                    return new ActiveCue(cue, null, cameraHandle);
                case ActorPresentationCueKind.PresentationEvent:
                    PresentationEvent?.Invoke(cue.BindingId);
                    return null;
                default: throw new ArgumentOutOfRangeException(nameof(cue), cue.Kind, "Unsupported actor presentation cue kind.");
            }
        }

        /// <summary>清理一个仍然活跃的 Cue，并通过 TrackEntry 引用验证避免清除其他系统后来接管的动画。</summary>
        private void CleanupCue(ActiveCue activeCue)
        {
            spineAuthorityRuntime.UnregisterTrack(activeCue.TrackEntry);
            if (activeCue.TrackEntry != null && spineComponent.spineAnimator != null)
            {
                TrackEntry current = spineComponent.spineAnimator.AnimationState.GetCurrent(activeCue.Definition.SpineTrack);
                if (ReferenceEquals(current, activeCue.TrackEntry)) spineComponent.spineAnimator.AnimationState.SetEmptyAnimation(activeCue.Definition.SpineTrack, activeCue.Definition.MixOut);
            }
            if (activeCue.CameraHandle.IsValid && cameraDirector != null) cameraDirector.ReleaseRequest(activeCue.CameraHandle);
        }

        /// <summary>从任意结束路径统一逆序清理全部持续 Cue 并重置行为表现状态。</summary>
        private void EndBehaviorInternal()
        {
            for (int index = activeCues.Count - 1; index >= 0; index--) CleanupCue(activeCues[index]);
            activeCues.Clear();
            activeBehavior = null;
            activeVariant = null;
            activeHandle = default;
            lastProcessedTick = -1;
            spineAuthorityRuntime.EndBehavior();
        }

        /// <summary>阻止已经释放的表现运行时继续操作场景对象。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ActorPresentationRuntime));
        }

        /// <summary>保存一个持续 Cue 独占的 Spine TrackEntry 或镜头请求句柄。</summary>
        private sealed class ActiveCue
        {
            /// <summary>创建一个持续 Cue 运行时记录。</summary>
            internal ActiveCue(ActorPresentationCueDefinition definition, TrackEntry trackEntry, CameraRequestHandle cameraHandle)
            {
                Definition = definition;
                TrackEntry = trackEntry;
                CameraHandle = cameraHandle;
            }

            /// <summary>获取 Cue 资产配置。</summary>
            internal ActorPresentationCueDefinition Definition { get; }

            /// <summary>获取当前运行时拥有的可选 Spine TrackEntry。</summary>
            internal TrackEntry TrackEntry { get; }

            /// <summary>获取当前运行时拥有的可选镜头请求句柄。</summary>
            internal CameraRequestHandle CameraHandle { get; }
        }
    }
}
