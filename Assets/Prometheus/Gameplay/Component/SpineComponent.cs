using System;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>定义角色朝向，Spine 骨架缩放与三维特效根节点旋转会保持一致。</summary>
    public enum FaceDir
    {
        Left,
        Right
    }

    /// <summary>作为角色唯一的 Spine 动画播放组件，负责 AnimationLine 解析、优先级仲裁、序列播放和会话清理。</summary>
    // [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(SkeletonAnimation))]
    public sealed class SpineComponent : MonoComponent
    {
        /// <summary>定义所有主轨动画之间统一使用的过渡时间，避免各 Logic 产生不一致的混合观感。</summary>
        public const float TransitionDuration = 0.2f;

        private AnimationPlayback currentPlayback;
        private int nextPlaybackVersion;

        [NonSerialized] public SkeletonAnimation spineAnimator;
        [SerializeField] public AnimationLibrary animationLib;
        [SerializeField] public Transform rotateRoot;

        /// <summary>获取当前仍然拥有主轨道的动画会话；没有活动会话时返回空。</summary>
        public AnimationPlayback CurrentPlayback => currentPlayback;

        /// <summary>获取或设置角色朝向，并同步 Spine 水平缩放与特效旋转根节点。</summary>
        public FaceDir CurFaceDir
        {
            get
            {
                return spineAnimator != null && spineAnimator.skeleton.ScaleX > 0f ? FaceDir.Right : FaceDir.Left;
            }
            set
            {
                if (spineAnimator == null || spineAnimator.skeleton == null) return;
                bool faceRight = value == FaceDir.Right;
                spineAnimator.skeleton.ScaleX = faceRight ? 1f : -1f;
                if (rotateRoot != null) rotateRoot.rotation = Quaternion.Euler(faceRight ? Vector3.zero : new Vector3(0f, 180f, 0f));
            }
        }

        /// <summary>根据二维移动方向更新朝向，横向输入为零时保持当前朝向。</summary>
        public void SetFaceDir(Vector2 moveDir)
        {
            SetFaceDir(moveDir.x);
        }

        /// <summary>根据横向输入更新朝向，输入为零时保持当前朝向。</summary>
        public void SetFaceDir(float horizontal)
        {
            if (horizontal > 0f) CurFaceDir = FaceDir.Right;
            else if (horizontal < 0f) CurFaceDir = FaceDir.Left;
        }

        /// <summary>缓存 Spine 组件并尽早校验共享动画配置；AnimationLibrary 保持纯配置且不再按实体克隆。</summary>
        private void Awake()
        {
            spineAnimator = GetComponent<SkeletonAnimation>();
            if (spineAnimator != null && spineAnimator.AnimationState != null) spineAnimator.AnimationState.Data.DefaultMix = TransitionDuration;
            if (animationLib == null) Debug.LogError($"角色 '{name}' 未配置 AnimationLibrary。", this);
        }

        /// <summary>组件销毁时结束当前动画会话并通知其持有 Logic。</summary>
        private void OnDestroy()
        {
            AnimationPlayback playback = currentPlayback;
            currentPlayback = null;
            playback?.Finish(AnimationEndReason.Disposed);
        }

        /// <summary>通过动画语义解析角色专属 AnimationLine 并尝试播放；低优先级请求会被拒绝，同所有者同语义默认复用当前会话。</summary>
        public AnimationPlayback TryPlay(AnimationSemantic semantic, AnimationOwner owner, AnimationPriority priority, bool loop = false, float speed = 1f, bool restart = false, int trackIndex = 0)
        {
            if (animationLib == null || !animationLib.TryGetLine(semantic, out AnimationLine line)) return null;
            return TryPlayResolvedSequence(line, null, owner, priority, loop, speed, restart, trackIndex);
        }

        /// <summary>通过两个动画语义播放 AnimationLine 序列；片段间使用统一过渡，整个序列共享同一优先级和结束生命周期。</summary>
        public AnimationPlayback TryPlaySequence(AnimationSemantic firstSemantic, AnimationSemantic finalSemantic, AnimationOwner owner, AnimationPriority priority, bool finalLoop = false, float speed = 1f, bool restart = false, int trackIndex = 0)
        {
            if (animationLib == null || !animationLib.TryGetLine(firstSemantic, out AnimationLine firstLine) || !animationLib.TryGetLine(finalSemantic, out AnimationLine finalLine)) return null;
            return TryPlayResolvedSequence(firstLine, finalLine, owner, priority, finalLoop, speed, restart, trackIndex);
        }

        /// <summary>仅当当前动画属于指定所有者时以统一时长淡出到 Setup Pose，避免硬清轨残留未被后续动画关键帧覆盖的骨骼姿势。</summary>
        public bool Stop(AnimationOwner owner, AnimationEndReason reason = AnimationEndReason.Stopped, int trackIndex = 0)
        {
            AnimationPlayback playback = currentPlayback;
            if (playback == null || playback.Owner != owner || playback.TrackIndex != trackIndex) return false;
            playback.DetachTrackCallbacks();
            currentPlayback = null;
            spineAnimator.AnimationState.SetEmptyAnimation(trackIndex, TransitionDuration);
            playback.Finish(reason);
            return true;
        }

        /// <summary>无条件清理指定轨道、恢复 Setup Pose 并结束其会话，仅供死亡、回收和基础设施级流程使用。</summary>
        public void ClearTrack(int trackIndex = 0, AnimationEndReason reason = AnimationEndReason.Stopped)
        {
            AnimationPlayback playback = currentPlayback;
            if (playback != null && playback.TrackIndex == trackIndex)
            {
                playback.DetachTrackCallbacks();
                currentPlayback = null;
            }
            spineAnimator.AnimationState.ClearTrack(trackIndex);
            if (spineAnimator.Skeleton != null) spineAnimator.Skeleton.SetToSetupPose();
            playback?.Finish(reason);
        }

        /// <summary>调整当前指定轨道的播放速度；轨道为空时保持安全并返回失败。</summary>
        public bool SetSpeed(float speed = 1f, int trackIndex = 0)
        {
            TrackEntry entry = spineAnimator.AnimationState.GetCurrent(trackIndex);
            if (entry == null) return false;
            entry.TimeScale = speed;
            return true;
        }

        /// <summary>获取 Spine 当前轨道上的动画名称；轨道为空时返回空。</summary>
        public string GetCurrentAnimation(int trackIndex = 0)
        {
            return spineAnimator.AnimationState.GetCurrent(trackIndex)?.Animation?.Name;
        }

        /// <summary>判断指定轨道是否已经没有受控会话，或非循环动画已经自然完成。</summary>
        public bool IsEmpty(int trackIndex = 0)
        {
            if (currentPlayback != null && currentPlayback.TrackIndex == trackIndex && currentPlayback.IsActive) return false;
            TrackEntry entry = spineAnimator.AnimationState.GetCurrent(trackIndex);
            return entry == null || entry.Animation == null || entry.IsComplete;
        }

        /// <summary>判断给定会话是否仍是组件当前会话，AnimationPlayback 使用该入口排除旧 TrackEntry 回调。</summary>
        internal bool IsPlaybackActive(AnimationPlayback playback)
        {
            return playback != null && ReferenceEquals(currentPlayback, playback) && currentPlayback.Version == playback.Version;
        }

        /// <summary>接收最终 TrackEntry 的自然完成通知，先释放轨道优先级所有权再通知 Logic。</summary>
        internal void CompletePlayback(AnimationPlayback playback)
        {
            if (!IsPlaybackActive(playback)) return;
            currentPlayback = null;
            playback.Finish(AnimationEndReason.Completed);
        }

        /// <summary>执行已经解析完成的单段或双段序列，并以原子顺序完成旧会话替换和新会话发布。</summary>
        private AnimationPlayback TryPlayResolvedSequence(AnimationLine firstLine, AnimationLine finalLine, AnimationOwner owner, AnimationPriority priority, bool finalLoop, float speed, bool restart, int trackIndex)
        {
            if (spineAnimator == null || firstLine == null) return null;
            Spine.Animation firstAnimation = firstLine.GetRuntimeAnimation();
            Spine.Animation finalAnimation = finalLine == null ? firstAnimation : finalLine.GetRuntimeAnimation();
            if (firstAnimation == null || finalAnimation == null) return null;
            AnimationSemantic requestedSemantic = finalLine == null ? firstLine.Semantic : finalLine.Semantic;
            AnimationPlayback previousPlayback = currentPlayback;
            if (previousPlayback != null && previousPlayback.TrackIndex == trackIndex)
            {
                if ((int)priority < (int)previousPlayback.Priority) return null;
                bool sameRequest = previousPlayback.Owner == owner && previousPlayback.Priority == priority && previousPlayback.Semantic == requestedSemantic;
                if (sameRequest && !restart) return previousPlayback;
                previousPlayback.DetachTrackCallbacks();
            }
            TrackEntry firstEntry = spineAnimator.AnimationState.SetAnimation(trackIndex, firstAnimation, finalLine == null && finalLoop);
            firstEntry.MixDuration = TransitionDuration;
            firstEntry.MixBlend = MixBlend.Replace;
            firstEntry.TimeScale = Mathf.Max(0f, speed);
            TrackEntry resolvedFinalEntry = firstEntry;
            if (finalLine != null)
            {
                resolvedFinalEntry = spineAnimator.AnimationState.AddAnimation(trackIndex, finalAnimation, finalLoop, 0f);
                resolvedFinalEntry.Delay = Mathf.Max(0f, firstEntry.AnimationEnd - firstEntry.AnimationStart - TransitionDuration * firstEntry.TimeScale);
                resolvedFinalEntry.MixDuration = TransitionDuration;
                resolvedFinalEntry.MixBlend = MixBlend.Replace;
                resolvedFinalEntry.TimeScale = Mathf.Max(0f, speed);
            }
            AnimationPlayback playback = new AnimationPlayback(this, requestedSemantic, owner, priority, trackIndex, ++nextPlaybackVersion);
            playback.AddEntry(firstEntry);
            if (!ReferenceEquals(firstEntry, resolvedFinalEntry)) playback.AddEntry(resolvedFinalEntry);
            playback.Seal(resolvedFinalEntry, finalLoop);
            currentPlayback = playback;
            previousPlayback?.Finish(AnimationEndReason.Interrupted);
            return playback;
        }
    }
}
