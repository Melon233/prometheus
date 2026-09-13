namespace Xuan.Prometheus.Combat
{
    /// <summary>
    /// 伤害、治疗与护盾的纯函数结算器。
    ///
    /// 全部方法无副作用、不读取任何全局状态：输入完全由 Context 给出，相同输入恒得相同输出。
    /// 扣血、施加效果、发布表现信号都由调用方在拿到结果之后执行——这是把伤害公式与战斗流程
    /// 解耦的唯一手段，也是数值验收得以自动化的前提。
    /// </summary>
    public static class DamageCalculator
    {
        /// <summary>抗性区的高抗软上限阈值；到达该抗性后改用倒数形式，避免高抗敌人完全免疫。</summary>
        private const float HighResistanceThreshold = 0.75f;

        /// <summary>
        /// 按主公式求一次伤害的最终值：
        /// `(基础伤害 + 激化加算) × (1 + 伤害加成) × 暴击区 × 增幅倍率 × 防御区 × 抗性区 × (1 - 减伤)`。
        ///
        /// 乘区顺序不可调换：激化是加算项必须最先并入，增幅是最后一层乘区必须在防御区之前。
        /// </summary>
        public static float Evaluate(in DamageContext context)
        {
            float baseDamage = context.SkillMultiplier * context.ScalingValue + context.FlatDamage + context.CatalyzeBonus;
            float damage = baseDamage * (1f + context.DamageBonus);
            damage *= CriticalMultiplier(context.IsCritical, context.CritDamage);
            damage *= context.AmplifyMultiplier;
            damage *= DefenseMultiplier(context.AttackerLevel, context.TargetLevel, context.DefenseReduction, context.DefenseIgnore);
            damage *= ResistanceMultiplier(context.TargetResistance);
            damage *= 1f - context.DamageReduction;
            return damage > 0f ? damage : 0f;
        }

        /// <summary>
        /// 按剧变公式求一次反应伤害：
        /// `等级系数 × 反应倍率 × (1 + 精通项 + 反应加成) × 抗性区`。
        ///
        /// 剧变不吃攻击力、不吃伤害加成、不能暴击、不吃防御区——它是独立结算的第二笔伤害。
        /// </summary>
        public static float EvaluateTransformative(in TransformativeDamageContext context)
        {
            float damage = context.LevelCoefficient * context.ReactionMultiplier;
            damage *= ReactionFormula.Scale(context.ElementalMastery, context.ReactionBonus, context.MasteryCoefficient, context.MasteryDenominator);
            damage *= ResistanceMultiplier(context.TargetResistance);
            return damage > 0f ? damage : 0f;
        }

        /// <summary>按 `(倍率 × 属性 + 固定值) × (1 + 治疗加成) × (1 + 受治疗加成)` 求治疗量。</summary>
        public static float EvaluateHealing(in HealingContext context)
        {
            float healing = (context.Multiplier * context.ScalingValue + context.FlatHealing)
                            * (1f + context.HealingBonus)
                            * (1f + context.IncomingHealingBonus);
            return healing > 0f ? healing : 0f;
        }

        /// <summary>按 `(倍率 × 属性 + 固定值) × (1 + 护盾强效)` 求护盾吸收量。</summary>
        public static float EvaluateShield(in ShieldContext context)
        {
            float shield = (context.Multiplier * context.ScalingValue + context.FlatShield) * (1f + context.ShieldStrength);
            return shield > 0f ? shield : 0f;
        }

        /// <summary>求暴击区：暴击时为 `1 + 暴击伤害`，未暴击时为 1。</summary>
        public static float CriticalMultiplier(bool isCritical, float critDamage)
        {
            return isCritical ? 1f + critDamage : 1f;
        }

        /// <summary>
        /// 求防御区 `(攻等 + 100) / ((攻等 + 100) + (守等 + 100) × (1 - 减防) × (1 - 无视防御))`。
        ///
        /// 减防与无视防御是两个**独立相乘**的系数，来源不同（减防挂目标、无视防御挂施加者），
        /// 合并成一个会让两类效果互相稀释。
        /// </summary>
        public static float DefenseMultiplier(int attackerLevel, int targetLevel, float defenseReduction, float defenseIgnore)
        {
            float attacker = attackerLevel + 100f;
            float target = (targetLevel + 100f) * (1f - defenseReduction) * (1f - defenseIgnore);
            if (target < 0f) target = 0f;
            return attacker / (attacker + target);
        }

        /// <summary>
        /// 求抗性区，分三段：
        /// 负抗收益减半防止无限叠减抗；高抗段用倒数做软上限保证高抗敌人不会完全免疫。
        /// </summary>
        /// <param name="resistance">目标对该元素的最终抗性，已扣除全部削减。</param>
        public static float ResistanceMultiplier(float resistance)
        {
            if (resistance < 0f) return 1f - resistance / 2f;
            if (resistance < HighResistanceThreshold) return 1f - resistance;
            return 1f / (4f * resistance + 1f);
        }
    }
}
