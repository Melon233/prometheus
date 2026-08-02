using System;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    // [Serializable]
    // public class AnimationBinding
    // {
    //     [LabelText("动画")]
    //     [TableColumnWidth(180, true)]
    //     public AnimationReferenceAsset ani;
    //     [LabelText("特效")]
    //     [TableColumnWidth(180, true)]
    //     public VfxExecutor vfxExecutor;
    //     [LabelText("音效")]
    //     [TableColumnWidth(180, true)]
    //     public AudioClip audio;
    // }
    [Serializable]
    public class SkillExecutor : AnimationExecutor
    {
        // [TableList(AlwaysExpanded = true, ShowIndexLabels = true, DrawScrollView = true)]
        // public List<AnimationBinding> binds;
        [SerializeField] AnimationReferenceAsset skillStartAni;
        [SerializeField] AnimationReferenceAsset skillAni;
        [SerializeField] AudioClip skillAudio;
        [SerializeField] YefaVfx skillVfx;
        SkillComponent skillComp;
        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
            spineComp.Entity.TryGetComp(out skillComp);
        }
        public TrackEntry Execute()
        {
            spineComp.Play(skillStartAni);
            var entry = spineComp.Add(skillAni);
            entry.Event += (entry, evt) =>
            {
                if (evt.Data.Name == lib.hitStart)
                {
                    vfxComp.Play(skillVfx);
                    AudioKit.Instance.Play(skillAudio);
                    skillComp.colliderProxy.cod.enabled = true;
                }
                else if (evt.Data.Name == lib.hitEnd)
                    skillComp.colliderProxy.cod.enabled = false;
            };
            return entry;
        }
    }
}