using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class DodgeExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset dodgeFrontAnimation;
        [SerializeField] AnimationReferenceAsset dodgeBackAnimation;

        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
        }
        public TrackEntry Execute(bool isMoving)
        {
            if (isMoving)
                return spineComp.Play(dodgeFrontAnimation);
            else
                return spineComp.Play(dodgeBackAnimation);
        }
    }
}