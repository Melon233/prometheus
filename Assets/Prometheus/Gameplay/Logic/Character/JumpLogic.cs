using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class JumpLogic : Logic
    {
        private SpineComponent spineComp;
        private InputComponent inputComp;
        private MotionComponent motionComp;
        /// <summary>
        /// 提供经过 modifier 计算后的跳跃速度。
        /// </summary>
        private PropertyComponent propComp;
        AirMoveExecutor airMoveExecutor;
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.Move;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
            Entity.TryGetComp(out propComp);
            airMoveExecutor = spineComp.animationLib.airMoveExecutor;
        }

        public override bool CanEnable()
        {
            return motionComp.cc.isGrounded && inputComp.wasJumpPressedThisFrame;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<TalentLogic>();
            Entity.BlockLogic<DodgeLogic>();
            airMoveExecutor.Execute(AirMoveState.Jump);
            motionComp.curVelo.y = propComp.JumpSpeed;
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<TalentLogic>();
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
