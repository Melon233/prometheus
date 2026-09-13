namespace Xuan.Prometheus.Combat
{
    /// <summary>
    /// 承载一次伤害从产生到落地的全部输入。
    ///
    /// 全部字段都是**已解析的数值**：按哪个属性缩放、目标抗性从哪些 Modifier 汇总而来，
    /// 都是接线层的职责。计算器只做算术，因此可以脱离 Entity、属性组件与配表单独验证。
    /// </summary>
    public struct DamageContext
    {
        /// <summary>攻击者等级；参与防御区。</summary>
        public int AttackerLevel;
        /// <summary>目标等级；参与防御区。</summary>
        public int TargetLevel;

        /// <summary>技能倍率，来自天赋倍率表。</summary>
        public float SkillMultiplier;
        /// <summary>本段伤害所依据的属性值，由接线层按段落的缩放属性解析（攻击力、生命值、防御力或元素精通）。</summary>
        public float ScalingValue;
        /// <summary>不随面板缩放的固定伤害值。</summary>
        public float FlatDamage;

        /// <summary>激化加算；无激化时为 0。它并入基础伤害后一起吃后续全部乘区。</summary>
        public float CatalyzeBonus;

        /// <summary>已聚合的伤害加成区：元素加成、动作加成、全伤害加成与特定目标加成之和。</summary>
        public float DamageBonus;

        /// <summary>本次是否暴击；暴击判定由接线层掷骰，计算器不掷骰以保持纯净。</summary>
        public bool IsCritical;
        /// <summary>暴击伤害。</summary>
        public float CritDamage;

        /// <summary>增幅倍率；无增幅反应时为 1。</summary>
        public float AmplifyMultiplier;

        /// <summary>减防；来自目标身上的减防效果。</summary>
        public float DefenseReduction;
        /// <summary>无视防御；来自施加者。与减防独立相乘。</summary>
        public float DefenseIgnore;

        /// <summary>目标对本次伤害元素的最终抗性，已扣除全部削减；可为负。</summary>
        public float TargetResistance;

        /// <summary>目标的受伤降低；与抗性区独立。</summary>
        public float DamageReduction;
    }

    /// <summary>
    /// 承载一次剧变反应伤害的输入。
    ///
    /// 剧变不走主公式：它不吃攻击力、不吃伤害加成、不能暴击、不吃防御区，
    /// 只由等级系数、反应倍率、元素精通、反应加成与目标抗性决定。
    /// </summary>
    public struct TransformativeDamageContext
    {
        /// <summary>施加者等级对应的反应等级系数，来自 `ReactionLevelCoefficient` 表。</summary>
        public float LevelCoefficient;
        /// <summary>反应倍率，来自 `ReactionMatrix` 表。</summary>
        public float ReactionMultiplier;
        /// <summary>施加者元素精通。</summary>
        public float ElementalMastery;
        /// <summary>该反应的专属加成与全剧变加成之和。</summary>
        public float ReactionBonus;
        /// <summary>精通项的分子系数，来自 `ReactionMatrix` 的 `emCoefficient` 列。</summary>
        public float MasteryCoefficient;
        /// <summary>精通项的分母常数，来自 `ReactionMatrix` 的 `emDenominator` 列。</summary>
        public float MasteryDenominator;
        /// <summary>目标对剧变伤害元素的最终抗性。</summary>
        public float TargetResistance;
    }

    /// <summary>承载一次治疗量的输入。</summary>
    public struct HealingContext
    {
        /// <summary>治疗倍率。</summary>
        public float Multiplier;
        /// <summary>治疗所依据的属性值。</summary>
        public float ScalingValue;
        /// <summary>不随面板缩放的固定治疗量。</summary>
        public float FlatHealing;
        /// <summary>治疗者的治疗加成。</summary>
        public float HealingBonus;
        /// <summary>被治疗者的受治疗加成。</summary>
        public float IncomingHealingBonus;
    }

    /// <summary>承载一次护盾吸收量的输入。</summary>
    public struct ShieldContext
    {
        /// <summary>护盾倍率。</summary>
        public float Multiplier;
        /// <summary>护盾所依据的属性值。</summary>
        public float ScalingValue;
        /// <summary>不随面板缩放的固定护盾量。</summary>
        public float FlatShield;
        /// <summary>护盾强效。</summary>
        public float ShieldStrength;
    }
}
