using System;
using System.Collections.Generic;
using Cfg = Prometheus.Config;

namespace Xuan.Prometheus.Growth
{
    /// <summary>
    /// 角色养成三张表的复合键索引。
    ///
    /// `TbCharacterLevelCurve` 与 `TbCharacterAscension` 需要两个键（曲线+等级、角色+阶段），
    /// 而 Luban 不支持复合主键，因此这两张表以 `mode = list` 导出，索引在这里建一次。
    /// 见 Docs/Workflow/ConfigPipeline.md 第 3.4 节。
    /// </summary>
    public sealed class CharacterGrowthTables
    {
        /// <summary>按角色标识索引的静态定义。</summary>
        private readonly Dictionary<string, Cfg.CharacterBaseRow> characters;
        /// <summary>按 (曲线, 等级) 索引的逐级成长数据。</summary>
        private readonly Dictionary<(string CurveId, int Level), Cfg.CharacterLevelCurveRow> levels;
        /// <summary>按 (角色, 阶段) 索引的突破数据。</summary>
        private readonly Dictionary<(string CharacterId, int Phase), Cfg.CharacterAscensionRow> ascensions;
        /// <summary>按角色标识记录其最大突破阶段，避免每次查询遍历。</summary>
        private readonly Dictionary<string, int> maximumPhases;

        /// <summary>从已加载的表构造索引。</summary>
        public CharacterGrowthTables(Cfg.Tables tables)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));

            characters = new Dictionary<string, Cfg.CharacterBaseRow>(tables.TbCharacterBase.DataList.Count);
            foreach (Cfg.CharacterBaseRow row in tables.TbCharacterBase.DataList) characters[row.CharacterId] = row;

            levels = new Dictionary<(string, int), Cfg.CharacterLevelCurveRow>(tables.TbCharacterLevelCurve.DataList.Count);
            foreach (Cfg.CharacterLevelCurveRow row in tables.TbCharacterLevelCurve.DataList) levels[(row.CurveId, row.Level)] = row;

            ascensions = new Dictionary<(string, int), Cfg.CharacterAscensionRow>(tables.TbCharacterAscension.DataList.Count);
            maximumPhases = new Dictionary<string, int>();
            foreach (Cfg.CharacterAscensionRow row in tables.TbCharacterAscension.DataList)
            {
                ascensions[(row.CharacterId, row.Phase)] = row;
                maximumPhases.TryGetValue(row.CharacterId, out int recorded);
                if (row.Phase > recorded) maximumPhases[row.CharacterId] = row.Phase;
            }
        }

        /// <summary>获取角色静态定义；配表缺失是配置错误，直接抛出而不回退默认值。</summary>
        public Cfg.CharacterBaseRow GetCharacter(string characterId)
        {
            return characters.TryGetValue(characterId, out Cfg.CharacterBaseRow row)
                ? row
                : throw new InvalidOperationException($"TbCharacterBase has no row for character '{characterId}'.");
        }

        /// <summary>获取指定曲线在指定等级的成长数据。</summary>
        public Cfg.CharacterLevelCurveRow GetLevel(string curveId, int level)
        {
            return levels.TryGetValue((curveId, level), out Cfg.CharacterLevelCurveRow row)
                ? row
                : throw new InvalidOperationException($"TbCharacterLevelCurve has no row for curve '{curveId}' at level {level}.");
        }

        /// <summary>获取指定角色指定突破阶段的数据。</summary>
        public Cfg.CharacterAscensionRow GetAscension(string characterId, int phase)
        {
            return ascensions.TryGetValue((characterId, phase), out Cfg.CharacterAscensionRow row)
                ? row
                : throw new InvalidOperationException($"TbCharacterAscension has no row for character '{characterId}' at phase {phase}.");
        }

        /// <summary>获取指定角色的最大突破阶段；没有配置突破的角色返回 0。</summary>
        public int GetMaximumPhase(string characterId)
        {
            return maximumPhases.TryGetValue(characterId, out int phase) ? phase : 0;
        }
    }
}
