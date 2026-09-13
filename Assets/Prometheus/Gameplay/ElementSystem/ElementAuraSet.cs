using System;
using System.Collections.Generic;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Elements
{
    /// <summary>描述一条正在目标身上衰减的元素附着。</summary>
    public struct ElementAura
    {
        /// <summary>获取附着键；含三个以状态而非附着形式存在的伪元素。</summary>
        public Cfg.AuraKey Key;
        /// <summary>获取当前剩余附着量。</summary>
        public float Gauge;
        /// <summary>
        /// 获取衰减速率（U/秒）。
        /// 速率在写入时按**标称值**锁定，之后不随剩余量改变；同元素再次施加会重设它。
        /// </summary>
        public float DecayRate;
        /// <summary>
        /// 获取本条是否是伪元素状态（冻结、草原核、原激化）而非真实附着。
        ///
        /// 状态条的 <see cref="Gauge"/> 存的是**剩余秒数**而不是附着量 U：
        /// 状态只关心什么时候结束，附着量对它没有意义。读取 `Gauge` 前必须先看这个标记。
        /// </summary>
        public bool IsState;
        /// <summary>
        /// 获取写入这条伪元素状态的施加者实体编号；真实附着不使用该字段。
        ///
        /// 草原核到期引爆时要用创建者的等级与元素精通结算剧变伤害，
        /// 因此状态必须记住是谁造出来的——引爆发生在命中之后很久，届时已经没有攻击上下文。
        /// </summary>
        public int SourceEntityId;
    }

    /// <summary>
    /// 伪元素状态的判定。
    ///
    /// 三个伪元素与八元素共用 <see cref="Cfg.AuraKey"/>，但取值段独立（20 起），
    /// 因此只需一次区间判断，新增伪元素也不必改这里。
    /// </summary>
    public static class PseudoElement
    {
        /// <summary>伪元素在 AuraKey 中的起始取值；八元素占 0～8。</summary>
        private const int FirstPseudoValue = 20;

        /// <summary>判断一个附着键是否是伪元素状态。</summary>
        public static bool IsPseudo(Cfg.AuraKey key)
        {
            return (int)key >= FirstPseudoValue;
        }

        /// <summary>
        /// 把附着键转换回元素；伪元素状态没有对应元素，返回 `None`。
        /// 两个枚举的前九个成员一一对应，因此可以直接换值。
        /// </summary>
        public static Cfg.ElementType ToElement(Cfg.AuraKey key)
        {
            return IsPseudo(key) ? Cfg.ElementType.None : (Cfg.ElementType)(int)key;
        }
    }

    /// <summary>
    /// 一个目标身上全部元素附着的集合。
    ///
    /// 纯数据结构：不认识时间来源、不认识反应规则，只负责写入、衰减与消耗。
    /// 时间由调用方以 dt 传入，因此可以脱离 Unity 单独验证。
    /// </summary>
    public sealed class ElementAuraSet
    {
        /// <summary>保存当前存在的全部附着；键是元素，元素之间互不覆盖。</summary>
        private readonly Dictionary<Cfg.AuraKey, ElementAura> auras = new Dictionary<Cfg.AuraKey, ElementAura>();
        /// <summary>复用的键缓冲；衰减必须先取键快照再写回，字典不允许在遍历中被修改。</summary>
        private readonly List<Cfg.AuraKey> keyBuffer = new List<Cfg.AuraKey>();

        /// <summary>获取当前存在的附着数量。</summary>
        public int Count => auras.Count;

        /// <summary>获取当前全部附着；调用方只读遍历。</summary>
        public IReadOnlyDictionary<Cfg.AuraKey, ElementAura> Auras => auras;

        /// <summary>
        /// 写入或刷新一条附着。
        ///
        /// 同元素再次命中时取 `max(当前剩余量, 新写入量)`，但**衰减速率总是取新一次施加对应的值**——
        /// 即使新施加量更小也会重设速率。这是原神附着刷新的实际行为。
        /// </summary>
        /// <param name="key">附着键。</param>
        /// <param name="gauge">本次实际写入量（标称值已扣除衰减税）。</param>
        /// <param name="durationSeconds">该标称档位对应的总持续时间。</param>
        public void Apply(Cfg.AuraKey key, float gauge, float durationSeconds)
        {
            if (gauge <= 0f) throw new ArgumentOutOfRangeException(nameof(gauge), gauge, "Aura gauge must be positive.");
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "Aura duration must be positive.");

            float decayRate = gauge / durationSeconds;
            if (auras.TryGetValue(key, out ElementAura existing) && existing.Gauge > gauge) gauge = existing.Gauge;
            auras[key] = new ElementAura { Key = key, Gauge = gauge, DecayRate = decayRate };
        }

        /// <summary>
        /// 写入一条伪元素状态，用于冻结、草原核与原激化。
        ///
        /// 状态以「剩余秒数按 1/秒 倒数」的形式存储，从而与真实附着共用同一套衰减与清理路径，
        /// 不必为状态另建一条生命周期。重复触发同一状态取更长的剩余时间，短的那次不会缩短已有状态。
        /// </summary>
        /// <returns>本次是否新建了状态；刷新既有状态时返回 false，使调用方只在首次写入时施加产物 Effect。</returns>
        public bool ApplyState(Cfg.AuraKey key, float durationSeconds, int sourceEntityId)
        {
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds), durationSeconds, "State duration must be positive.");
            bool existed = auras.TryGetValue(key, out ElementAura existing);
            if (existed && existing.Gauge > durationSeconds) durationSeconds = existing.Gauge;
            auras[key] = new ElementAura { Key = key, Gauge = durationSeconds, DecayRate = 1f, IsState = true, SourceEntityId = sourceEntityId };
            return !existed;
        }

        /// <summary>查询一条附着；不存在时返回 false。</summary>
        public bool TryGet(Cfg.AuraKey key, out ElementAura aura)
        {
            return auras.TryGetValue(key, out aura);
        }

        /// <summary>
        /// 消耗指定附着的一部分；归零后移除。
        /// </summary>
        /// <returns>实际被消耗的量；附着不存在时返回 0。</returns>
        public float Consume(Cfg.AuraKey key, float amount)
        {
            if (amount <= 0f || !auras.TryGetValue(key, out ElementAura aura)) return 0f;
            float consumed = amount < aura.Gauge ? amount : aura.Gauge;
            aura.Gauge -= consumed;
            if (aura.Gauge <= 0f) auras.Remove(key);
            else auras[key] = aura;
            return consumed;
        }

        /// <summary>移除一条附着。</summary>
        public bool Remove(Cfg.AuraKey key)
        {
            return auras.Remove(key);
        }

        /// <summary>移除一条附着并取回它被移除前的内容，供调用方广播状态消失。</summary>
        public bool Remove(Cfg.AuraKey key, out ElementAura removed)
        {
            return auras.TryGetValue(key, out removed) && auras.Remove(key);
        }

        /// <summary>
        /// 按经过时间推进全部附着的衰减，并移除归零的条目。
        /// 先把键取成快照再逐个写回：在 foreach 中给已有键赋值同样属于「修改集合」，会抛 InvalidOperationException。
        /// </summary>
        /// <param name="deltaSeconds">经过的时间。</param>
        /// <param name="expiredStates">
        /// 收集本次自然到期的**伪元素状态**；传 null 表示调用方不关心。
        /// 只收状态不收真实附着：附着到期没有下游后果，而状态到期要撤下产物 Effect、
        /// 甚至要引爆草原核，必须让调用方看见。
        /// </param>
        public void Decay(float deltaSeconds, List<ElementAura> expiredStates = null)
        {
            if (deltaSeconds <= 0f || auras.Count == 0) return;
            keyBuffer.Clear();
            foreach (Cfg.AuraKey key in auras.Keys) keyBuffer.Add(key);
            for (int index = 0; index < keyBuffer.Count; index++)
            {
                Cfg.AuraKey key = keyBuffer[index];
                ElementAura aura = auras[key];
                aura.Gauge -= aura.DecayRate * deltaSeconds;
                if (aura.Gauge > 0f)
                {
                    auras[key] = aura;
                    continue;
                }
                auras.Remove(key);
                if (aura.IsState) expiredStates?.Add(aura);
            }
        }

        /// <summary>清空全部附着。</summary>
        public void Clear()
        {
            auras.Clear();
        }
    }
}
