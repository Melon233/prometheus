using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectTriggerDefinition 描述 When、If、Select 和 Then 四部分，不保存冷却等运行时状态。
    /// </summary>
    [Serializable]
    public sealed class EffectTriggerDefinition
    {
        [SerializeField] private string triggerId;
        [SerializeField] private EffectSignalType signalType;
        [SerializeField] private EffectListenScope listenScope = EffectListenScope.Source;
        [SerializeField] private EffectTargetSelector targetSelector = EffectTargetSelector.Target;
        [SerializeField, Range(0f, 1f)] private float chance = 1f;
        [SerializeField, Min(0f)] private float cooldown;
        [SerializeField] private bool oncePerSignalChain = true;
        [SerializeField] private int priority;
        [SerializeField] private List<EffectConditionDefinition> conditions = new List<EffectConditionDefinition>();
        [SerializeField] private List<EffectDefinition> effects = new List<EffectDefinition>();

        /// <summary>获取触发规则编号。</summary>
        public string TriggerId => string.IsNullOrWhiteSpace(triggerId) ? signalType.ToString() : triggerId;

        /// <summary>获取监听的信号类型。</summary>
        public EffectSignalType SignalType => signalType;

        /// <summary>获取相对规则拥有者的监听范围。</summary>
        public EffectListenScope ListenScope => listenScope;

        /// <summary>获取目标选择方式。</summary>
        public EffectTargetSelector TargetSelector => targetSelector;

        /// <summary>获取零到一之间的触发概率。</summary>
        public float Chance => Mathf.Clamp01(chance);

        /// <summary>获取成功触发后的冷却时间。</summary>
        public float Cooldown => Mathf.Max(0f, cooldown);

        /// <summary>获取同一信号因果链中是否最多触发一次。</summary>
        public bool OncePerSignalChain => oncePerSignalChain;

        /// <summary>获取规则附加到效果请求上的优先级。</summary>
        public int Priority => priority;

        /// <summary>获取全部触发条件。</summary>
        public IReadOnlyList<EffectConditionDefinition> Conditions => conditions;

        /// <summary>获取触发成功后请求的效果定义。</summary>
        public IReadOnlyList<EffectDefinition> Effects => effects;

    }
}
