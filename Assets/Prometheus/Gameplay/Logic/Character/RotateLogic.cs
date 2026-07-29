using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Logic
{
    public class RotateLogic : Logic
    {
        InputComponent inputComp;
        SpineComponent spineComp;
        public override void AfterNew()
        {
            LogicGroup = OrderTag.Gameplay;
            Entity.TryGetComp(out inputComp);
            Entity.TryGetComp(out spineComp);
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
            spineComp.SetFaceDir(inputComp.moveDir);
        }


        public override void OnDispose()
        {
        }
    }
}