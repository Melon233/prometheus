using System;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus
{
    [Serializable]
    public class AttackedExecutor : AnimationExecutor
    {
        [SerializeField] public AnimationReferenceAsset attackedAni;
        [SerializeField] AudioClip attackedSfx;
        public TrackEntry Execute()
        {
            AudioKit.Instance.Play(attackedSfx);
            return spineComp.Play(attackedAni, canRefresh: true, mixDuration: 0f);
        }
    }
}