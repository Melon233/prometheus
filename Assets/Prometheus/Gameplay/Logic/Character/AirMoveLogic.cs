using Spine;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class AirMoveLogic : Logic
    {
        private SpineComponent spineComp;
        private InputComponent inputComp;
        private MotionComponent motionComp;
        private TrackEntry trackEntry;
        AirMoveExecutor airMoveExecutor;
        public override void AfterNew()
        {
            LogicGroup = OrderTag.Gameplay;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
            airMoveExecutor = spineComp.animationLib.airMoveExecutor;
        }

        public override bool CanEnable()
        {
            if (!motionComp.cc.isGrounded) return true;
            if (motionComp.cc.isGrounded && motionComp.baseSpeed.y > 0.1f) return true;
            return false;
        }

        public override bool CanDisable()
        {
            return motionComp.cc.isGrounded;
        }

        public override void OnEnable()
        {
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<TalentLogic>();
            Entity.BlockLogic<DodgeLogic>();
        }

        public override void OnDisable()
        {
            Entity.UnBlockLogic<GroundMoveLogic>();
            Entity.UnBlockLogic<TalentLogic>();
            Entity.UnBlockLogic<DodgeLogic>();
        }

        public override void OnUpdate(float dt)
        {
            motionComp.baseSpeed.y -= 9.8f * dt;
            if (motionComp.baseSpeed.y < 0f)
                trackEntry = airMoveExecutor.Execute(AirMoveState.Fall);
            if (inputComp.moveDir != Vector2.zero)
            {
                motionComp.baseSpeed = new Vector3(inputComp.moveDir.x * motionComp.walkVelo,
                                                    motionComp.baseSpeed.y,
                                                    inputComp.moveDir.y * motionComp.walkVelo);
            }
            else
            {
                motionComp.baseSpeed.x = 0f;
                motionComp.baseSpeed.z = 0f;
            }

        }
        public override void OnDispose()
        {
        }
    }
}