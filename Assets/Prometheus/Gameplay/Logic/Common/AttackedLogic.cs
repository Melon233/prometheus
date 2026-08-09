using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus
{
    /// <summary>负责受击 AnimationLine 会话与受击开始/结束事件，连续受击和高优先级打断均保持成对通知。</summary>
    public sealed class AttackedLogic : Logic.Logic
    {
        private EventComponent eventComponent;
        private SpineComponent spineComponent;
        private AnimationPlayback playback;
        private bool replacingPlayback;
        private bool reactionActive;

        public override void AfterNew()
        {
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out eventComponent);
            Entity.TryGetComp(out spineComponent);
            eventComponent.AddListener<AttackedEvent>(OnAttacked);
        }

        /// <summary>以高于玩家动作的优先级播放单段或双段受击序列，双段动画通过 AddAnimation 正确排队。</summary>
        private void OnAttacked(AttackedEvent evt)
        {
            AttackedExecutor configuration = spineComponent.animationLib.attackedExecutor;
            replacingPlayback = playback != null;
            AnimationPlayback newPlayback;
            try
            {
                newPlayback = configuration.HasRecoveryAnimation ? spineComponent.TryPlaySequence(configuration.Semantic, configuration.RecoverySemantic, AnimationOwner.HitReaction, AnimationPriority.HitReaction, false, 1f, true) : spineComponent.TryPlay(configuration.Semantic, AnimationOwner.HitReaction, AnimationPriority.HitReaction, false, 1f, true);
            }
            finally
            {
                replacingPlayback = false;
            }
            if (newPlayback == null) return;
            playback = newPlayback;
            playback.Finished += OnAnimationFinished;
            if (!reactionActive)
            {
                reactionActive = true;
                eventComponent.Invoke(new AttackedStartEvent());
            }
            if (configuration.AudioClip != null) AudioKit.Ins.Play(configuration.AudioClip);
        }

        /// <summary>连续受击替换只更新播放会话，最后一次自然完成或外部高优先级抢占时才发布受击结束事件。</summary>
        private void OnAnimationFinished(AnimationPlayback source, AnimationEndReason reason)
        {
            if (!ReferenceEquals(source, playback)) return;
            playback = null;
            if (replacingPlayback && reason == AnimationEndReason.Interrupted) return;
            if (!reactionActive) return;
            reactionActive = false;
            eventComponent.Invoke(new AttackedEndEvent());
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override void OnDisable()
        {
        }

        /// <summary>回收时注销实体事件和会话回调，不在实体释放阶段再次发布受击结束事件。</summary>
        public override void OnDispose()
        {
            if (eventComponent != null) eventComponent.RemoveListener<AttackedEvent>(OnAttacked);
            if (playback != null) playback.Finished -= OnAnimationFinished;
            playback = null;
            replacingPlayback = false;
            reactionActive = false;
            eventComponent = null;
            spineComponent = null;
        }

        public override void OnEnable()
        {
        }

        public override void OnUpdate(float dt)
        {
        }
    }
}
