using System;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>负责敌人的竖直空中速度与地面吸附；水平速度由 EnemyAiLogic 写入，最终位移由 MotionLogic 统一提交。</summary>
    public sealed class EnemyAirMoveLogic : Logic
    {
        private const float GroundedStickVelocity = -2f;
        private MotionComponent motionComponent;
        private PropertyComponent propertyComponent;

        /// <summary>获取敌人物理所需的运动与属性组件，并声明该基础设施逻辑不受眩晕、禁锢或死亡表现暂停。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out motionComponent) || motionComponent == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' requires MotionComponent for airborne movement.");
            if (!Entity.TryGetComp(out propertyComponent) || propertyComponent == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' requires PropertyComponent for gravity.");
            if (motionComponent.cc == null) throw new InvalidOperationException($"Enemy '{Entity.bindGo.name}' MotionComponent requires CharacterController.");
        }

        /// <summary>敌人物理在整个实体生命周期内持续运行。</summary>
        public override bool CanEnable()
        {
            return true;
        }

        /// <summary>重力不会因 AI、受击或控制状态而自动停用。</summary>
        public override bool CanDisable()
        {
            return false;
        }

        /// <summary>启用时无需额外状态切换。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>基础物理逻辑不会通过普通调度路径禁用。</summary>
        public override void OnDisable()
        {
        }

        /// <summary>空中累计向下重力，接地时保留轻微向下速度以稳定 CharacterController 的接地判定。</summary>
        public override void OnUpdate(float dt)
        {
            if (dt <= 0f) return;
            if (motionComponent.cc.isGrounded && motionComponent.curVelo.y <= 0f)
            {
                motionComponent.curVelo.y = GroundedStickVelocity;
                return;
            }
            motionComponent.curVelo.y -= Mathf.Max(0f, propertyComponent.Gravity) * dt;
        }

        /// <summary>回收时清除组件引用，实体组件数据由 Entity 生命周期统一释放。</summary>
        public override void OnDispose()
        {
            motionComponent = null;
            propertyComponent = null;
        }
    }
}
