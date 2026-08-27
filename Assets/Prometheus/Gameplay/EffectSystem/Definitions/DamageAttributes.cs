using System;

namespace Xuan.Prometheus
{
    /// <summary>
    /// 标识一笔伤害唯一拥有的属性；Physical 固定为零，使未迁移资产和默认序列化值安全回退为物理。
    /// </summary>
    public enum DamageAttribute
    {
        /// <summary>物理伤害不参与任何元素克制。</summary>
        Physical = 0,
        /// <summary>火属性克制冰属性。</summary>
        Fire = 1,
        /// <summary>水属性克制火属性。</summary>
        Water = 2,
        /// <summary>雷属性克制水属性。</summary>
        Lightning = 3,
        /// <summary>冰属性克制草属性。</summary>
        Ice = 4,
        /// <summary>草属性克制雷属性。</summary>
        Grass = 5,
        /// <summary>光属性与暗属性互相克制。</summary>
        Light = 6,
        /// <summary>暗属性与光属性互相克制。</summary>
        Dark = 7
    }

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
    /// 使用位标记描述伤害属性覆盖可以影响的动作类别。
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
    /// 描述最终伤害属性与目标角色属性之间是否形成克制。
    /// </summary>
    public enum DamageAttributeRelation
    {
        /// <summary>当前属性组合不产生独立倍率。</summary>
        Neutral = 0,
        /// <summary>攻击属性克制目标属性并产生百分之三十独立增伤。</summary>
        Advantage = 1
    }

    /// <summary>
    /// 表示一份按对象身份添加和移除的出伤属性覆盖；高优先级和同优先级后加入者优先。
    /// </summary>
    public sealed class DamageAttributeModifier
    {
        /// <summary>获取覆盖后的伤害属性。</summary>
        public DamageAttribute Attribute { get; }

        /// <summary>获取当前覆盖作用的动作集合。</summary>
        public DamageActionMask ActionMask { get; }

        /// <summary>获取覆盖优先级，数值越大优先级越高。</summary>
        public int Priority { get; }

        /// <summary>获取 PropertyComponent 分配的稳定加入序号，用于解决同优先级覆盖。</summary>
        internal long Sequence { get; }

        /// <summary>创建一份只能由 PropertyComponent 登记的伤害属性覆盖。</summary>
        internal DamageAttributeModifier(DamageAttribute attribute, DamageActionMask actionMask, int priority, long sequence)
        {
            Attribute = attribute;
            ActionMask = actionMask;
            Priority = priority;
            Sequence = sequence;
        }
    }

    /// <summary>
    /// DamageAttributeRules 集中维护动作基础属性、动作掩码和克制倍率，避免结算规则散落在表现或属性组件中。
    /// </summary>
    public static class DamageAttributeRules
    {
        /// <summary>克制关系使用独立百分之三十增伤乘区。</summary>
        public const float AdvantageMultiplier = 1.3f;

        /// <summary>
        /// 普通攻击和特殊攻击以物理为基础，技能、大招及显式读取角色属性的 Effect 使用角色元素。
        /// </summary>
        public static DamageAttribute GetBaseAttribute(DamageActionType actionType, DamageAttribute characterAttribute)
        {
            return actionType == DamageActionType.NormalAttack || actionType == DamageActionType.SpecialAttack ? DamageAttribute.Physical : characterAttribute;
        }

        /// <summary>
        /// 将单个动作类别转换为属性覆盖筛选使用的位标记。
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

        /// <summary>
        /// 判断攻击属性是否克制目标角色属性；物理、同属性和反向五行关系均保持中立。
        /// </summary>
        public static DamageAttributeRelation GetRelation(DamageAttribute attackAttribute, DamageAttribute targetAttribute)
        {
            if (attackAttribute == DamageAttribute.Physical || targetAttribute == DamageAttribute.Physical) return DamageAttributeRelation.Neutral;
            bool hasAdvantage = attackAttribute == DamageAttribute.Fire && targetAttribute == DamageAttribute.Ice || attackAttribute == DamageAttribute.Ice && targetAttribute == DamageAttribute.Grass || attackAttribute == DamageAttribute.Grass && targetAttribute == DamageAttribute.Lightning || attackAttribute == DamageAttribute.Lightning && targetAttribute == DamageAttribute.Water || attackAttribute == DamageAttribute.Water && targetAttribute == DamageAttribute.Fire || attackAttribute == DamageAttribute.Light && targetAttribute == DamageAttribute.Dark || attackAttribute == DamageAttribute.Dark && targetAttribute == DamageAttribute.Light;
            return hasAdvantage ? DamageAttributeRelation.Advantage : DamageAttributeRelation.Neutral;
        }

        /// <summary>
        /// 返回克制独立乘区；只有 Advantage 返回 1.3，其余关系固定返回 1。
        /// </summary>
        public static float GetMultiplier(DamageAttribute attackAttribute, DamageAttribute targetAttribute)
        {
            return GetRelation(attackAttribute, targetAttribute) == DamageAttributeRelation.Advantage ? AdvantageMultiplier : 1f;
        }
    }
}
