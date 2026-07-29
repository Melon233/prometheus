using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    [Serializable]
    public class SpecialAttackExecutor : AnimationExecutor
    {
        [SerializeField] AnimationReferenceAsset specialAttackAni;
        [SerializeField] AudioClip specialAttackAudio;
        [SerializeField] YefaVfx specialAttackVfx;
        SpecialAttackComponent specialAttackComp;
        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
            spineComp.Entity.TryGetComp(out specialAttackComp);
        }
        public TrackEntry Execute()
        {
            var entry = spineComp.Play(specialAttackAni);
            entry.Event += (entry, evt) =>
            {
                if (evt.Data.Name == lib.hitStart)
                {
                    vfxComp.Play(specialAttackVfx);
                    AudioKit.Instance.Play(specialAttackAudio);
                    specialAttackComp.colliderProxy.cod.enabled = true;
                }
                else if (evt.Data.Name == lib.hitEnd)
                    specialAttackComp.colliderProxy.cod.enabled = false;
            };
            return entry;
        }
    }
}