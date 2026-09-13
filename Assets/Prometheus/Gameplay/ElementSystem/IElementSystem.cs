using System;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Elements
{
    /// <summary>描述一次元素施加请求。</summary>
    public struct ElementApplyRequest
    {
        /// <summary>施加者实体编号；与天赋一起构成 ICD 分组。</summary>
        public int SourceEntityId;
        /// <summary>承受附着的目标实体编号。</summary>
        public int TargetEntityId;
        /// <summary>具体天赋标识；同一角色的普攻与战技必须用不同标识，否则两者会共用一条 ICD。</summary>
        public string TalentId;
        /// <summary>本段落使用的独立 ICD 组号；共享策略时为 0。</summary>
        public int IcdGroupId;
        /// <summary>ICD 策略。</summary>
        public Cfg.IcdPolicy IcdPolicy;
        /// <summary>本次攻击携带的元素；`Anemo` 与 `Geo` 只触发反应，不留存附着。</summary>
        public Cfg.ElementType Element;
        /// <summary>附着强度档位；`None` 表示不附着，仅用于判定反应。</summary>
        public Cfg.GaugeStrength Strength;
    }

    /// <summary>描述一次伪元素状态变化的原因。</summary>
    public enum PseudoStateChange
    {
        /// <summary>状态首次写入；刷新既有状态不会再次广播。</summary>
        Applied = 0,
        /// <summary>状态自然到期。草原核正是在这一刻引爆。</summary>
        Expired = 1,
        /// <summary>状态被反应消耗。碎冰解除冻结、超绽放消耗草原核都走这里，且**不引爆**。</summary>
        Consumed = 2
    }

    /// <summary>
    /// 一次伪元素状态变化的完整上下文。
    ///
    /// 到期与被消耗必须分开：草原核自然到期要引爆，被超绽放消耗则由超绽放自己结算伤害，
    /// 两者合并会让同一个核打出两次伤害。
    /// </summary>
    public readonly struct PseudoStateChangedEvent
    {
        /// <summary>获取承载状态的目标实体编号。</summary>
        public readonly int TargetEntityId;
        /// <summary>获取写入该状态的施加者实体编号。</summary>
        public readonly int SourceEntityId;
        /// <summary>获取状态键。</summary>
        public readonly Cfg.AuraKey Key;
        /// <summary>获取变化原因。</summary>
        public readonly PseudoStateChange Change;
        /// <summary>
        /// 获取写入该状态的反应行；状态消失时同样携带它。
        /// 草原核引爆要用这一行的倍率与伤害元素，而引爆时早已没有攻击上下文。
        /// </summary>
        public readonly Cfg.ReactionMatrixRow Reaction;

        /// <summary>创建一次状态变化事件。</summary>
        public PseudoStateChangedEvent(int targetEntityId, int sourceEntityId, Cfg.AuraKey key, PseudoStateChange change, Cfg.ReactionMatrixRow reaction)
        {
            TargetEntityId = targetEntityId;
            SourceEntityId = sourceEntityId;
            Key = key;
            Change = change;
            Reaction = reaction;
        }
    }

    /// <summary>只读的附着快照，供 UI、AI 与测试查询目标当前状态。</summary>
    public readonly struct ElementAuraSnapshot
    {
        /// <summary>获取目标当前的附着集合；目标没有任何附着时为空。</summary>
        public readonly ElementAuraSet Auras;

        /// <summary>创建一份附着快照。</summary>
        internal ElementAuraSnapshot(ElementAuraSet auras)
        {
            Auras = auras;
        }
    }

    /// <summary>
    /// 元素附着与反应的唯一权威。
    ///
    /// 它只修改附着与 ICD 状态并返回命中的反应行，**不结算任何伤害、不施加任何效果**。
    /// 剧变伤害与反应产物由伤害管线在结算阶段统一发起，避免在附着阶段产生递归。
    /// </summary>
    public interface IElementSystem : ISystemContract
    {
        /// <summary>
        /// 施加一次元素附着，并返回本次触发的反应。
        ///
        /// 同步、无副作用外泄：只改变附着与 ICD，其余由调用方按返回值决定。
        /// </summary>
        ReactionResult Apply(in ElementApplyRequest request);

        /// <summary>只读查询目标当前附着。</summary>
        ElementAuraSnapshot QueryAura(int entityId);

        /// <summary>清除目标的全部附着，供实体死亡或回收时调用。</summary>
        void ClearAura(int entityId);

        /// <summary>
        /// 伪元素状态写入、到期或被消耗时触发。
        ///
        /// 本系统只广播事实，不施加任何产物：冻结的行动禁止、草原核的引爆伤害
        /// 都由 `ReactionProductSystem` 消费本事件后完成，元素系统因此仍然不认识属性面板与伤害。
        /// </summary>
        event Action<PseudoStateChangedEvent> PseudoStateChanged;
    }
}
