using System;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    /// <summary>把 AnimationLine 最终播放出的 Spine Root Motion 作为 Unity 回调事件转交给 ELC Logic。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkeletonAnimation))]
    public sealed class CharacterRootMotionComponent : SkeletonRootMotion
    {
        /// <summary>当 Spine 产生一段世界空间 Root Motion 时通知当前 Entity 的 MotionLogic。</summary>
        public event Action<Vector3> RootMotionApplied;

        /// <summary>完成 Spine 根骨骼初始化；运行时订阅关系由 MotionLogic 在 AfterNew 中建立。</summary>
        protected override void Start()
        {
            base.Start();
        }

        /// <summary>将 Spine 已完成缩放和轴向配置的 Root Motion 转成世界空间后累计，不在动画回调中直接修改 Transform。</summary>
        protected override void ApplyRootMotion(Vector2 skeletonDelta, Vector2 parentBoneScale)
        {
            RootMotionApplied?.Invoke(transform.TransformVector(skeletonDelta));
            ClearEffectiveBoneOffsets(parentBoneScale);
        }

        /// <summary>组件停用时只执行 Spine 桥接器自身清理；MotionLogic 在禁用时清空未提交位移。</summary>
        protected override void OnDisable()
        {
            base.OnDisable();
        }
    }
}
