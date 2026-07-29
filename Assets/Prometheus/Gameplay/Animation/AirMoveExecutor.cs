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
        [SerializeField] public AnimationReferenceAsset landAni;
        public TrackEntry Execute(AirMoveState state)
        {
            switch (state)
            {
                case AirMoveState.Land:
                    return spineComp.Play(landAni);
                case AirMoveState.Jump:
                    spineComp.Play(jumpAni);
                    return spineComp.Add(riseAni, true);
                case AirMoveState.Fall:
                    if (spineComp.IsPlaying(fallAni)) return null;
                    return spineComp.Play(fallAni, true);
                default:
                    return null;
            }
        }
    }
}