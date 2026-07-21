using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class RotateLogic : Logic
    {
        private SpineComponent aniComp;
        private InputComponent inputComp;
        MotionComponent motionComp;
        public override void AfterNew()
        {
            LogicGroup = LogicGroup.Gameplay;
            Entity.TryGetComp(out aniComp);
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out motionComp);
        }

        public override bool CanEnable()
        {
            return true;
        }

        public override bool CanDisable()
        {
            return !CanEnable();
        }

        public override void OnEnable()
        {
        }

        public override void OnDisable()
        {
        }

        public override void OnUpdate(float dt)
        {
            if (inputComp.moveDir.x > 0) { aniComp.CurFaceDir = FaceDir.Right; motionComp.rotateRoot.rotation = Quaternion.Euler(Vector3.zero); }
            else if (inputComp.moveDir.x < 0) { aniComp.CurFaceDir = FaceDir.Left; motionComp.rotateRoot.rotation = Quaternion.Euler(new Vector3(0f, 180f, 0f)); }
        }


        public override void OnDispose()
        {
        }
    }
}