using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class IdleExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset idleAni;
        public TrackEntry Execute()
        {
            return spineComp.Play(idleAni, true);
        }
    }
}