using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class UltimateExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset ultimateAni;
        [SerializeField] AudioClip ultimateAudio;
        [SerializeField] YefaVfx ultVfx;
        public override void Init(CharacterAnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
        }

        public TrackEntry Execute(SpineComponent spine)
        {
            return spine.Play(ultimateAni, onEvent: OnEvent);
        }
        public void OnEvent(TrackEntry entry, Spine.Event e)
        {
            if (e.Data.Name == lib.hitStart)
            {
                vfxComp.Play(ultVfx);
                AudioKit.Instance.Play(ultimateAudio);
            }
            else if (e.Data.Name == lib.hitEnd)
            {
            }
        }
    }
}