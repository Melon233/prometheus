using System;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 标识产生伤害的动作类别，用于选择普攻物理规则、角色元素规则和 Effect 覆盖范围。
    /// </summary>
    public enum DamageActionType
    {
        /// <summary>普通攻击以物理作为基础属性。</summary>
        NormalAttack = 0,
        /// <summary>特殊攻击以物理作为基础属性。</summary>
        SpecialAttack = 1,
        /// <summary>技能以角色元素作为基础属性。</summary>
        Skill = 2,
        /// <summary>大招以角色元素作为基础属性。</summary>
        Ultimate = 3,
        /// <summary>独立 Effect 默认使用角色元素，但 DamageOperation 可以覆盖来源。</summary>
        Effect = 4,
        /// <summary>周期伤害默认使用角色元素，但固定属性 DOT 可以覆盖来源。</summary>
        Periodic = 5
    }

    /// <summary>
    /// 使用位标记描述伤害元素覆盖可以影响的动作类别。
    /// </summary>
    [Flags]
    public enum DamageActionMask
    {
        /// <summary>不匹配任何伤害动作。</summary>
        None = 0,
        /// <summary>匹配普通攻击。</summary>
        NormalAttack = 1 << 0,
        /// <summary>匹配特殊攻击。</summary>
        SpecialAttack = 1 << 1,
        /// <summary>匹配技能。</summary>
        Skill = 1 << 2,
        /// <summary>匹配大招。</summary>
        Ultimate = 1 << 3,
        /// <summary>匹配独立 Effect 伤害。</summary>
        Effect = 1 << 4,
        /// <summary>匹配周期伤害。</summary>
        Periodic = 1 << 5,
        /// <summary>匹配当前定义的全部伤害动作。</summary>
        All = NormalAttack | SpecialAttack | Skill | Ultimate | Effect | Periodic
    }

    /// <summary>
    /// 表示一份按对象身份添加和移除的元素附魔；高优先级和同优先级后加入者优先。
    ///
    /// 附魔只改变伤害的**元素身份**，不改变数值：改成火之后，这段伤害走火元素加成、火抗、
    /// 并以火参与元素附着与反应。数值仍由 `DamagePipeline` 统一按主公式结算。
    /// </summary>
    public sealed class ElementInfusionModifier
    {
        /// <summary>获取覆盖后的伤害元素。</summary>
        public Cfg.ElementType Element { get; }

        /// <summary>获取当前覆盖作用的动作集合。</summary>
        public DamageActionMask ActionMask { get; }

        /// <summary>获取覆盖优先级，数值越大优先级越高。</summary>
        public int Priority { get; }

        /// <summary>获取 PropertyComponent 分配的稳定加入序号，用于解决同优先级覆盖。</summary>
        internal long Sequence { get; }

        /// <summary>创建一份只能由 PropertyComponent 登记的元素附魔。</summary>
        internal ElementInfusionModifier(Cfg.ElementType element, DamageActionMask actionMask, int priority, long sequence)
        {
            Element = element;
            ActionMask = actionMask;
            Priority = priority;
            Sequence = sequence;
        }
    }

    /// <summary>
    /// DamageElementRules 集中维护动作的基础元素与动作掩码，避免结算规则散落在表现或属性组件中。
    ///
    /// 这里**不再有任何克制倍率**：元素之间的相互作用全部由元素反应矩阵承载，
    /// 由 `IElementSystem` 判定、由 `DamageCalculator` 求值。
    /// </summary>
    public static class DamageElementRules
    {
        /// <summary>
        /// 普通攻击和特殊攻击以物理为基础，技能、大招及显式读取角色属性的 Effect 使用角色元素。
        /// 普攻走物理正是附魔存在的前提：附魔把普攻的元素从物理改写为角色元素。
        /// </summary>
        public static Cfg.ElementType GetBaseElement(DamageActionType actionType, Cfg.ElementType characterElement)
        {
            return actionType == DamageActionType.NormalAttack || actionType == DamageActionType.SpecialAttack ? Cfg.ElementType.Physical : characterElement;
        }

        /// <summary>
        /// 将单个动作类别转换为元素覆盖筛选使用的位标记。
        /// </summary>
        public static DamageActionMask GetActionMask(DamageActionType actionType)
        {
            switch (actionType)
            {
                case DamageActionType.NormalAttack: return DamageActionMask.NormalAttack;
                case DamageActionType.SpecialAttack: return DamageActionMask.SpecialAttack;
                case DamageActionType.Skill: return DamageActionMask.Skill;
                case DamageActionType.Ultimate: return DamageActionMask.Ultimate;
                case DamageActionType.Effect: return DamageActionMask.Effect;
                case DamageActionType.Periodic: return DamageActionMask.Periodic;
                default: return DamageActionMask.None;
            }
        }
    }
}
