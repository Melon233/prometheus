using System;
using System.Collections.Generic;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Elements
{
    /// <summary>
    /// 一次元素施加的结果。
    ///
    /// 只携带**反应的身份与配表参数**，不做任何数值求值：元素精通、反应加成与等级系数
    /// 属于伤害管线的输入，求值由 `DamageCalculator` / `ReactionFormula` 这两个纯函数完成。
    /// 这样元素系统不需要读取施加者的属性面板，反应规则与数值公式各自独立可测。
    /// </summary>
    public readonly struct ReactionResult
    {
        /// <summary>表示本次施加没有触发任何反应。</summary>
        public static readonly ReactionResult None = default;

        /// <summary>获取本次是否触发了反应。</summary>
        public bool Triggered => Row != null;

        /// <summary>获取命中的反应行；未触发时为空。</summary>
        public readonly Cfg.ReactionMatrixRow Row;

        /// <summary>获取本次施加是否真的写入了附着；被 ICD 挡下时为 false。</summary>
        public readonly bool Applied;

        /// <summary>获取本次反应消耗掉的附着量。</summary>
        public readonly float ConsumedGauge;

        /// <summary>创建一次施加结果。</summary>
        internal ReactionResult(Cfg.ReactionMatrixRow row, bool applied, float consumedGauge)
        {
            Row = row;
            Applied = applied;
            ConsumedGauge = consumedGauge;
        }
    }

    /// <summary>
    /// 反应矩阵的查询索引。
    ///
    /// 表按 `(触发元素, 目标附着)` 定位唯一一行，且**顺序敏感**——火打水与水打火是两行。
    /// 目标身上同时存在多个可反应附着时按 `priority` 降序取第一条：单次攻击只触发一种反应。
    /// </summary>
    public sealed class ReactionMatrixIndex
    {
        /// <summary>按 (触发元素, 附着键) 索引的反应行。</summary>
        private readonly Dictionary<(Cfg.ElementType Trigger, Cfg.AuraKey Aura), Cfg.ReactionMatrixRow> byPair;
        /// <summary>按触发元素分组、已按优先级降序排好的候选行，用于在多附着时快速定位。</summary>
        private readonly Dictionary<Cfg.ElementType, List<Cfg.ReactionMatrixRow>> byTrigger;

        /// <summary>从已加载的表建立索引。</summary>
        public ReactionMatrixIndex(Cfg.Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));

            IReadOnlyList<Cfg.ReactionMatrixRow> rows = tables.TbReactionMatrix.DataList;
            byPair = new Dictionary<(Cfg.ElementType, Cfg.AuraKey), Cfg.ReactionMatrixRow>(rows.Count);
            byTrigger = new Dictionary<Cfg.ElementType, List<Cfg.ReactionMatrixRow>>();

            foreach (Cfg.ReactionMatrixRow row in rows)
            {
                (Cfg.ElementType, Cfg.AuraKey) pair = (row.TriggerElement, row.AuraElement);
                if (byPair.ContainsKey(pair)) throw new InvalidOperationException($"TbReactionMatrix has duplicate rows for trigger '{row.TriggerElement}' on aura '{row.AuraElement}'.");
                byPair.Add(pair, row);

                if (!byTrigger.TryGetValue(row.TriggerElement, out List<Cfg.ReactionMatrixRow> candidates))
                {
                    candidates = new List<Cfg.ReactionMatrixRow>();
                    byTrigger.Add(row.TriggerElement, candidates);
                }
                candidates.Add(row);
            }

            // 预先按优先级降序排好，使每次查询不必重复排序。
            foreach (List<Cfg.ReactionMatrixRow> candidates in byTrigger.Values)
                candidates.Sort((left, right) => right.Priority.CompareTo(left.Priority));
        }

        /// <summary>查询一对 (触发元素, 附着) 是否构成反应。</summary>
        public bool TryGet(Cfg.ElementType trigger, Cfg.AuraKey aura, out Cfg.ReactionMatrixRow row)
        {
            return byPair.TryGetValue((trigger, aura), out row);
        }

        /// <summary>
        /// 在目标当前全部附着中，按优先级取出唯一一条应当触发的反应。
        /// 目标同时带水与雷时，火命中优先触发蒸发而非超载，正是由这里的优先级决定。
        /// </summary>
        /// <param name="trigger">本次攻击携带的元素。</param>
        /// <param name="auraSet">目标当前的附着集合。</param>
        /// <param name="row">命中的反应行。</param>
        /// <returns>是否存在可触发的反应。</returns>
        public bool TryResolve(Cfg.ElementType trigger, ElementAuraSet auraSet, out Cfg.ReactionMatrixRow row)
        {
            row = null;
            if (auraSet == null || auraSet.Count == 0) return false;
            if (!byTrigger.TryGetValue(trigger, out List<Cfg.ReactionMatrixRow> candidates)) return false;

            for (int index = 0; index < candidates.Count; index++)
            {
                Cfg.ReactionMatrixRow candidate = candidates[index];
                if (!auraSet.TryGet(candidate.AuraElement, out _)) continue;
                row = candidate;
                return true;
            }
            return false;
        }
    }
}
