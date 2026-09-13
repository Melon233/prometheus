using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// 把一笔**已经算好的**伤害落到目标身上，并发布对应的事实信号。
    ///
    /// 抽成公共入口是因为伤害不只来自命中：草原核到期引爆、场域周期伤害都要落地，
    /// 而「扣血 → 发 HpChanged → 发 Die → 发 DamageApplied → 致死再发 Killed」这串顺序
    /// 一旦有第二份实现，两边迟早会漏掉其中一条。
    ///
    /// 本类**不计算任何数值**：乘区在 `DamageCalculator`，取数与选槽在 `DamagePipeline`。
    /// </summary>
    public static class DamageSettlement
    {
        /// <summary>
        /// 结算一笔伤害。
        /// </summary>
        /// <param name="runtime">发布信号的效果运行时。</param>
        /// <param name="property">目标属性面板。</param>
        /// <param name="damage">已完成全部乘区计算的伤害值。</param>
        /// <param name="tags">写入结果信号的标签。</param>
        /// <param name="facts">本笔伤害的结算事实；`WasFatal` 由本方法填写，调用方无需预判。</param>
        /// <param name="context">因果与身份信息。</param>
        public static void Settle(EffectRuntime runtime, PropertyComponent property, float damage, EffectTag tags, in DamageFacts facts, in DamageSettlementContext context)
        {
            if (runtime == null || property == null) return;

            // 护盾先于生命值吸收（00 第 3 节的结算顺序）。被吸收掉的部分不进 OnTakeDamage，
            // 因此也不会产生 HpChanged 与受击事实——挡住的伤害不是「受到的伤害」。
            float absorbed = context.Target == null ? 0f : runtime.ShieldSystem.Absorb(context.Target.EntityId, damage, facts.Element);
            damage -= absorbed;

            float oldHp = property.Hp;
            float actualDamage = property.OnTakeDamage(damage, out bool wasFatal);
            PublishHealthEvents(context.Target, property, oldHp, actualDamage, wasFatal);
            PublishStaggerEvents(context.Target, property, actualDamage, facts.StaggerLevel, wasFatal);

            // 是否致死只有结算完才知道，因此在这里补进事实而不是要求调用方预判。
            DamageFacts settled = facts.With(wasFatal: wasFatal);
            runtime.EnqueueSignal(context.CreateResult(EffectSignalType.DamageApplied, damage, actualDamage, tags, in settled));
            if (wasFatal) runtime.EnqueueSignal(context.CreateResult(EffectSignalType.Killed, damage, actualDamage, tags, in settled));
        }

        /// <summary>
        /// 同步发送生命变化和死亡事实事件；受击控制与表现统一由 Stun Effect 和 ControlState 驱动。
        /// </summary>
        private static void PublishHealthEvents(Entity target, PropertyComponent property, float oldHp, float actualDamage, bool wasFatal)
        {
            if (actualDamage <= 0f || target == null) return;
            bool hasEntityEvents = target.TryGetComp(out EventComponent eventComponent);
            if (hasEntityEvents) eventComponent.Invoke(new HpChangedEvent { oldHp = oldHp, newHp = property.Hp, maxHp = property.MaxHp });
            if (!wasFatal) return;
            if (hasEntityEvents) eventComponent.Invoke(new DieEvent());
            // 首次致死伤害是统一的死亡事实来源，向 Core.Event 转发实体编号供跨系统响应。
            if (target.EntityId > 0) Core.Event.Invoke(new EntityDiedEvent(target.EntityId));
        }

        /// <summary>
        /// 按 08 第 2.3 节的分级比较发布受击事实：`打断等级 > 抗打断等级` 时打断成立，否则只发轻微受击。
        ///
        /// 致死伤害两条都不发：死亡动画会抢占受击动画，此时再播受击只会打断死亡表现。
        /// 零扣血同样不发——被护盾完全挡下的攻击不构成受击。
        /// </summary>
        private static void PublishStaggerEvents(Entity target, PropertyComponent property, float actualDamage, int staggerLevel, bool wasFatal)
        {
            if (actualDamage <= 0f || wasFatal || target == null) return;
            if (!target.TryGetComp(out EventComponent eventComponent)) return;

            float resistance = property.EffectiveStaggerResistance;
            if (staggerLevel > resistance) eventComponent.Invoke(new StaggeredEvent(actualDamage, staggerLevel, resistance));
            else eventComponent.Invoke(new StaggerResistedEvent(actualDamage, staggerLevel, resistance));
        }
    }

    /// <summary>
    /// 一笔伤害的因果与身份信息。
    ///
    /// 命中产生的伤害挂在驱动它的信号下形成因果链；草原核引爆这类没有驱动信号的伤害
    /// 自成一条新链，由运行时分配事务编号。
    /// </summary>
    public readonly struct DamageSettlementContext
    {
        /// <summary>获取直接释放者。</summary>
        public readonly Entity Caster;
        /// <summary>获取承伤目标。</summary>
        public readonly Entity Target;
        /// <summary>获取因果链的实际源头。</summary>
        public readonly Entity Source;
        /// <summary>获取产生本次伤害的能力编号。</summary>
        public readonly string AbilityId;
        /// <summary>获取产生本次伤害的效果实例编号；无实例时为 0。</summary>
        public readonly long InstanceId;
        /// <summary>获取伤害发生的世界坐标。</summary>
        public readonly Vector3 Position;
        /// <summary>获取驱动本次伤害的信号；为空表示本次伤害自成一条因果链。</summary>
        public readonly EffectSignal Parent;

        /// <summary>创建一份伤害身份信息。</summary>
        public DamageSettlementContext(Entity caster, Entity target, Entity source, string abilityId, long instanceId, Vector3 position, EffectSignal parent)
        {
            Caster = caster;
            Target = target;
            Source = source;
            AbilityId = abilityId ?? string.Empty;
            InstanceId = instanceId;
            Position = position;
            Parent = parent;
        }

        /// <summary>按有无驱动信号构造结果信号，使两类来源产生结构一致的事实。</summary>
        internal EffectSignal CreateResult(EffectSignalType type, float requestedValue, float value, EffectTag tags, in DamageFacts facts)
        {
            if (Parent != null)
                return Parent.CreateChild(type, Caster, Target, Source, requestedValue, value, tags, AbilityId, InstanceId, Position, facts);
            return new EffectSignal(type, Caster, Target, Source, requestedValue, value, tags, AbilityId, InstanceId, Position, 0L, 0, facts);
        }
    }
}
