using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public enum AirMoveState
    {
        Jump,
        Fall,
        Land
    }
    [Serializable]
    public class AirMoveExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset jumpAni;
        [SerializeField] AnimationReferenceAsset riseAni;
        [SerializeField] AnimationReferenceAsset fallAni;
        [SerializeField] AnimationReferenceAsset landAni;
        public TrackEntry Execute(AirMoveState state)
        {
            switch (state)
            {
                case AirMoveState.Land:
                    return spineComp.Play(landAni, track: 1);
                case AirMoveState.Jump:
                    spineComp.Play(jumpAni);
                    return spineComp.Add(riseAni, true);
                case AirMoveState.Fall:
                    if (spineComp.IsPlaying(AnimationName.jump_atk_loop)) return null;
                    return spineComp.Play(fallAni, true);
                default:
                    return null;
            }
        }
    }
}