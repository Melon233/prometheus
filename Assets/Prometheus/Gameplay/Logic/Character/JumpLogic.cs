using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    /// <summary>负责消费跳跃输入、施加竖直速度并请求起跳到上升循环的 AnimationLine 序列。</summary>
    public sealed class JumpLogic : Logic
    {
        private SpineComponent spineComponent;
        private InputComponent inputComponent;
        private MotionComponent motionComponent;
        private PropertyComponent propertyComponent;

        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out spineComponent);
            Entity.TryGetComp(out inputComponent);
            Entity.TryGetComp(out motionComponent);
            Entity.TryGetComp(out propertyComponent);
        }

        public override bool CanEnable()
        {
            return motionComponent.cc.isGrounded && inputComponent.wasJumpPressedThisFrame;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<NormalAttackLogic>();
            Entity.BlockLogic<SpecialAttackLogic>();
            Entity.BlockLogic<SkillLogic>();
            Entity.BlockLogic<UltimateLogic>();
            Entity.BlockLogic<DodgeLogic>();
            AirMoveExecutor configuration = spineComponent.animationLib.airMoveExecutor;
            spineComponent.TryPlaySequence(configuration.JumpSemantic, configuration.RiseSemantic, AnimationOwner.AirMove, AnimationPriority.Airborne, true);
            motionComponent.curVelo.y = propertyComponent.JumpSpeed;
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<NormalAttackLogic>();
            Entity.UnBlockLogic<SpecialAttackLogic>();
            Entity.UnBlockLogic<SkillLogic>();
            Entity.UnBlockLogic<UltimateLogic>();
            Entity.UnBlockLogic<DodgeLogic>();
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnDispose()
        {
        }
    }
}
