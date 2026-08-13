using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Xuan.Prometheus.Growth
{
    /// <summary>作为只读 ScriptableObject 保存一件装备的稳定编号和静态词条定义，不承载经验、等级或当前词条值。</summary>
    [CreateAssetMenu(menuName = "Prometheus/Growth/Equipment Definition", fileName = "EquipmentDefinition")]
    public sealed class EquipmentDefinition : ScriptableObject
    {
        /// <summary>配置存档、诊断和 Debug 使用的稳定装备编号。</summary>
        [SerializeField, FormerlySerializedAs("equipmentId")] private string definitionId;
        /// <summary>配置一个主词条和不超过 EquipmentConfig 上限的副词条。</summary>
        [SerializeField] private List<TierDefinition> tiers = new List<TierDefinition>();

        /// <summary>获取稳定装备定义编号。</summary>
        public string DefinitionId => string.IsNullOrWhiteSpace(definitionId) ? "Equipment" : definitionId.Trim();

        /// <summary>获取只读词条定义列表；EquipmentComponent 初始化时会把每条定义复制为运行时 TierInstance。</summary>
        public IReadOnlyList<TierDefinition> Tiers => tiers ?? (tiers = new List<TierDefinition>());

        /// <summary>在 Inspector 修改时补齐非空词条定义集合。</summary>
        private void OnValidate()
        {
            if (tiers == null) tiers = new List<TierDefinition>();
        }
    }
}
