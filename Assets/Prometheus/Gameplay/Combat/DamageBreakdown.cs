using System.Text;

namespace Xuan.Prometheus.Combat
{
    /// <summary>
    /// 把一次伤害结算拆成可读的乘区明细。
    ///
    /// 每个乘区都由 `DamageCalculator` 的**同一批公开函数**重新求出，而不是在这里另写一遍算式：
    /// 拆解一旦有自己的实现，它就会在公式调整后悄悄说谎，而说谎的诊断比没有诊断更糟。
    ///
    /// 只在诊断通道有订阅者时才会被调用，因此正式运行不产生任何字符串拼接。
    /// </summary>
    public static class DamageBreakdown
    {
        /// <summary>复用的拼接缓冲；拆解只在主线程的诊断路径上发生。</summary>
        private static readonly StringBuilder Builder = new StringBuilder(256);

        /// <summary>
        /// 描述一次伤害结算的全部乘区。
        ///
        /// 输出形如：
        /// `Pyro 1000.0 base x1.50 bonus x2.00 crit x2.00 VaporizeForward x0.50 def x0.90 res = 2700.0`
        /// </summary>
        public static string Describe(in DamageResolution resolution)
        {
            DamageContext context = resolution.Context;
            Builder.Clear();
            Builder.Append(resolution.Element).Append(' ');

            float baseDamage = context.SkillMultiplier * context.ScalingValue + context.FlatDamage;
            Builder.Append(baseDamage.ToString("0.#")).Append(" base");
            if (context.CatalyzeBonus != 0f) Builder.Append(" +").Append(context.CatalyzeBonus.ToString("0.#")).Append(" catalyze");

            Append(" x{0} bonus", 1f + context.DamageBonus, 1f);
            Append(" x{0} crit", DamageCalculator.CriticalMultiplier(context.IsCritical, context.CritDamage), 1f);

            if (resolution.Reacted && context.AmplifyMultiplier != 1f)
                Builder.Append(" x").Append(context.AmplifyMultiplier.ToString("0.###")).Append(' ').Append(resolution.Reaction.ReactionId);

            Append(" x{0} def", DamageCalculator.DefenseMultiplier(context.AttackerLevel, context.TargetLevel, context.DefenseReduction, context.DefenseIgnore), 1f);
            Append(" x{0} res", DamageCalculator.ResistanceMultiplier(context.TargetResistance), 1f);
            Append(" x{0} reduction", 1f - context.DamageReduction, 1f);

            Builder.Append(" = ").Append(resolution.Damage.ToString("0.#"));

            if (resolution.TransformativeDamage > 0f)
                Builder.Append(" | ").Append(resolution.Reaction.ReactionId).Append(' ').Append(resolution.TransformativeElement)
                       .Append(' ').Append(resolution.TransformativeDamage.ToString("0.#")).Append(" transformative");
            if (resolution.ProductValue > 0f)
                Builder.Append(" | ").Append(resolution.ProductEffectId).Append(' ').Append(resolution.ProductValue.ToString("0.#"));

            return Builder.ToString();
        }

        /// <summary>追加一个乘区；等于中性值的乘区不输出，避免把有效信息淹没在一串 x1.00 里。</summary>
        private static void Append(string format, float value, float neutral)
        {
            if (UnityEngine.Mathf.Approximately(value, neutral)) return;
            Builder.AppendFormat(format, value.ToString("0.###"));
        }
    }
}
