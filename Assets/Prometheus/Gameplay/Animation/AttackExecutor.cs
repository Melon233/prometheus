using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    [Serializable]
    public class AttackExecutor : AnimationExecutor
    {
        [SerializeField] public List<AnimationReferenceAsset> atkAnis;
        [SerializeField] public List<AnimationReferenceAsset> atkMoveAnis;
        [SerializeField] bool hasVfx;
        [SerializeField][ShowIf("hasVfx")] List<YefaVfx> atkVfx;
        [SerializeField] List<AudioClip> atkSfx;
        AttackComponent atkComp;
        public override void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
            spineComp.Entity.TryGetComp(out atkComp);
        }
        public TrackEntry Execute(int index = 0, bool move = false)
        {
            var animations = move ? atkMoveAnis : atkAnis;

            if (index < 0 || index >= animations.Count)
                return null;
            var trackEntry = spineComp.Play(animations[index], mixDuration: 0f);
            trackEntry.Event += (entry, evt) =>
            {
                if (evt.ToString() == lib.hitStart)
                {
                    atkComp.atkCollider.cod.enabled = true;
                    if (hasVfx) vfxComp.Play(atkVfx[index]);
                    Debug.Assert(atkSfx[index] != null, "攻击音效不存在");
                    AudioKit.Instance.Play(atkSfx[index]);
                }
                else if (evt.ToString() == lib.hitEnd) atkComp.atkCollider.cod.enabled = false;
            };
            trackEntry.Interrupt += (entry) => atkComp.atkCollider.cod.enabled = false;
            trackEntry.Complete += (entry) => lib.idleExecutor.Execute(); // Reset track entry after completion
            return trackEntry;
        }
    }
}