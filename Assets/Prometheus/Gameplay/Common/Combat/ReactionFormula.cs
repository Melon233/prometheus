namespace Xuan.Prometheus.Combat
{
    /// <summary>
    /// 元素精通对三类反应的影响。
    ///
    /// 三类反应的精通项共用同一形式 `系数 × EM / (EM + 常数)`，
    /// 只是系数与常数不同；这两个参数配在 `ReactionMatrix` 的 `emCoefficient` 与 `emDenominator` 两列，
    /// 因此调整精通曲线只需改表。这里的常量只是三类反应的默认取值，供没有配表上下文的调用方使用。
    /// </summary>
    public static class ReactionFormula
    {
        /// <summary>增幅反应的精通分子系数。</summary>
        public const float AmplifyingCoefficient = 2.78f;
        /// <summary>增幅反应的精通分母常数。</summary>
        public const float AmplifyingDenominator = 1400f;

        /// <summary>剧变反应的精通分子系数。</summary>
        public const float TransformativeCoefficient = 16f;
        /// <summary>剧变反应的精通分母常数。</summary>
        public const float TransformativeDenominator = 2000f;

        /// <summary>激化反应的精通分子系数。</summary>
        public const float CatalyzeCoefficient = 5f;
        /// <summary>激化反应的精通分母常数。</summary>
        public const float CatalyzeDenominator = 1200f;

        /// <summary>结晶护盾的精通分子系数。</summary>
        public const float CrystallizeCoefficient = 4.44f;
        /// <summary>结晶护盾的精通分母常数。</summary>
        public const float CrystallizeDenominator = 1400f;
        /// <summary>结晶护盾的固定倍率。</summary>
        public const float CrystallizeMultiplier = 1.5f;

        /// <summary>
        /// 求元素精通项 `系数 × EM / (EM + 常数)`。
        /// 精通为 0 时返回 0，因此各反应公式中的 `(1 + 精通项 + 反应加成)` 退化为 `(1 + 反应加成)`。
        /// </summary>
        /// <param name="elementalMastery">元素精通；负值按 0 处理。</param>
        /// <param name="coefficient">精通分子系数。</param>
        /// <param name="denominator">精通分母常数。</param>
        public static float MasteryTerm(float elementalMastery, float coefficient, float denominator)
        {
            float mastery = elementalMastery > 0f ? elementalMastery : 0f;
            // 系数或分母为 0 表示该反应**没有配精通曲线**（冻结、原激化这类行就是如此）。
            // 必须在这里显式返回 0：否则 mastery 也为 0 时会算出 0/0 = NaN，
            // 而 NaN 会被后续的伤害钳制悄悄吞成 0，变成一个无处可查的静默失败。
            if (coefficient <= 0f || mastery + denominator <= 0f) return 0f;
            return coefficient * mastery / (mastery + denominator);
        }

        /// <summary>
        /// 求增幅反应的最终倍率 `基础倍率 × (1 + 精通项 + 反应加成)`。
        /// </summary>
        /// <param name="baseMultiplier">正向 2.0 或逆向 1.5，来自 `ReactionMatrix`。</param>
        /// <param name="elementalMastery">施加者元素精通。</param>
        /// <param name="reactionBonus">该反应的专属加成。</param>
        public static float Amplifying(float baseMultiplier, float elementalMastery, float reactionBonus)
        {
            return Amplifying(baseMultiplier, elementalMastery, reactionBonus, AmplifyingCoefficient, AmplifyingDenominator);
        }

        /// <summary>
        /// 求增幅反应的最终倍率，精通系数由调用方给出。
        /// 接线层应当走这个重载并传入配表的 `emCoefficient` / `emDenominator`，
        /// 使精通曲线只改表就能生效；上面的无系数重载只是没有配表上下文时的默认取值。
        /// </summary>
        public static float Amplifying(float baseMultiplier, float elementalMastery, float reactionBonus, float coefficient, float denominator)
        {
            return baseMultiplier * (1f + MasteryTerm(elementalMastery, coefficient, denominator) + reactionBonus);
        }

        /// <summary>求剧变反应公式中的 `(1 + 精通项 + 反应加成)` 部分。</summary>
        public static float TransformativeScale(float elementalMastery, float reactionBonus)
        {
            return Scale(elementalMastery, reactionBonus, TransformativeCoefficient, TransformativeDenominator);
        }

        /// <summary>
        /// 求 `(1 + 精通项 + 反应加成)`，精通系数由调用方给出。
        /// 剧变、激化与结晶三类反应的这一项形状完全相同，只有系数不同，因此共用一个入口。
        /// </summary>
        public static float Scale(float elementalMastery, float reactionBonus, float coefficient, float denominator)
        {
            return 1f + MasteryTerm(elementalMastery, coefficient, denominator) + reactionBonus;
        }

        /// <summary>
        /// 求激化反应的加算值 `等级系数 × 激化倍率 × (1 + 精通项 + 反应加成)`。
        /// 该值并入基础伤害，随后与基础伤害一起吃增伤、暴击、防御区与抗性区。
        /// </summary>
        /// <param name="levelCoefficient">施加者等级对应的反应等级系数。</param>
        /// <param name="catalyzeMultiplier">超激化 1.15 或蔓激化 1.25。</param>
        /// <param name="elementalMastery">施加者元素精通。</param>
        /// <param name="reactionBonus">该反应的专属加成。</param>
        public static float Catalyze(float levelCoefficient, float catalyzeMultiplier, float elementalMastery, float reactionBonus)
        {
            return Catalyze(levelCoefficient, catalyzeMultiplier, elementalMastery, reactionBonus, CatalyzeCoefficient, CatalyzeDenominator);
        }

        /// <summary>求激化反应的加算值，精通系数由调用方给出。</summary>
        public static float Catalyze(float levelCoefficient, float catalyzeMultiplier, float elementalMastery, float reactionBonus, float coefficient, float denominator)
        {
            return levelCoefficient * catalyzeMultiplier * Scale(elementalMastery, reactionBonus, coefficient, denominator);
        }

        /// <summary>求结晶护盾的吸收量 `等级系数 × 1.5 × (1 + 精通项)`。</summary>
        public static float CrystallizeShield(float levelCoefficient, float elementalMastery)
        {
            return levelCoefficient * CrystallizeMultiplier * (1f + MasteryTerm(elementalMastery, CrystallizeCoefficient, CrystallizeDenominator));
        }
    }
}
