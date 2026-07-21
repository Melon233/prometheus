using System;
using System.Collections.Generic;
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
        [SerializeField] List<YefaVfx> atkVfx;
        [SerializeField] List<AudioClip> atkSfx;

        public override void Init(CharacterAnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            base.Init(lib, spineComp, vfxComp);
        }
        public TrackEntry Execute(int index, bool move)
        {
            var animations = move ? atkMoveAnis : atkAnis;

            if (index < 0 || index >= animations.Count)
                return null;

            return spineComp.Play(animations[index], mixDuration: 0f, onEvent: (_, spineEvent) => OnEvent(index, spineEvent));
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

            vfxComp.Play(atkVfx[index]);

            var sfx = atkSfx[index];
            if (sfx != null)
                AudioKit.Instance.Play(sfx);
        }
    }
}