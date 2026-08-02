using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectDefinition 只保存不可变配置，持续时间、层数和句柄等状态由 EffectInstance 保存。
    /// </summary>
    [CreateAssetMenu(menuName = "Prometheus/Effect System/Effect Definition", fileName = "EffectDefinition")]
    public sealed class EffectDefinition : ScriptableObject
    {
        [SerializeField] private string effectId;
        [SerializeField] private EffectTag tags;
        [SerializeField] private EffectDurationType durationType = EffectDurationType.Instant;
        [SerializeField, Min(0f)] private float duration;
        [SerializeField, Min(0f)] private float tickInterval;
        [SerializeField] private EffectStackPolicy stackPolicy = EffectStackPolicy.Reject;
        [SerializeField] private EffectStackKeyPolicy stackKeyPolicy = EffectStackKeyPolicy.Definition;
        [SerializeField, Min(1)] private int maxStacks = 1;
        [SerializeField] private EffectExecutionPhase phase = EffectExecutionPhase.Apply;
        [SerializeField] private int priority;
        [SerializeReference] private List<EffectOperation> onApplyOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onStackOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onTickOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onRemoveOperations = new List<EffectOperation>();
        [SerializeField] private List<EffectTriggerDefinition> grantedTriggers = new List<EffectTriggerDefinition>();

        /// <summary>获取稳定效果编号；未配置时使用资产名称作为安全回退。</summary>
        public string EffectId => string.IsNullOrWhiteSpace(effectId) ? name : effectId;

        /// <summary>获取效果标签。</summary>
        public EffectTag Tags => tags;

        /// <summary>获取持续时间类型。</summary>
        public EffectDurationType DurationType => durationType;

        /// <summary>获取配置持续时间。</summary>
        public float Duration => duration;

        /// <summary>获取周期执行间隔；零表示没有 Tick。</summary>
        public float TickInterval => tickInterval;

        /// <summary>获取堆叠策略。</summary>
        public EffectStackPolicy StackPolicy => stackPolicy;

        /// <summary>获取堆叠键策略。</summary>
        public EffectStackKeyPolicy StackKeyPolicy => stackKeyPolicy;

        /// <summary>获取最大层数。</summary>
        public int MaxStacks => Mathf.Max(1, maxStacks);

        /// <summary>获取请求执行阶段。</summary>
        public EffectExecutionPhase Phase => phase;

        /// <summary>获取同阶段内的基础优先级。</summary>
        public int Priority => priority;

        /// <summary>获取首次应用时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnApplyOperations => onApplyOperations;

        /// <summary>获取叠层或刷新时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnStackOperations => onStackOperations;

        /// <summary>获取每次周期 Tick 时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnTickOperations => onTickOperations;

        /// <summary>获取移除实例时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnRemoveOperations => onRemoveOperations;

        /// <summary>获取效果存续期间授予拥有者的触发规则。</summary>
        public IReadOnlyList<EffectTriggerDefinition> GrantedTriggers => grantedTriggers;

        /// <summary>
        /// 在 Inspector 修改配置时约束时间和层数，防止产生无法运行的定义。
        /// </summary>
        private void OnValidate()
        {
            duration = Mathf.Max(0f, duration);
            tickInterval = Mathf.Max(0f, tickInterval);
            maxStacks = Mathf.Max(1, maxStacks);
        }
    }
}
