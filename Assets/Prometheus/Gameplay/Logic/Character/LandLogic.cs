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
            LogicGroup = LogicGroup.Buff;
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
            airMoveExecutor = spineComp.charaAniLib.airMoveExecutor;
        }

        public override bool CanEnable()
        {
            return motionComp.landThisFrame && inputComp.moveDir == Vector2.zero;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
            airMoveExecutor.Execute(spineComp, AirMoveState.Land);
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
        }


        public override void OnDispose()
        {
        }
    }
}