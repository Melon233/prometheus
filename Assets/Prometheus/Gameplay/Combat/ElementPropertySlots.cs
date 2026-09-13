using System;
using Xuan.Prometheus.Component;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Combat
{
    /// <summary>
    /// 元素与动作到属性位的映射。
    ///
    /// 伤害管线要读的「火伤加成」「火抗」「火抗削减」在 05A 里是三十多个独立属性位，
    /// 具体读哪一个只能在运行时按本次伤害的元素决定。把选槽集中在这里，
    /// 避免每个消费方各写一份 switch，也让新增元素时只有一处要改。
    /// </summary>
    public static class ElementPropertySlots
    {
        /// <summary>取该元素对应的伤害加成属性位。</summary>
        public static PropertyType DamageBonus(Cfg.ElementType element)
        {
            switch (element)
            {
                case Cfg.ElementType.Pyro: return PropertyType.PyroDamageBonus;
                case Cfg.ElementType.Hydro: return PropertyType.HydroDamageBonus;
                case Cfg.ElementType.Electro: return PropertyType.ElectroDamageBonus;
                case Cfg.ElementType.Cryo: return PropertyType.CryoDamageBonus;
                case Cfg.ElementType.Dendro: return PropertyType.DendroDamageBonus;
                case Cfg.ElementType.Anemo: return PropertyType.AnemoDamageBonus;
                case Cfg.ElementType.Geo: return PropertyType.GeoDamageBonus;
                case Cfg.ElementType.Physical: return PropertyType.PhysicalDamageBonus;
                default: throw new ArgumentOutOfRangeException(nameof(element), element, "Element has no damage bonus slot.");
            }
        }

        /// <summary>取该元素对应的抗性属性位。</summary>
        public static PropertyType Resistance(Cfg.ElementType element)
        {
            switch (element)
            {
                case Cfg.ElementType.Pyro: return PropertyType.PyroResistance;
                case Cfg.ElementType.Hydro: return PropertyType.HydroResistance;
                case Cfg.ElementType.Electro: return PropertyType.ElectroResistance;
                case Cfg.ElementType.Cryo: return PropertyType.CryoResistance;
                case Cfg.ElementType.Dendro: return PropertyType.DendroResistance;
                case Cfg.ElementType.Anemo: return PropertyType.AnemoResistance;
                case Cfg.ElementType.Geo: return PropertyType.GeoResistance;
                case Cfg.ElementType.Physical: return PropertyType.PhysicalResistance;
                default: throw new ArgumentOutOfRangeException(nameof(element), element, "Element has no resistance slot.");
            }
        }

        /// <summary>取该元素对应的抗性削减属性位。</summary>
        public static PropertyType ResistanceReduction(Cfg.ElementType element)
        {
            switch (element)
            {
                case Cfg.ElementType.Pyro: return PropertyType.PyroResistanceReduction;
                case Cfg.ElementType.Hydro: return PropertyType.HydroResistanceReduction;
                case Cfg.ElementType.Electro: return PropertyType.ElectroResistanceReduction;
                case Cfg.ElementType.Cryo: return PropertyType.CryoResistanceReduction;
                case Cfg.ElementType.Dendro: return PropertyType.DendroResistanceReduction;
                case Cfg.ElementType.Anemo: return PropertyType.AnemoResistanceReduction;
                case Cfg.ElementType.Geo: return PropertyType.GeoResistanceReduction;
                case Cfg.ElementType.Physical: return PropertyType.PhysicalResistanceReduction;
                default: throw new ArgumentOutOfRangeException(nameof(element), element, "Element has no resistance reduction slot.");
            }
        }

        /// <summary>
        /// 取该动作类别对应的伤害加成属性位。
        ///
        /// 返回 false 表示这个动作类别没有专属加成位（独立 Effect 与周期伤害）——
        /// 它们只吃全伤害加成与元素加成，这是设计结论而不是缺漏。
        /// </summary>
        public static bool TryActionBonus(DamageActionType actionType, out PropertyType slot)
        {
            switch (actionType)
            {
                case DamageActionType.NormalAttack: slot = PropertyType.NormalAttackBonus; return true;
                case DamageActionType.SpecialAttack: slot = PropertyType.ChargedAttackBonus; return true;
                case DamageActionType.Skill: slot = PropertyType.SkillBonus; return true;
                case DamageActionType.Ultimate: slot = PropertyType.BurstBonus; return true;
                default: slot = default; return false;
            }
        }

        /// <summary>
        /// 把 `ReactionMatrix` 的 `reactionBonusAttr` 列解析为属性位。
        ///
        /// 空值表示该反应没有专属加成（例如结晶的护盾量以外部分），返回 false；
        /// 非空但拼错则抛出——配表里的属性名写错必须当场暴露，静默当作零会让加成永远不生效。
        /// </summary>
        public static bool TryReactionBonus(string reactionBonusAttr, out PropertyType slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(reactionBonusAttr)) return false;
            if (!Enum.TryParse(reactionBonusAttr, false, out slot))
                throw new InvalidOperationException($"TbReactionMatrix.reactionBonusAttr '{reactionBonusAttr}' does not name a PropertyType slot.");
            return true;
        }
    }
}
