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
        /// <summary>
        /// 提供经过 modifier 计算后的空中移动速度和重力。
        /// </summary>
        private PropertyComponent propComp;
        private TrackEntry trackEntry;
        AirMoveExecutor airMoveExecutor;
        public override void AfterNew()
        {
            OrderTag = OrderTag.Gameplay;
            ControlRequirement = LogicControlRequirement.None;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
            Entity.TryGetComp(out propComp);
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
            motionComp.curVelo.y -= propComp.Gravity * dt;
            if (propComp.CanAct && motionComp.curVelo.y < 0f) trackEntry = airMoveExecutor.Execute(AirMoveState.Fall);
            if (propComp.CanMove && inputComp.moveDir != Vector2.zero)
            {
                motionComp.curVelo = new Vector3(inputComp.moveDir.x * propComp.AirMoveSpeed, motionComp.curVelo.y, inputComp.moveDir.y * propComp.AirMoveSpeed);
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
