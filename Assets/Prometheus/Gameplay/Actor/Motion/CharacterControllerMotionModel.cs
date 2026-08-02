using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// 通过一次 CharacterController.Move 完成每个固定 Tick 的平面移动、重力、跳跃和行为附加位移。
    /// </summary>
    public sealed class CharacterControllerMotionModel : IActorMotionModel
    {
        private readonly Transform root;
        private readonly CharacterController characterController;
        private readonly float acceleration;
        private readonly float deceleration;
        private readonly float airAcceleration;
        private readonly float gravity;
        private readonly float jumpSpeed;
        private readonly float groundedVerticalSpeed;
        private readonly float maximumFallSpeed;
        private Vector3 velocity;
        private bool wasGrounded;
        private bool disposed;

        /// <summary>
        /// 创建一个独立的 CharacterController 运动运行时。
        /// </summary>
        public CharacterControllerMotionModel(ActorMotionContext context, float acceleration, float deceleration, float airAcceleration, float gravity, float jumpSpeed, float groundedVerticalSpeed, float maximumFallSpeed)
        {
            root = context.Root;
            characterController = context.CharacterController != null ? context.CharacterController : throw new ArgumentNullException(nameof(context.CharacterController));
            this.acceleration = Mathf.Max(0f, acceleration);
            this.deceleration = Mathf.Max(0f, deceleration);
            this.airAcceleration = Mathf.Max(0f, airAcceleration);
            this.gravity = gravity;
            this.jumpSpeed = Mathf.Max(0f, jumpSpeed);
            this.groundedVerticalSpeed = groundedVerticalSpeed;
            this.maximumFallSpeed = Mathf.Max(0f, maximumFallSpeed);
            wasGrounded = characterController.isGrounded;
            Snapshot = new ActorMotionSnapshot(root.position, Vector3.zero, wasGrounded, false);
        }

        /// <inheritdoc />
        public ActorMotionSnapshot Snapshot { get; private set; }

        /// <inheritdoc />
        public void Simulate(ActorMotionIntent intent, float fixedDeltaTime)
        {
            ThrowIfDisposed();
            if (fixedDeltaTime <= 0f) return;
            bool groundedBeforeMove = characterController.isGrounded;
            Vector3 direction = Vector3.ProjectOnPlane(intent.WorldDirection, Vector3.up);
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            Vector3 targetPlanarVelocity = direction * intent.Speed;
            Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
            float planarRate = groundedBeforeMove ? (targetPlanarVelocity.sqrMagnitude > 0.000001f ? acceleration : deceleration) : airAcceleration;
            planarVelocity = Vector3.MoveTowards(planarVelocity, targetPlanarVelocity, planarRate * fixedDeltaTime);
            velocity.x = planarVelocity.x;
            velocity.z = planarVelocity.z;
            if (groundedBeforeMove && velocity.y < 0f) velocity.y = groundedVerticalSpeed;
            if (groundedBeforeMove && intent.JumpRequested) velocity.y = jumpSpeed;
            else velocity.y = Mathf.Max(-maximumFallSpeed, velocity.y + gravity * fixedDeltaTime);
            Vector3 displacement = velocity * fixedDeltaTime + intent.AdditiveDisplacement;
            characterController.Move(displacement);
            bool groundedAfterMove = characterController.isGrounded;
            bool landedThisTick = !wasGrounded && groundedAfterMove;
            if (groundedAfterMove && velocity.y < 0f) velocity.y = groundedVerticalSpeed;
            wasGrounded = groundedAfterMove;
            Snapshot = new ActorMotionSnapshot(root.position, velocity, groundedAfterMove, landedThisTick);
        }

        /// <inheritdoc />
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            ThrowIfDisposed();
            bool wasEnabled = characterController.enabled;
            if (wasEnabled) characterController.enabled = false;
            root.SetPositionAndRotation(position, rotation);
            if (wasEnabled) characterController.enabled = true;
            velocity = Vector3.zero;
            wasGrounded = characterController.isGrounded;
            Snapshot = new ActorMotionSnapshot(position, Vector3.zero, wasGrounded, false);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            velocity = Vector3.zero;
        }

        /// <summary>阻止已经释放的运动运行时再次修改场景对象。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(CharacterControllerMotionModel));
        }
    }
}
