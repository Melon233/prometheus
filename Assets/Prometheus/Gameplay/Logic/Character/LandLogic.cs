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
            OrderTag = OrderTag.Buff;
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
            return true;
        }

        public override void OnEnable()
        {
            var entry = airMoveExecutor.Execute(AirMoveState.Land);
            entry.Event += (entry, e) =>
            {
                if (e.Data.Name == spineComp.animationLib.hitEnd)
                {
                    motionComp.landThisFrame = false;
                }
            };
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