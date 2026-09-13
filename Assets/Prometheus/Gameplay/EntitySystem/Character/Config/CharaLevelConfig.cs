using UnityEngine;

namespace Xuan.Prometheus.Growth
{
    /// <summary>
    /// 把角色 Prefab 关联到配表中的角色标识。
    ///
    /// 等级上限、成长系数、经验与摩拉消耗、突破阶段全部来自 `TbCharacterBase` /
    /// `TbCharacterLevelCurve` / `TbCharacterAscension` 三张表，本 SO 不再持有任何成长数值——
    /// 数值配在表里，策划改表即可，不需要为每个角色维护一份 ScriptableObject。
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Growth/Chara Level Config", fileName = "CharaLevelConfig")]
    public sealed class CharaLevelConfig : ScriptableObject
    {
        /// <summary>配置该角色在 `TbCharacterBase` 中的主键。</summary>
        [SerializeField] private string characterId = "Yefa";

        /// <summary>获取角色标识。</summary>
        public string CharacterId => characterId;

        /// <summary>在 Inspector 修改时阻止留空角色标识——空标识会在查表时才暴露，排查成本更高。</summary>
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(characterId)) characterId = "Yefa";
        }
    }
}
