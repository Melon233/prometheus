using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class IdleLogic : Logic.Logic
    {
        InputComponent inputComp;
        SpineComponent spineComp;
        MotionComponent motionComp;
        public override void AfterNew()
        {
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out motionComp);
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override bool CanEnable()
        {
            return motionComp.cc.isGrounded && inputComp.moveDir == Vector2.zero;
        }

        public override void OnDisable()
        {

        }

        public override void OnDispose()
        {

        }

        public override void OnEnable()
        {
            motionComp.curVelo.x = 0f;
            motionComp.curVelo.z = 0f;
        }

        public override void OnUpdate(float dt)
        {
            if (spineComp.IsEmpty())
                spineComp.animationLib.idleExecutor.Execute();
        }
    }
}