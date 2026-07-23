using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class GroundMoveExecutor : AnimationExecutor
    {
        [SerializeField] public AnimationReferenceAsset groundMoveAni;
        public TrackEntry Execute()
        {
            return spineComp.Play(groundMoveAni, true);
        }
    }
}