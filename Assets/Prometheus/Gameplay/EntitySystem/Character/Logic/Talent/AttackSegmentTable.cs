using System;
using System.Collections.Generic;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Logic.Talent
{
    /// <summary>
    /// 攻击段落表的查询索引。
    ///
    /// 键是 `(talentId, stageIndex, windowIndex)`：
    /// - `talentId` 同时是 ICD 分组键，因此同一天赋的全部段落**共用一条 ICD**——
    ///   普攻五段共享 ICD 正是原神的行为，段落各自独立分组会让每一段都附着；
    /// - `windowIndex` 是**动画内**的命中窗口序号，由动画事件的 `intValue` 携带。
    ///   用动画内序号而不是表的全局下标，是为了让表重排与插行不会让已有动画事件指错段落。
    ///
    /// 表里**没有时序**：命中窗口何时开合由动画事件决定，见 Docs/Design/Combat/02 第 6.1.1 节。
    /// </summary>
    public sealed class AttackSegmentTable
    {
        /// <summary>按 (天赋, 连段, 窗口) 索引的段落行。</summary>
        private readonly Dictionary<(string TalentId, int StageIndex, int WindowIndex), Cfg.AttackSegmentRow> byKey;
        /// <summary>按天赋记录已配置的连段数，供连段循环判定使用。</summary>
        private readonly Dictionary<string, int> stageCountByTalent = new Dictionary<string, int>();

        /// <summary>从已加载的表建立索引。</summary>
        public AttackSegmentTable(Cfg.Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));

            IReadOnlyList<Cfg.AttackSegmentRow> rows = tables.TbAttackSegment.DataList;
            byKey = new Dictionary<(string, int, int), Cfg.AttackSegmentRow>(rows.Count);
            foreach (Cfg.AttackSegmentRow row in rows)
            {
                (string, int, int) key = (row.TalentId, row.StageIndex, row.WindowIndex);
                if (byKey.ContainsKey(key)) throw new InvalidOperationException($"TbAttackSegment has duplicate rows for talent '{row.TalentId}' stage {row.StageIndex} window {row.WindowIndex}.");
                byKey.Add(key, row);
                stageCountByTalent[row.TalentId] = Math.Max(stageCountByTalent.TryGetValue(row.TalentId, out int count) ? count : 0, row.StageIndex + 1);
            }
        }

        /// <summary>查询一段攻击的战斗数据；未配置时返回 false。</summary>
        public bool TryGet(string talentId, int stageIndex, int windowIndex, out Cfg.AttackSegmentRow row)
        {
            return byKey.TryGetValue((talentId ?? string.Empty, stageIndex, windowIndex), out row);
        }

        /// <summary>
        /// 查询一段攻击的战斗数据；未配置时抛出。
        ///
        /// 段落缺失是**配表错误**而不是运行时的正常分支：缺一行的后果是这一段既不附着也不打断，
        /// 而伤害照常结算，因此静默跳过会在实机里表现为「某一段手感不对」，几乎无法反查。
        /// </summary>
        public Cfg.AttackSegmentRow Get(string talentId, int stageIndex, int windowIndex)
        {
            if (TryGet(talentId, stageIndex, windowIndex, out Cfg.AttackSegmentRow row)) return row;
            throw new InvalidOperationException($"TbAttackSegment has no row for talent '{talentId}' stage {stageIndex} window {windowIndex}.");
        }

        /// <summary>获取某个天赋已配置的连段数；未配置该天赋时为 0。</summary>
        public int GetStageCount(string talentId)
        {
            return talentId != null && stageCountByTalent.TryGetValue(talentId, out int count) ? count : 0;
        }
    }
}
