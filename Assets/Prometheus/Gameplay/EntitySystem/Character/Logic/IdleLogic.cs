using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    /// <summary>在角色落地且没有移动输入时持续请求最低优先级待机动画，实际播放权由 SpineComponent 仲裁。</summary>
    public sealed class IdleLogic : Logic.Logic
    {
        private InputComponent inputComponent;
        private SpineComponent spineComponent;
        private MotionComponent motionComponent;

        /// <summary>缓存待机条件所需组件，Logic 不保存任何 Spine TrackEntry。</summary>
        public override void AfterNew()
        {
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out motionComponent);
        }

        public override bool CanEnable()
        {
            return motionComponent.cc.isGrounded && inputComponent.moveDir == Vector2.zero;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
            motionComponent.curVelo.x = 0f;
            motionComponent.curVelo.z = 0f;
        }

        /// <summary>只释放 IdleLogic 自己的会话；移动或其他高优先级动画已经抢占时不会误停新动画。</summary>
        public override void OnDisable()
        {
            spineComponent.Stop(AnimationOwner.Idle);
        }

        /// <summary>每帧请求待机使落地动画完成后可以立即接回待机，同时低优先级请求无法打断落地或移动。</summary>
        public override void OnUpdate(float dt)
        {
            spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true);
        }

        public override void OnDispose()
        {
        }
    }
}
