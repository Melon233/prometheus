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
            if (motionComp.cc.isGrounded && motionComp.curVelo.y > 0.1f) return true;
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
            motionComp.curVelo.y -= 9.8f * dt;
            if (motionComp.curVelo.y < 0f)
                trackEntry = airMoveExecutor.Execute(AirMoveState.Fall);
            if (inputComp.moveDir != Vector2.zero)
            {
                motionComp.curVelo = new Vector3(inputComp.moveDir.x * motionComp.propertyConfig.airMoveSpeed,
                                                    motionComp.curVelo.y,
                                                    inputComp.moveDir.y * motionComp.propertyConfig.airMoveSpeed);
            }
            else
            {
                motionComp.curVelo.x = 0f;
                motionComp.curVelo.z = 0f;
            }

        }
        public override void OnDispose()
        {
        }
    }
}