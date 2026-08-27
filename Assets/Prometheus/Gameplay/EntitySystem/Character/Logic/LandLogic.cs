using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>消费单帧落地标记并请求落地动画；待机优先级更低无法打断，移动优先级更高可以立即抢占。</summary>
    public sealed class LandLogic : Logic
    {
        private SpineComponent spineComponent;
        private InputComponent inputComponent;
        private MotionComponent motionComponent;
        private AnimationPlayback playback;

        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out motionComponent);
        }

        public override bool CanEnable()
        {
            return motionComponent.landThisFrame && inputComponent.moveDir == Vector2.zero;
        }

        public override bool CanDisable()
        {
            return playback == null || !playback.IsActive;
        }

        public override void OnEnable()
        {
            motionComponent.landThisFrame = false;
            AirMoveExecutor configuration = spineComponent.animationLib.airMoveExecutor;
            playback = spineComponent.TryPlay(configuration.LandSemantic, AnimationOwner.Landing, AnimationPriority.Landing, false);
        }

        public override void OnDisable()
        {
            playback = null;
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnDispose()
        {
            playback = null;
        }
    }
}
