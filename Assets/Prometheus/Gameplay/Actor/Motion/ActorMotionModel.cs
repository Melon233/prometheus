using System;
using UnityEngine;

namespace Xuan.Prometheus.Actor
{
    /// <summary>
    /// 描述一个固定模拟 Tick 内由控制器和行为共同生成的运动意图；该值不直接决定使用哪一种物理实现。
    /// </summary>
    public readonly struct ActorMotionIntent
    {
        /// <summary>
        /// 创建一个运动意图。
        /// </summary>
        /// <param name="worldDirection">世界空间平面移动方向，运行时会将长度限制到一。</param>
        /// <param name="speed">期望平面速度。</param>
        /// <param name="jumpRequested">当前 Tick 是否请求起跳。</param>
        /// <param name="additiveDisplacement">行为时间轴在当前 Tick 追加的世界空间位移。</param>
        public ActorMotionIntent(Vector3 worldDirection, float speed, bool jumpRequested, Vector3 additiveDisplacement)
        {
            WorldDirection = worldDirection;
            Speed = Mathf.Max(0f, speed);
            JumpRequested = jumpRequested;
            AdditiveDisplacement = additiveDisplacement;
        }

        /// <summary>获取世界空间平面移动方向。</summary>
        public Vector3 WorldDirection { get; }

        /// <summary>获取期望平面速度。</summary>
        public float Speed { get; }

        /// <summary>获取当前 Tick 是否请求起跳。</summary>
        public bool JumpRequested { get; }

        /// <summary>获取行为时间轴在当前 Tick 追加的世界空间位移。</summary>
        public Vector3 AdditiveDisplacement { get; }

        /// <summary>获取不产生任何位移的空意图。</summary>
        public static ActorMotionIntent None => new ActorMotionIntent(Vector3.zero, 0f, false, Vector3.zero);
    }

    /// <summary>
    /// 保存运动模型完成一个固定 Tick 后的权威客户端快照，供动画、镜头和调试表现只读消费。
    /// </summary>
    public readonly struct ActorMotionSnapshot
    {
        /// <summary>
        /// 创建一个运动快照。
        /// </summary>
        public ActorMotionSnapshot(Vector3 position, Vector3 velocity, bool isGrounded, bool landedThisTick)
        {
            Position = position;
            Velocity = velocity;
            IsGrounded = isGrounded;
            LandedThisTick = landedThisTick;
        }

        /// <summary>获取当前世界位置。</summary>
        public Vector3 Position { get; }

        /// <summary>获取当前世界速度。</summary>
        public Vector3 Velocity { get; }

        /// <summary>获取运动模型当前是否处于地面。</summary>
        public bool IsGrounded { get; }

        /// <summary>获取运动模型是否在当前 Tick 首次落地。</summary>
        public bool LandedThisTick { get; }
    }

    /// <summary>
    /// 定义可替换的客户端运动模型；角色、飞行单位和载具通过不同实现解释同一种上层运动意图。
    /// </summary>
    public interface IActorMotionModel : IDisposable
    {
        /// <summary>获取最近一个固定 Tick 生成的运动快照。</summary>
        ActorMotionSnapshot Snapshot { get; }

        /// <summary>在一个固定模拟 Tick 内解释并应用运动意图。</summary>
        void Simulate(ActorMotionIntent intent, float fixedDeltaTime);

        /// <summary>将对象传送到指定世界姿态并重置实现持有的瞬时速度。</summary>
        void Teleport(Vector3 position, Quaternion rotation);
    }

    /// <summary>
    /// 保存创建客户端运动模型所需的场景绑定；服务端可以为同一意图实现独立物理适配器。
    /// </summary>
    public readonly struct ActorMotionContext
    {
        /// <summary>
        /// 创建一组运动模型场景绑定。
        /// </summary>
        public ActorMotionContext(Transform root, CharacterController characterController)
        {
            Root = root != null ? root : throw new ArgumentNullException(nameof(root));
            CharacterController = characterController;
        }

        /// <summary>获取对象根节点。</summary>
        public Transform Root { get; }

        /// <summary>获取可选的 Unity CharacterController。</summary>
        public CharacterController CharacterController { get; }
    }

    /// <summary>
    /// 作为资产化运动配置的统一工厂基类；增加飞行或载具时只需新增 Definition 与 Runtime 实现。
    /// </summary>
    public abstract class ActorMotionModelDefinition : ScriptableObject
    {
        /// <summary>使用当前资产和场景绑定创建独立运行时，禁止把可变速度写回共享资产。</summary>
        public abstract IActorMotionModel CreateRuntime(ActorMotionContext context);
    }
}
