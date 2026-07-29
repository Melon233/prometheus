using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class MotionLogic : Logic
    {
        private MotionComponent motionComp;


        public override void AfterNew()
        {
            LogicGroup = OrderTag.AfterGameplay;
            Entity.TryGetComp(out motionComp);
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override bool CanDisable()
        {
            return false;
        }

        public override void OnEnable()
        {
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
            motionComp.cc.Move(motionComp.baseSpeed * dt);
            motionComp.landThisFrame = motionComp.cc.isGrounded && !motionComp.wasGroundedLastFrame;
            motionComp.wasGroundedLastFrame = motionComp.cc.isGrounded;
        }

        public override void OnDispose()
        {
        }
    }
}