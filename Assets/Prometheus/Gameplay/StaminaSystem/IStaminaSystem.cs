using System;

namespace Xuan.Prometheus.Stamina
{
    /// <summary>体力变化的只读事实，供 HUD 与表现层订阅。</summary>
    public readonly struct StaminaChangedEvent
    {
        /// <summary>获取变化后的当前体力。</summary>
        public readonly float Current;
        /// <summary>获取体力上限。</summary>
        public readonly float Max;
        /// <summary>获取本次变化量；消耗为负，恢复为正。</summary>
        public readonly float Delta;

        /// <summary>创建一条体力变化事实。</summary>
        public StaminaChangedEvent(float current, float max, float delta)
        {
            Current = current;
            Max = max;
            Delta = delta;
        }
    }

    /// <summary>
    /// 体力池的唯一权威。
    ///
    /// 体力**全队共享、不随切人重置**（02 第 5 节），因此它不能做成 `PropertyComponent` 上的
    /// 逐角色资源：切人时角色换了，池子不能跟着换。
    ///
    /// 它是攀爬、游泳、滑翔、冲刺与重击的公共约束，因此先于这些动作落地。
    /// </summary>
    public interface IStaminaSystem : ISystemContract
    {
        /// <summary>获取当前体力。</summary>
        float Current { get; }

        /// <summary>获取体力上限；等于基础上限加上七天神像提供的加成。</summary>
        float Max { get; }

        /// <summary>
        /// 设置七天神像累计提供的体力上限加成。
        ///
        /// 上限是**全局**的，不随角色变化：体力池全队共享，能提升它的只有神像这类世界进度。
        /// 提升上限时当前体力同额增加——解锁神像立刻就能多冲刺一段，而不是要等恢复。
        /// </summary>
        /// <param name="bonus">累计加成总量，不是增量；重复传入同一个值不会重复生效。</param>
        void SetStatueBonus(float bonus);

        /// <summary>体力发生变化时触发。</summary>
        event Action<StaminaChangedEvent> Changed;

        /// <summary>
        /// 尝试消耗一次体力。
        ///
        /// **全有全无**：体力不足时一点都不扣，返回 false。原神里体力不够就放不出重击，
        /// 而不是放出一个削弱版本；扣一半会让调用方必须处理一个不存在的中间态。
        /// </summary>
        /// <param name="entityId">发起消耗的实体；它的体力消耗降低会作用于本次消耗。</param>
        /// <param name="amount">消耗量；非正数视为不消耗并返回 true。</param>
        /// <returns>体力足够并已扣除时返回 true。</returns>
        bool TryConsume(int entityId, float amount);

        /// <summary>
        /// 按持续消耗速率扣除体力，扣多少算多少。
        ///
        /// 与 <see cref="TryConsume"/> 的区别是它允许把池子扣空：冲刺与攀爬是持续行为，
        /// 体力见底时应当在那一刻停下，而不是最后一帧整体失败。
        /// </summary>
        /// <returns>实际扣除量；返回值小于请求量表示体力已经见底。</returns>
        float ConsumeContinuous(int entityId, float amountPerSecond, float deltaSeconds);

        /// <summary>判断当前体力是否足以支付一次消耗，不改变任何状态。</summary>
        bool CanAfford(int entityId, float amount);
    }
}
