using System;
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
        [SerializeField] private Sprite buffIcon;
        [SerializeField] private bool showInBuffList = true;
        [SerializeField, Min(0f)] private float duration;
        [SerializeField, Min(0f)] private float tickInterval;
        [SerializeField] private EffectStackPolicy stackPolicy = EffectStackPolicy.Reject;
        [SerializeField] private EffectStackKeyPolicy stackKeyPolicy = EffectStackKeyPolicy.Definition;
        [SerializeField, Min(1)] private int maxStacks = 1;
        [SerializeField] private EffectExecutionPhase phase = EffectExecutionPhase.Apply;
        [SerializeField] private int priority;
        [SerializeReference] private List<EffectOperation> onApplyOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onStackOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onRefreshOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onTickOperations = new List<EffectOperation>();
        [SerializeReference] private List<EffectOperation> onRemoveOperations = new List<EffectOperation>();
        [SerializeField] private List<EffectTriggerDefinition> grantedTriggers = new List<EffectTriggerDefinition>();

        /// <summary>获取稳定效果编号；未配置时使用资产名称作为安全回退。</summary>
        public string EffectId => string.IsNullOrWhiteSpace(effectId) ? name : effectId;

        /// <summary>获取效果标签。</summary>
        public EffectTag Tags => tags;

        /// <summary>获取持续时间类型。</summary>
        public EffectDurationType DurationType => durationType;

        /// <summary>获取持续型 Buff 在 HUD 列表中显示的图标；未配置时 UI 会隐藏该实例的图标图片。</summary>
        public Sprite BuffIcon => buffIcon;

        /// <summary>获取持续效果是否应该进入 HUD Buff 列表；内部养成投影会关闭该显示。</summary>
        public bool ShowInBuffList => showInBuffList;

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

        /// <summary>获取层数实际增加时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnStackOperations => onStackOperations;

        /// <summary>获取有限持续时间实际刷新时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnRefreshOperations => onRefreshOperations;

        /// <summary>获取每次周期 Tick 时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnTickOperations => onTickOperations;

        /// <summary>获取移除实例时执行的操作。</summary>
        public IReadOnlyList<EffectOperation> OnRemoveOperations => onRemoveOperations;

        /// <summary>获取效果存续期间授予拥有者的触发规则。</summary>
        public IReadOnlyList<EffectTriggerDefinition> GrantedTriggers => grantedTriggers;

        /// <summary>创建仅属于当前 Entity 生命周期的运行时 Effect 定义，使动态养成数据仍经过标准 EffectRequest 与 EffectInstance 生命周期。</summary>
        public static EffectDefinition CreateRuntime(string runtimeEffectId, EffectTag runtimeTags, EffectDurationType runtimeDurationType, IEnumerable<EffectOperation> applyOperations, bool runtimeShowInBuffList = false)
        {
            if (string.IsNullOrWhiteSpace(runtimeEffectId)) throw new ArgumentException("Runtime effect ID cannot be empty.", nameof(runtimeEffectId));
            EffectDefinition definition = CreateInstance<EffectDefinition>();
            definition.name = runtimeEffectId.Trim();
            definition.hideFlags = HideFlags.HideAndDontSave;
            definition.effectId = definition.name;
            definition.tags = runtimeTags;
            definition.durationType = runtimeDurationType;
            definition.buffIcon = null;
            definition.showInBuffList = runtimeShowInBuffList;
            definition.duration = 0f;
            definition.tickInterval = 0f;
            definition.stackPolicy = EffectStackPolicy.Reject;
            definition.stackKeyPolicy = EffectStackKeyPolicy.Definition;
            definition.maxStacks = 1;
            definition.phase = EffectExecutionPhase.Apply;
            definition.priority = 0;
            definition.onApplyOperations = applyOperations == null ? new List<EffectOperation>() : new List<EffectOperation>(applyOperations);
            definition.onStackOperations = new List<EffectOperation>();
            definition.onRefreshOperations = new List<EffectOperation>();
            definition.onTickOperations = new List<EffectOperation>();
            definition.onRemoveOperations = new List<EffectOperation>();
            definition.grantedTriggers = new List<EffectTriggerDefinition>();
            return definition;
        }

        /// <summary>只释放由 CreateRuntime 创建并带有 DontSave 标记的临时定义，避免误删正式配置资产。</summary>
        public static void ReleaseRuntime(EffectDefinition definition)
        {
            if (definition == null || (definition.hideFlags & HideFlags.DontSave) == 0) return;
            if (Application.isPlaying) Destroy(definition);
            else DestroyImmediate(definition);
        }

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
