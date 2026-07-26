using System;
using Sirenix.OdinInspector;
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
        [SerializeField] bool hasNextAni;
        [SerializeField][ShowIf("hasNextAni")] AnimationReferenceAsset nextAttackedAni;
        public TrackEntry Execute()
        {
            AudioKit.Instance.Play(attackedSfx);
            return hasNextAni ? spineComp.Play(attackedAni, canRefresh: true, mixDuration: 0f, nextAni: nextAttackedAni) : spineComp.Play(attackedAni, canRefresh: true, mixDuration: 0f);
        }
    }
}