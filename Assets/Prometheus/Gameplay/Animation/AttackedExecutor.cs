using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class AttackedExecutor : AnimationExecutor
    {
        [SerializeField] public AnimationReferenceAsset attackedAni;
        public TrackEntry Execute()
        {
            return spineComp.Play(attackedAni, canRefresh: true, mixDuration: 0f);
        }
    }
}