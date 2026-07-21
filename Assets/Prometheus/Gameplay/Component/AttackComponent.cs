using System.Collections.Generic;
using Spine;
using Spine.Unity;

namespace Xuan.Prometheus.Component
{
    public class AttackComponent : MonoComponent
    {
        public bool canCombo;
        // public List<AnimationReferenceAsset> comboAnimations;
        // public AnimationReferenceAsset ultAnimation;
        // public AnimationReferenceAsset branchStart;
        // public AnimationReferenceAsset branchAtk;
        public float elapsedComboTime;
        public int maxComboIndex = 3;
        public float maxComboInterval = 2f;
        public float minComboInterval = 0.5f;
        public int nextComboIndex;
        public ColliderProxy atkCollider;
        public TrackEntry curTrackEntry;
    }
}