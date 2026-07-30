using System;
using System.Collections.Generic;
using Spine;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public enum MoveMode
    {
        Walk,
        Run,
        Sprint
    }
    public class MotionComponent : MonoComponent
    {
        public CharacterController cc;
        public Vector3 curVelo;
        public bool wasGroundedLastFrame;
        public bool landThisFrame;
        public MoveMode moveMode = MoveMode.Run;
        public float curMoveSpeed;
        public PropertyConfig propertyConfig;
        public TrackEntry entry;
    }
}