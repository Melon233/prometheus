using System;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus
{
    [Serializable]
    public class DieExecutor : AnimationExecutor
    {
        [SerializeField] public AnimationReferenceAsset dieAnimation;
        public TrackEntry Execute()
        {
            return spineComp.Play(dieAnimation, mixDuration: 0f);
        }
    }
}