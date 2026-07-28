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
        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
        }

        public TrackEntry Execute()
        {
            var entry = spineComp.Play(ultimateAni);
            entry.Event += (entry, evt) =>
            {
                if (evt.Data.Name == lib.hitStart)
                {
                    vfxComp.Play(ultVfx);
                    AudioKit.Instance.Play(ultimateAudio);
                }
            };
            return entry;
        }
    }
}