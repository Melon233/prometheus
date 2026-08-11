using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>负责空中速度与下落循环动画；落地时主动释放空中动画所有权。</summary>
    public sealed class AirMoveLogic : Logic
    {
        private SpineComponent spineComponent;
        private InputComponent inputComponent;
        private MotionComponent motionComponent;
        private PropertyComponent propertyComponent;
        /// <summary>保存可选的小队成员状态，使离场玩家停止重力与空中动画而不影响敌人。</summary>
        private TeamMemberComponent teamMemberComponent;

        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out motionComponent);
            Entity.TryGetComp(out propertyComponent);
            Entity.TryGetComp(out teamMemberComponent);
        }

        public override bool CanEnable()
        {
            return (teamMemberComponent == null || teamMemberComponent.IsOnField) && (!motionComponent.cc.isGrounded || motionComponent.curVelo.y > 0.1f);
        }

        public override bool CanDisable()
        {
            return teamMemberComponent != null && !teamMemberComponent.IsOnField || motionComponent.cc.isGrounded;
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<NormalAttackLogic>();
            Entity.BlockLogic<SpecialAttackLogic>();
            Entity.BlockLogic<SkillLogic>();
            Entity.BlockLogic<UltimateLogic>();
            Entity.BlockLogic<DodgeLogic>();
        }

        public override void OnDisable()
        {
            spineComponent.Stop(AnimationOwner.AirMove);
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<NormalAttackLogic>();
            Entity.UnBlockLogic<SpecialAttackLogic>();
            Entity.UnBlockLogic<SkillLogic>();
            Entity.UnBlockLogic<UltimateLogic>();
            Entity.UnBlockLogic<DodgeLogic>();
        }

        public override void OnUpdate(float dt)
        {
            if (teamMemberComponent != null && !teamMemberComponent.IsOnField) return;
            motionComponent.curVelo.y -= propertyComponent.Gravity * dt;
            if (propertyComponent.CanAct && motionComponent.curVelo.y < 0f)
            {
                AirMoveExecutor configuration = spineComponent.animationLib.airMoveExecutor;
                spineComponent.TryPlay(configuration.FallSemantic, AnimationOwner.AirMove, AnimationPriority.Airborne, true);
            }
            if (propertyComponent.CanMove && inputComponent.moveDir != Vector2.zero)
            {
                motionComponent.curVelo = new Vector3(inputComponent.moveDir.x * propertyComponent.AirMoveSpeed, motionComponent.curVelo.y, inputComponent.moveDir.y * propertyComponent.AirMoveSpeed);
            }
            else
            {
                motionComponent.curVelo.x = 0f;
                motionComponent.curVelo.z = 0f;
            }
        }

        public override void OnDispose()
        {
            teamMemberComponent = null;
        }
    }
}
