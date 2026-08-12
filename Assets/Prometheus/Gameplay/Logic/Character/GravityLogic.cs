using System;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>统一负责玩家与敌人的竖直重力积分、地面吸附和真实落地边沿，不参与水平移动或动画播放。</summary>
    public sealed class GravityLogic : Logic
    {
        /// <summary>接地时保留的轻微向下速度，确保 CharacterController 每帧都能持续报告接地。</summary>
        private const float GroundedStickVelocity = -2f;

        /// <summary>保存当前实体唯一的运动运行态。</summary>
        private MotionComponent motionComponent;

        /// <summary>保存提供重力数值的属性组件。</summary>
        private PropertyComponent propertyComponent;

        /// <summary>保存可选的小队成员状态；敌人等非小队实体始终运行重力。</summary>
        private TeamMemberComponent teamMemberComponent;

        /// <summary>获取重力所需组件，并声明重力在水平玩法逻辑之后、最终位移提交之前运行。</summary>
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
            if (!Entity.TryGetComp(out motionComponent) || motionComponent == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' requires MotionComponent for gravity.");
            if (!Entity.TryGetComp(out propertyComponent) || propertyComponent == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' requires PropertyComponent for gravity.");
            if (motionComponent.cc == null) throw new InvalidOperationException($"Entity '{Entity.bindGo.name}' MotionComponent requires CharacterController.");
            Entity.TryGetComp(out teamMemberComponent);
        }

        /// <summary>敌人始终运行重力，玩家仅在当前上场时运行重力。</summary>
        public override bool CanEnable()
        {
            return teamMemberComponent == null || teamMemberComponent.IsOnField;
        }

        /// <summary>只有小队成员离场才暂停其场景物理，闪避、受击和其他控制状态不会停用重力。</summary>
        public override bool CanDisable()
        {
            return teamMemberComponent != null && !teamMemberComponent.IsOnField;
        }

        /// <summary>启用时沿用 MotionComponent 已保存或由 TeamSystem 迁移的接地历史。</summary>
        public override void OnEnable()
        {
        }

        /// <summary>离场时清除未消费的落地边沿，接地历史由 TeamSystem 在下一次切入时恢复。</summary>
        public override void OnDisable()
        {
            motionComponent.landThisFrame = false;
        }

        /// <summary>先根据上次位移结果生成真实落地边沿，再为空中实体累计重力或为接地实体保持向下吸附。</summary>
        public override void OnUpdate(float dt)
        {
            bool isGrounded = motionComponent.cc.isGrounded;
            motionComponent.landThisFrame = isGrounded && !motionComponent.wasGroundedLastFrame;
            motionComponent.wasGroundedLastFrame = isGrounded;
            if (isGrounded && motionComponent.curVelo.y <= 0f)
            {
                motionComponent.curVelo.y = GroundedStickVelocity;
                return;
            }
            if (dt > 0f) motionComponent.curVelo.y -= Mathf.Max(0f, propertyComponent.Gravity) * dt;
        }

        /// <summary>回收时清除组件引用，运行态数据由 Entity 统一释放。</summary>
        public override void OnDispose()
        {
            motionComponent = null;
            propertyComponent = null;
            teamMemberComponent = null;
        }
    }
}
