using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectTriggerSet 将一组可复用触发规则保存为资产，供技能、天赋、装备或关卡注册。
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Effect System/Trigger Set", fileName = "EffectTriggerSet")]
    public sealed class EffectTriggerSet : ScriptableObject
    {
        [SerializeField] private List<EffectTriggerDefinition> triggers = new List<EffectTriggerDefinition>();

        /// <summary>获取触发规则只读列表。</summary>
        public IReadOnlyList<EffectTriggerDefinition> Triggers => triggers;

    }
}
