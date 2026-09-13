using System;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Stamina
{
    /// <summary>
    /// 体力池的实现。
    ///
    /// 按 Docs/Design/Combat/02 第 5 节：上限 240、停止消耗约 1 秒后开始恢复、恢复约 25/秒。
    /// 池子全队共享，因此由本系统持有而不是挂在任何一个角色身上。
    ///
    /// 消耗降低读**发起消耗的那个角色**的面板：同一次冲刺由谁发起，就按谁的减免算。
    /// </summary>
    internal sealed class StaminaSystem : XSystem, IStaminaSystem
    {
        /// <summary>基础体力上限，来自 02 第 5 节。神像加成在此之上叠加。</summary>
        public const float DefaultMax = 240f;
        /// <summary>停止消耗到开始恢复之间的延迟（秒）。</summary>
        public const float RecoveryDelaySeconds = 1f;
        /// <summary>恢复速率（点/秒）。</summary>
        public const float RecoveryRatePerSecond = 25f;

        /// <summary>按编号取回实体，用于读取发起者的体力消耗降低。</summary>
        private readonly IEntitySystem entitySystem;

        /// <summary>距离上一次消耗经过的时间；用于判断恢复延迟是否已过。</summary>
        private float secondsSinceLastConsume = RecoveryDelaySeconds;

        /// <inheritdoc />
        public float Current { get; private set; } = DefaultMax;

        /// <summary>七天神像累计提供的上限加成。</summary>
        private float statueBonus;

        /// <inheritdoc />
        public float Max => DefaultMax + statueBonus;

        /// <inheritdoc />
        public event Action<StaminaChangedEvent> Changed;

        /// <summary>创建体力系统；实体系统由组合根构造注入。</summary>
        public StaminaSystem(IEntitySystem entities)
        {
            entitySystem = entities ?? throw new ArgumentNullException(nameof(entities));
        }

        /// <summary>按恢复延迟与速率回复体力。</summary>
        public override void OnUpdate(float dt)
        {
            if (dt <= 0f) return;
            secondsSinceLastConsume += dt;
            if (secondsSinceLastConsume <= RecoveryDelaySeconds || Current >= Max) return;

            // 跨越延迟阈值的那一帧只能按**阈值之后**的那一段恢复。
            // 直接用整个 dt 会把仍处于延迟窗口内的时间也算成恢复时间，
            // 结果是帧率越低恢复越快——这类偏差在低帧率设备上才显形，极难复现。
            float recoverableSeconds = secondsSinceLastConsume - RecoveryDelaySeconds;
            if (recoverableSeconds > dt) recoverableSeconds = dt;

            float target = Current + RecoveryRatePerSecond * recoverableSeconds;
            if (target > Max) target = Max;
            if (target <= Current) return;
            float delta = target - Current;
            Current = target;
            Changed?.Invoke(new StaminaChangedEvent(Current, Max, delta));
        }

        /// <inheritdoc />
        public void SetStatueBonus(float bonus)
        {
            float safeBonus = bonus > 0f ? bonus : 0f;
            if (Mathf.Approximately(safeBonus, statueBonus)) return;

            float delta = safeBonus - statueBonus;
            statueBonus = safeBonus;
            // 上限提高时当前体力同额增加：解锁神像应当立刻可用，而不是要等恢复把新容量填满。
            // 上限降低时只做钳制，不额外扣除。
            Current = delta > 0f ? Current + delta : Mathf.Min(Current, Max);
            Changed?.Invoke(new StaminaChangedEvent(Current, Max, delta > 0f ? delta : 0f));
        }

        /// <inheritdoc />
        public bool CanAfford(int entityId, float amount)
        {
            return ResolveCost(entityId, amount) <= Current;
        }

        /// <inheritdoc />
        public bool TryConsume(int entityId, float amount)
        {
            float cost = ResolveCost(entityId, amount);
            if (cost <= 0f) return true;
            if (cost > Current) return false;
            Apply(-cost);
            return true;
        }

        /// <inheritdoc />
        public float ConsumeContinuous(int entityId, float amountPerSecond, float deltaSeconds)
        {
            if (deltaSeconds <= 0f) return 0f;
            float cost = ResolveCost(entityId, amountPerSecond * deltaSeconds);
            if (cost <= 0f) return 0f;
            // 持续消耗允许把池子扣空：冲刺该在体力见底的那一刻停下，而不是整帧失败。
            if (cost > Current) cost = Current;
            if (cost <= 0f) return 0f;
            Apply(-cost);
            return cost;
        }

        /// <summary>释放体力状态。</summary>
        public override void Dispose()
        {
            Changed = null;
        }

        /// <summary>写入一次变化并广播；消耗同时重置恢复延迟。</summary>
        private void Apply(float delta)
        {
            Current += delta;
            if (Current < 0f) Current = 0f;
            else if (Current > Max) Current = Max;
            if (delta < 0f) secondsSinceLastConsume = 0f;
            Changed?.Invoke(new StaminaChangedEvent(Current, Max, delta));
        }

        /// <summary>按发起者的体力消耗降低折算实际消耗；降低上限为 1，折算后不小于 0。</summary>
        private float ResolveCost(int entityId, float amount)
        {
            if (amount <= 0f) return 0f;
            if (!entitySystem.TryGetEntity(entityId, out Entity entity) || !entity.TryGetComp(out PropertyComponent property)) return amount;
            float reduction = property.GetValue(PropertyType.StaminaConsumptionReduction);
            if (reduction <= 0f) return amount;
            if (reduction >= 1f) return 0f;
            return amount * (1f - reduction);
        }
    }
}
