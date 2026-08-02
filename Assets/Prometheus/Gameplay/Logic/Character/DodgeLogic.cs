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
            OrderTag = OrderTag.Controller;
            ControlRequirement = LogicControlRequirement.Move;
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

        /// <summary>结束闪避状态并释放阻塞；死亡回收时保留 DieLogic 已经切换到的死亡动画。</summary>
        public override void OnDisable()
        {
            bool entityIsDead = Entity.TryGetComp(out PropertyComponent propertyComponent) && propertyComponent.IsDead;
            if (!entityIsDead && entry != null && !entry.IsComplete) spineComp.Stop(0, 0f);
            entry = null;
            dodgeComp.isDodging = false;
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
