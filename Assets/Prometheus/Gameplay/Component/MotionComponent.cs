using System;
using UnityEngine;

namespace Xuan.Prometheus.Component
{
    public class MotionComponent : MonoComponent
    {
        public CharacterController cc;
        public float runVelo;
        public Vector3 baseSpeed;
        public Vector3 addSpeed;
        public float walkVelo = 3f;
        public float gravity = 9.8f;
        public bool wasGroundedLastFrame;
        public bool landThisFrame;
        public Transform rotateRoot;
        public float jumpVelo = 6f;
    }
}