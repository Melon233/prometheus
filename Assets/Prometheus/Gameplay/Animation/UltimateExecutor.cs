using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    [Serializable]
    public class UltimateExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset ultimateAni;
        [SerializeField] AudioClip ultimateAudio;
        [SerializeField] YefaVfx ultVfx;
        UltimateComponent ultimateComp;
        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
            spineComp.Entity.TryGetComp(out ultimateComp);
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
                    ultimateComp.colliderProxy.cod.enabled = true; // Reset collider proxy after ultimate execution
                }
                else if (evt.Data.Name == lib.hitEnd)
                    ultimateComp.colliderProxy.cod.enabled = false; // Reset collider proxy after ultimate execution
            };
            return entry;
        }
    }
}