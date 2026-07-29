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
        [SerializeField] public AnimationReferenceAsset walkAnimation;
        [SerializeField] public AnimationReferenceAsset runAnimaiton;
        [SerializeField] public AnimationReferenceAsset sprintAnimation;

        public TrackEntry Execute(MoveMode moveMode = MoveMode.Run)
        {
            switch (moveMode)
            {
                case MoveMode.Walk:
                    if (spineComp.IsPlaying(walkAnimation)) return null;
                    return spineComp.Play(walkAnimation, true);
                case MoveMode.Run:
                    if (spineComp.IsPlaying(runAnimaiton)) return null;
                    return spineComp.Play(runAnimaiton, true);
                case MoveMode.Sprint:
                    if (spineComp.IsPlaying(sprintAnimation)) return null;
                    return spineComp.Play(sprintAnimation, true);
                default:
                    return null;
            }
        }
    }
}