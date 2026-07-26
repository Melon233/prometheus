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
            var trackEntry = spineComp.Play(animations[index], mixDuration: 0f, onEvent: (_, spineEvent) => OnEvent(index, spineEvent));
            trackEntry.Event += (entry, evt) =>
            {
                if (evt.ToString() == lib.hitStart)
                    atkComp.atkCollider.cod.enabled = true;
                else if (evt.ToString() == lib.hitEnd) atkComp.atkCollider.cod.enabled = false;
            };
            trackEntry.Complete += (entry) => atkComp.atkCollider.cod.enabled = false;
            return trackEntry;
        }
        private void OnEvent(int index, Spine.Event spineEvent)
        {
            if (spineEvent.Data.Name != lib.hitStart)
                return;

            if (index < 0 || index >= atkVfx.Count || index >= atkSfx.Count)
            {
                Debug.LogWarning($"Attack presentation is missing at index {index}.");
                return;
            }

            if (hasVfx) vfxComp.Play(atkVfx[index]);
            var sfx = atkSfx[index];
            if (sfx != null)
                AudioKit.Instance.Play(sfx);
        }
    }
}