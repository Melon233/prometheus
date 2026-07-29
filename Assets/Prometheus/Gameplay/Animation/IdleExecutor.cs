using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Serialization;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class IdleExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset idleAnimation;
        public TrackEntry Execute()
        {
            if (spineComp.IsPlaying(AnimationName.idle1_1)) return null;
            return spineComp.Play(idleAnimation, true, 0);
        }
    }
}
