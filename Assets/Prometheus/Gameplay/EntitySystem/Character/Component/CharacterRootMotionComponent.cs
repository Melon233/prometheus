using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>把 AnimationLine 最终播放出的 Spine Root Motion 转交给 MotionComponent，避免直接写 Transform 与 CharacterController 内部位置发生冲突。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkeletonAnimation))]
    [RequireComponent(typeof(MotionComponent))]
    public sealed class CharacterRootMotionComponent : SkeletonRootMotion
    {
        /// <summary>缓存角色唯一的运动状态组件，所有动画位移都由 MotionLogic 在统一出口提交。</summary>
        private MotionComponent motionComponent;

        /// <summary>完成 Spine 根骨骼初始化并绑定同物体上的运动组件。</summary>
        protected override void Start()
        {
            motionComponent = GetComponent<MotionComponent>();
            base.Start();
        }

        /// <summary>将 Spine 已完成缩放和轴向配置的 Root Motion 转成世界空间后累计，不在动画回调中直接修改 Transform。</summary>
        protected override void ApplyRootMotion(Vector2 skeletonDelta, Vector2 parentBoneScale)
        {
            motionComponent.AddRootMotionDelta(transform.TransformVector(skeletonDelta));
            ClearEffectiveBoneOffsets(parentBoneScale);
        }

        /// <summary>组件停用时同时丢弃尚未提交的动画位移，防止重新启用后补交过期移动。</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
            if (motionComponent != null) motionComponent.ClearRootMotionDelta();
        }
    }
}
