using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class LandLogic : Logic
    {
        private SpineComponent spineComp;
        private InputComponent inputComp;
        MotionComponent motionComp;
        AirMoveExecutor airMoveExecutor;

        public override void AfterNew()
        {
            LogicGroup = OrderTag.Buff;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
            airMoveExecutor = spineComp.animationLib.airMoveExecutor;
        }

        public override bool CanEnable()
        {
            return motionComp.landThisFrame && inputComp.moveDir == Vector2.zero;
        }

        public override bool CanDisable()
        {
            return !spineComp.IsPlaying(airMoveExecutor.landAni) || inputComp.hasInputThisFrame;
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            airMoveExecutor.Execute(AirMoveState.Land);
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
        }

        public override void OnUpdate(float dt)
        {
        }


        public override void OnDispose()
        {
        }
    }
}