using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// 保存基于 CharacterController 的地面运动参数；独立同名脚本确保 Unity 能为该 ScriptableObject 建立稳定 MonoScript 引用。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterControllerMotion", menuName = "Prometheus/Actor/Motion/Character Controller")]
    public sealed class CharacterControllerMotionModelDefinition : ActorMotionModelDefinition
    {
        [SerializeField, Min(0f)] private float acceleration = 35f;
        [SerializeField, Min(0f)] private float deceleration = 45f;
        [SerializeField, Min(0f)] private float airAcceleration = 10f;
        [SerializeField] private float gravity = -25f;
        [SerializeField, Min(0f)] private float jumpSpeed = 9f;
        [SerializeField] private float groundedVerticalSpeed = -2f;
        [SerializeField, Min(0f)] private float maximumFallSpeed = 40f;

        /// <summary>为一个 Actor 创建独立运动运行时，资产本身只保存可共享的只读参数。</summary>
        public override IActorMotionModel CreateRuntime(ActorMotionContext context)
        {
            if (context.CharacterController == null) throw new InvalidOperationException($"Actor '{context.Root.name}' requires a CharacterController for '{name}'.");
            return new CharacterControllerMotionModel(context, acceleration, deceleration, airAcceleration, gravity, jumpSpeed, groundedVerticalSpeed, maximumFallSpeed);
        }
    }
}
