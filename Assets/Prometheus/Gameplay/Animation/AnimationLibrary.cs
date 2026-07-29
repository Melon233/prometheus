using System;
using System.Linq;
using System.Reflection;
using Sirenix.OdinInspector;
using Spine;
using Spine.Unity;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus
{
    public interface IExecutor
    {
        public void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp);
    }
    public abstract class AnimationExecutor : IExecutor
    {
        protected AnimationLibrary lib;
        protected SpineComponent spineComp;
        protected VfxComponent vfxComp;

        public virtual void Init(AnimationLibrary lib, SpineComponent spineComp, VfxComponent vfxComp)
        {
            this.lib = lib;
            this.spineComp = spineComp;
            this.vfxComp = vfxComp;
        }
    }
    [CreateAssetMenu(menuName = "Spiner/AnimationLibrary")]
    public class AnimationLibrary : ScriptableObject
    {
        public SkeletonDataAsset skeletonDataAsset;
        [SpineEvent(dataField = "skeletonDataAsset")] public string hitStart;
        [SpineEvent(dataField = "skeletonDataAsset")] public string hitEnd;
        public AttackExecutor atkExecutor;
        public IdleExecutor idleExecutor;
        public GroundMoveExecutor groundMoveExecutor;
        public DodgeExecutor dodgeExecutor;
        public AirMoveExecutor airMoveExecutor;
        public bool hasTalent;
        [ShowIf("hasTalent")] public UltimateExecutor ultimateExecutor;
        [ShowIf("hasTalent")] public SkillExecutor skillExecutor;
        [ShowIf("hasTalent")] public SpecialAttackExecutor specialAttackExecutor;
        public AttackedExecutor attackedExecutor;
        public DieExecutor dieExecutor;
        public void Init(SpineComponent spineComp, VfxComponent vfxComp)
        {
            atkExecutor.Init(this, spineComp, vfxComp);
            idleExecutor.Init(this, spineComp, vfxComp);
            groundMoveExecutor.Init(this, spineComp, vfxComp);
            airMoveExecutor.Init(this, spineComp, vfxComp);
            if (hasTalent)
            {
                ultimateExecutor.Init(this, spineComp, vfxComp);
                skillExecutor.Init(this, spineComp, vfxComp);
                specialAttackExecutor.Init(this, spineComp, vfxComp);
            }
            dodgeExecutor.Init(this, spineComp, vfxComp);
            attackedExecutor.Init(this, spineComp, vfxComp);
            dieExecutor.Init(this, spineComp, vfxComp);
        }
    }
}