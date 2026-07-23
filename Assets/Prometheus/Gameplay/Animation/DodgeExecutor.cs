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
        [SerializeField] AnimationReferenceAsset dodgeAni;
        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
        }
        public TrackEntry Execute()
        {
            return spineComp.Play(dodgeAni);
        }
    }
}