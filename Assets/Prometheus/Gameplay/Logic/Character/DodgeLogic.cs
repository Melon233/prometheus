using Spine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class DodgeData : Component.Component
    {
    }

    public class DodgeLogic : Logic
    {
        private SpineComponent aniComp;
        private TrackEntry entry;
        private InputComponent inputComp;
        private SpineComponent spineComp;
        DodgeComponent dodgeComp;
        public override void AfterNew()
        {
            LogicGroup = OrderTag.Controller;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out aniComp);
            Entity.TryGetComp(out spineComp);
            Entity.TryGetComp(out dodgeComp);
        }

        public override bool CanEnable()
        {
            return inputComp.wasDodgePressedThisFrame && !dodgeComp.isDodging;
        }

        public override bool CanDisable()
        {
            return !dodgeComp.isDodging;
        }

        public override void OnEnable()
        {
            dodgeComp.isDodging = true;
            Entity.BlockLogic<RotateLogic>();
            Entity.BlockLogic<GroundMoveLogic>();
            Entity.BlockLogic<MotionLogic>();
            Entity.BlockLogic<AirMoveLogic>();
            entry = spineComp.animationLib.dodgeExecutor.Execute(inputComp.moveDir.x != 0f);
            entry.Event += (entry, e) =>
            {
                if (e.Data.Name == spineComp.animationLib.hitEnd)
                {
                    dodgeComp.isDodging = false;
                }
            };
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