using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;

namespace Xuan.Prometheus.Component
{
    public class AttackComponent : MonoComponent
    {
        [NonSerialized] public bool canCombo = true;
        public float elapsedComboTime;
        public int maxComboIndex = 3;
        public float maxComboInterval = 2f;
        public int nextComboIndex;
        public ColliderProxy atkCollider;
        public TrackEntry curTrackEntry;
        public bool isBlocking;
    }
}