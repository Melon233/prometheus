using System;
using System.Collections.Generic;
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
            if (state == AirMoveState.Land)
                return spineComp.Play(landAni, track: 1);
            return state switch
            {
                AirMoveState.Jump => spineComp.Play(jumpAni, nextAni: riseAni, nextLoop: true),
                AirMoveState.Fall => spineComp.Play(fallAni),
                _ => null,
            };
        }
    }
}