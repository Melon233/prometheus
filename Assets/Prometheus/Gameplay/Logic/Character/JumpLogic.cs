using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class JumpLogic : Logic
    {
        private SpineComponent spineComp;
        private InputComponent inputComp;
        private MotionComponent motionComp;
        AirMoveExecutor airMoveExecutor;
        public override void AfterNew()
        {
            LogicGroup = LogicGroup.Gameplay;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
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
            motionComp.baseSpeed.y = motionComp.jumpVelo;
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