using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class SpecialAttackExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset specialAttackAni;
        public TrackEntry Execute()
        {
            return spineComp.Play(specialAttackAni);
        }
    }
}