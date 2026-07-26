using Spine;
using Xuan.Prometheus.Component;
using Animation = Xuan.Prometheus.Component.Animation;

namespace Xuan.Prometheus.Logic
{
    public class DodgeData : Component.Component
    {
    }

    public class DodgeLogic : Logic
    {
        private SpineComponent aniComp;
        private TrackEntry dodgeAni;
        private InputComponent inputComp;
        private SpineComponent motionComp;

        public override void AfterNew()
        {
            LogicGroup = LogicGroup.Controller;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out aniComp);
            Entity.TryGetComp(out motionComp);
        }

        public override bool CanEnable()
        {
            return inputComp.wasDodgePressedThisFrame;
        }

        public override bool CanDisable()
        {
            return dodgeAni.Animation == null || dodgeAni.NormalizedTime() >= 0.99f;
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<RotateLogic>();
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<MotionLogic>();
            Entity.BlockLogic<AirMoveLogic>();
            dodgeAni = aniComp.Play(Animation.dodge_front_move);
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<RotateLogic>();
            Entity.UnBlockLogic<MotionLogic>();
            Entity.UnBlockLogic<AirMoveLogic>();
        }

        public override void OnUpdate(float dt)
        {
        }

        public override void OnDispose()
        {
        }
    }
}