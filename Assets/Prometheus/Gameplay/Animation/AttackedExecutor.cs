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
            AudioKit.Ins.Play(attackedSfx);
            if (hasNextAni)
            {
                spineComp.Play(attackedAni);
                var entry = spineComp.Play(nextAttackedAni);
                entry.Complete += (entry) => lib.idleExecutor.Execute();
                return entry;
            }
            else
            {
                var entry = spineComp.Play(attackedAni);
                entry.Complete += (entry) => lib.idleExecutor.Execute();
                return entry;
            }
        }
    }
}