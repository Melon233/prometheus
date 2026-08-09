using System;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>在敌人持有 Stun 控制状态时停止水平运动并持续请求 Idle，使受击动画结束后能够自动回到待机表现。</summary>
    public sealed class EnemyStunIdleLogic : Logic
    {
        private PropertyComponent propertyComponent;
        private MotionComponent motionComponent;
        private SpineComponent spineComponent;

        /// <summary>获取控制状态、运动数据与动画仲裁组件；本 Logic 不受 Stun 自身的行动限制。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out propertyComponent) || propertyComponent == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' requires PropertyComponent for stun presentation.");
            if (!Entity.TryGetComp(out motionComponent) || motionComponent == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' requires MotionComponent for stun presentation.");
            if (!Entity.TryGetComp(out spineComponent) || spineComponent == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' requires SpineComponent for stun presentation.");
        }

        /// <summary>仅在未死亡且至少持有一个 Stun 状态贡献时启用。</summary>
        public override bool CanEnable()
        {
            return !propertyComponent.IsDead && propertyComponent.HasAnyControlState(ControlState.Stun);
        }

        /// <summary>Stun 完全移除或敌人死亡时退出待机持有状态。</summary>
        public override bool CanDisable()
        {
            return !CanEnable();
        }

        /// <summary>进入 Stun 时立即停止水平运动并尝试播放 Idle；高优先级受击动画仍可拒绝本次请求。</summary>
        public override void OnEnable()
        {
            StopHorizontalMotion();
            RequestIdle();
        }

        /// <summary>退出 Stun 时保留当前 Idle，由恢复运行的 AI 根据下一状态自然替换。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>Stun 存续期间持续停止水平运动并重试 Idle，使受击会话释放优先级后立即完成接管。</summary>
        public override void OnUpdate(float dt)
        {
            StopHorizontalMotion();
            RequestIdle();
        }

        /// <summary>回收时清除组件引用，不主动改变已经由死亡动画接管的主轨。</summary>
        public override void OnDispose()
        {
            propertyComponent = null;
            motionComponent = null;
            spineComponent = null;
        }

        /// <summary>清除 AI 水平速度并保留 EnemyAirMoveLogic 管理的竖直重力速度。</summary>
        private void StopHorizontalMotion()
        {
            motionComponent.curVelo.x = 0f;
            motionComponent.curVelo.z = 0f;
        }

        /// <summary>通过统一动画仲裁器请求最低优先级待机循环，避免覆盖受击、死亡或其他高优先级表现。</summary>
        private void RequestIdle()
        {
            spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true);
        }
    }
}
