using NUnit.Framework;

namespace Xuan.Prometheus.Combat.Tests
{
    /// <summary>
    /// 按 Docs/Design/Combat/05 第 8 节的验收用例验证伤害公式。
    /// 全部用例都是纯算术，不构造 Entity、不读配表、不碰属性组件。
    /// </summary>
    public sealed class DamageCalculatorTests
    {
        /// <summary>构造一份只有基础伤害、其余乘区全部中性的上下文，使每个用例只改动它关心的那一项。</summary>
        private static DamageContext Neutral(float skillMultiplier = 1f, float scalingValue = 1000f)
        {
            return new DamageContext
            {
                AttackerLevel = 90,
                TargetLevel = 90,
                SkillMultiplier = skillMultiplier,
                ScalingValue = scalingValue,
                AmplifyMultiplier = 1f
            };
        }

        // ---------- D-01 ~ D-04：防御区与抗性区 ----------

        [Test]
        public void DefenseMultiplier_AtEqualLevelWithoutReduction_IsOneHalf()
        {
            Assert.That(DamageCalculator.DefenseMultiplier(90, 90, 0f, 0f), Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ResistanceMultiplier_WithPositiveResistance_SubtractsDirectly()
        {
            Assert.That(DamageCalculator.ResistanceMultiplier(0.1f), Is.EqualTo(0.9f).Within(0.0001f));
        }

        [Test]
        public void ResistanceMultiplier_WithNegativeResistance_HalvesTheBenefit()
        {
            // 负抗收益减半，防止无限叠减抗。
            Assert.That(DamageCalculator.ResistanceMultiplier(-0.2f), Is.EqualTo(1.1f).Within(0.0001f));
        }

        [Test]
        public void ResistanceMultiplier_WithHighResistance_UsesSoftCap()
        {
            // 高抗段改用倒数形式，保证高抗敌人不会完全免疫。
            Assert.That(DamageCalculator.ResistanceMultiplier(0.9f), Is.EqualTo(1f / 4.6f).Within(0.0001f));
        }

        [Test]
        public void ResistanceMultiplier_IsContinuousAtSoftCapBoundary()
        {
            // 0.75 是分段边界：线性段给 0.25，倒数段给 1/4，两者必须相等，否则边界两侧会出现数值跳变。
            Assert.That(DamageCalculator.ResistanceMultiplier(0.75f), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(DamageCalculator.ResistanceMultiplier(0.7499f), Is.EqualTo(0.2501f).Within(0.0002f));
        }

        // ---------- D-05：伤害加成区是一个乘区 ----------

        [Test]
        public void DamageBonus_IsSingleAdditiveZone()
        {
            DamageContext context = Neutral();
            context.DamageBonus = 0.466f + 0.30f;

            float damage = DamageCalculator.Evaluate(in context);
            float expected = 1000f * 1.766f * 0.5f;

            Assert.That(damage, Is.EqualTo(expected).Within(0.01f), "火伤 46.6% 与重击 30% 相加为一个乘区，而非相乘。");
            Assert.That(damage, Is.Not.EqualTo(1000f * 1.466f * 1.30f * 0.5f).Within(0.01f));
        }

        // ---------- D-07：增幅反应 ----------

        [Test]
        public void Amplifying_WithMastery_MatchesFormula()
        {
            // 2.0 × (1 + 2.78 × 200 / 1600) = 2.695
            float multiplier = ReactionFormula.Amplifying(2.0f, 200f, 0f);
            Assert.That(multiplier, Is.EqualTo(2.695f).Within(0.001f));
        }

        [Test]
        public void Amplifying_WithZeroMastery_EqualsBaseMultiplier()
        {
            Assert.That(ReactionFormula.Amplifying(1.5f, 0f, 0f), Is.EqualTo(1.5f).Within(0.0001f));
        }

        // ---------- D-08：剧变不吃面板 ----------

        [Test]
        public void Transformative_IgnoresAttackAndCritical()
        {
            TransformativeDamageContext context = new TransformativeDamageContext
            {
                LevelCoefficient = 1446.9f,
                ReactionMultiplier = 2.0f,
                ElementalMastery = 0f,
                ReactionBonus = 0f,
                MasteryCoefficient = ReactionFormula.TransformativeCoefficient,
                MasteryDenominator = ReactionFormula.TransformativeDenominator,
                TargetResistance = 0.1f
            };

            float damage = DamageCalculator.EvaluateTransformative(in context);
            float expected = 1446.9f * 2.0f * 1f * 0.9f;

            Assert.That(damage, Is.EqualTo(expected).Within(0.01f), "剧变只由等级系数、反应倍率、精通与抗性决定。");
        }

        [Test]
        public void Transformative_MasteryTermMatchesFormula()
        {
            // 1 + 16 × 1000 / 3000 = 6.3333
            Assert.That(ReactionFormula.TransformativeScale(1000f, 0f), Is.EqualTo(6.33333f).Within(0.0001f));
        }

        // ---------- D-09：激化是加算，参与后续全部乘区 ----------

        [Test]
        public void Catalyze_IsAddedToBaseAndScaledByEveryZone()
        {
            DamageContext withoutCatalyze = Neutral();
            withoutCatalyze.DamageBonus = 0.5f;
            withoutCatalyze.IsCritical = true;
            withoutCatalyze.CritDamage = 1.0f;

            DamageContext withCatalyze = withoutCatalyze;
            withCatalyze.CatalyzeBonus = 500f;

            float plain = DamageCalculator.Evaluate(in withoutCatalyze);
            float catalyzed = DamageCalculator.Evaluate(in withCatalyze);

            // 加算项 500 与基础伤害同等地吃增伤、暴击与防御区，因此增量为 500 × 1.5 × 2 × 0.5。
            Assert.That(catalyzed - plain, Is.EqualTo(500f * 1.5f * 2f * 0.5f).Within(0.01f));
        }

        // ---------- D-10：减防与无视防御独立相乘 ----------

        [Test]
        public void DefenseReductionAndIgnore_MultiplyIndependently()
        {
            float multiplier = DamageCalculator.DefenseMultiplier(90, 90, 0.3f, 0.2f);
            float expected = 190f / (190f + 190f * 0.7f * 0.8f);

            Assert.That(multiplier, Is.EqualTo(expected).Within(0.0001f));
            Assert.That(multiplier, Is.Not.EqualTo(DamageCalculator.DefenseMultiplier(90, 90, 0.5f, 0f)).Within(0.0001f), "两者相乘与相加结果必须不同。");
        }

        [Test]
        public void DefenseMultiplier_WithFullIgnore_RemovesTargetDefence()
        {
            Assert.That(DamageCalculator.DefenseMultiplier(90, 90, 0f, 1f), Is.EqualTo(1f).Within(0.0001f));
        }

        // ---------- 乘区顺序与整体 ----------

        [Test]
        public void Evaluate_AppliesEveryZoneInOrder()
        {
            DamageContext context = new DamageContext
            {
                AttackerLevel = 90,
                TargetLevel = 90,
                SkillMultiplier = 2.0f,
                ScalingValue = 2000f,
                FlatDamage = 100f,
                CatalyzeBonus = 0f,
                DamageBonus = 0.466f,
                IsCritical = true,
                CritDamage = 1.5f,
                AmplifyMultiplier = 2.0f,
                DefenseReduction = 0f,
                DefenseIgnore = 0f,
                TargetResistance = 0.1f,
                DamageReduction = 0f
            };

            float expected = (2.0f * 2000f + 100f) * 1.466f * 2.5f * 2.0f * 0.5f * 0.9f;

            Assert.That(DamageCalculator.Evaluate(in context), Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void Evaluate_WithoutCritical_DoesNotApplyCritDamage()
        {
            DamageContext context = Neutral();
            context.CritDamage = 5f;

            Assert.That(DamageCalculator.Evaluate(in context), Is.EqualTo(1000f * 0.5f).Within(0.01f), "未暴击时暴击伤害不参与计算。");
        }

        [Test]
        public void Evaluate_WithFullDamageReduction_YieldsZero()
        {
            DamageContext context = Neutral();
            context.DamageReduction = 1f;

            Assert.That(DamageCalculator.Evaluate(in context), Is.Zero);
        }

        [Test]
        public void Evaluate_NeverReturnsNegative()
        {
            // 减伤配过头是配表错误，但结算不应当因此产生负伤害（等同于治疗）。
            DamageContext context = Neutral();
            context.DamageReduction = 1.5f;

            Assert.That(DamageCalculator.Evaluate(in context), Is.Zero);
        }

        // ---------- 治疗与护盾 ----------

        [Test]
        public void EvaluateHealing_AppliesBothHealingBonuses()
        {
            HealingContext context = new HealingContext
            {
                Multiplier = 0.1f,
                ScalingValue = 30000f,
                FlatHealing = 500f,
                HealingBonus = 0.3f,
                IncomingHealingBonus = 0.2f
            };

            float expected = (0.1f * 30000f + 500f) * 1.3f * 1.2f;

            Assert.That(DamageCalculator.EvaluateHealing(in context), Is.EqualTo(expected).Within(0.01f));
        }

        [Test]
        public void EvaluateShield_AppliesShieldStrength()
        {
            ShieldContext context = new ShieldContext
            {
                Multiplier = 0.2f,
                ScalingValue = 20000f,
                FlatShield = 1000f,
                ShieldStrength = 0.15f
            };

            Assert.That(DamageCalculator.EvaluateShield(in context), Is.EqualTo((0.2f * 20000f + 1000f) * 1.15f).Within(0.01f));
        }

        [Test]
        public void CrystallizeShield_MatchesFormula()
        {
            // 等级系数 1446.9 × 1.5 × (1 + 4.44 × 200 / 1600)
            float expected = 1446.9f * 1.5f * (1f + 4.44f * 200f / 1600f);
            Assert.That(ReactionFormula.CrystallizeShield(1446.9f, 200f), Is.EqualTo(expected).Within(0.01f));
        }

        // ---------- 纯函数性质 ----------

        [Test]
        public void Evaluate_IsDeterministic()
        {
            DamageContext context = Neutral(2f, 1500f);
            context.DamageBonus = 0.3f;
            context.IsCritical = true;
            context.CritDamage = 0.8f;

            float first = DamageCalculator.Evaluate(in context);
            float second = DamageCalculator.Evaluate(in context);

            Assert.That(second, Is.EqualTo(first), "相同输入必须恒得相同输出；掷骰由调用方完成，计算器内不得有随机。");
        }
    }
}
