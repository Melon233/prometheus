using System;
using UnityEngine;
using UnityEngine.Serialization;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// 标识战斗流程中可以驱动触发规则的信号类型。
    /// </summary>
    public enum EffectSignalType
    {
        Manual,
        HitConfirmed,
        DamageApplied,
        Healed,
        Killed,
        EffectApplied,
        EffectStacked,
        EffectRemoved,
        PeriodicTick,
        CoreEnergyGain,
    }

    /// <summary>
    /// 使用位标记描述信号和效果的语义，便于条件进行零分配的标签匹配。
    /// </summary>
    [Flags]
    public enum EffectTag
    {
        None = 0,
        Attack = 1 << 0,
        NormalAttack = 1 << 1,
        Skill = 1 << 2,
        Fire = 1 << 3,
        Dot = 1 << 4,
        Periodic = 1 << 5,
        Critical = 1 << 6,
        Healing = 1 << 7,
        Buff = 1 << 8,
        Debuff = 1 << 9,
        Attribute = 1 << 10,
        Control = 1 << 11,
        CoreEnergyGain = 1 << 12,
        UltEnergyGain = 1 << 13,
    }

    /// <summary>
    /// 指定触发规则相对于规则拥有者监听信号中的哪个角色。
    /// </summary>
    public enum EffectListenScope
    {
        Source,
        Target,
        Caster,
        Any
    }

    /// <summary>
    /// 指定触发后产生的效果应该选择信号中的哪个实体作为目标。
    /// </summary>
    public enum EffectTargetSelector
    {
        Source,
        Target,
        Caster
    }

    /// <summary>
    /// 指定效果是立即执行、持续一段时间，还是永久存在直到主动移除。
    /// </summary>
    public enum EffectDurationType
    {
        Instant,
        Duration,
        Permanent
    }

    /// <summary>
    /// 指定目标已经持有相同效果时的处理策略。
    /// </summary>
    public enum EffectStackPolicy
    {
        Independent,
        Reject,
        RefreshDuration,
        AddStack,
        AddStackAndRefreshDuration,
        Replace
    }

    /// <summary>
    /// 指定判断两个效果实例是否属于同一堆叠组时使用的键。
    /// </summary>
    public enum EffectStackKeyPolicy
    {
        Definition,
        DefinitionAndCaster,
        DefinitionAndSource
    }

    /// <summary>
    /// 指定效果请求在一次事务中的执行阶段。
    /// </summary>
    public enum EffectExecutionPhase
    {
        Validate = 0,
        BeforeCalculate = 100,
        Calculate = 200,
        BeforeApply = 300,
        Apply = 400,
        AfterApply = 500,
        Presentation = 600
    }

    /// <summary>
    /// 描述持续效果被移除的原因，方便 OnRemove 操作区分到期、驱散和替换。
    /// </summary>
    public enum EffectRemovalReason
    {
        Expired,
        Dispelled,
        Replaced,
        OwnerDisposed
    }

    /// <summary>
    /// 指定数值公式读取的基础数据来源。
    /// </summary>
    public enum EffectValueSource
    {
        Constant,
        SignalValue,
        SignalRequestedValue,
        SourceAttack,
        TargetAttack,
        SourceMaxHp,
        TargetMaxHp,
        SourceCoreEnergy,
    }

    /// <summary>
    /// 指定触发条件的判断方式。
    /// </summary>
    public enum EffectConditionType
    {
        Always,
        SourceExists,
        TargetExists,
        CasterExists,
        HasAllTags,
        HasAnyTags,
        LacksAnyTags,
        ValueGreaterThan,
        ValueGreaterThanOrEqual
    }

    /// <summary>
    /// 不可变的战斗信号携带一次事实的完整上下文，所有后续触发都通过该上下文建立因果链。
    /// </summary>
    public sealed class EffectSignal
    {
        /// <summary>获取根信号及其全部子信号共享的唯一因果链编号。</summary>
        public long SignalChainId { get; private set; }

        /// <summary>获取当前信号在触发链中的深度。</summary>
        public int ChainDepth { get; private set; }

        /// <summary>获取信号类型。</summary>
        public EffectSignalType Type { get; }

        /// <summary>获取行为发起者。</summary>
        public Entity Source { get; }

        /// <summary>获取行为直接目标。</summary>
        public Entity Target { get; }

        /// <summary>获取技能或效果的原始施法者。</summary>
        public Entity Caster { get; }

        /// <summary>获取结算前请求数值。</summary>
        public float RequestedValue { get; }

        /// <summary>获取最终实际数值。</summary>
        public float Value { get; }

        /// <summary>获取伤害配置提供的打断能力；零表示该伤害不能触发韧性打断。</summary>
        public float InterruptPower { get; }

        /// <summary>获取本次伤害是否首次把目标从存活推进到死亡。</summary>
        public bool WasFatal { get; }

        /// <summary>获取信号标签。</summary>
        public EffectTag Tags { get; }

        /// <summary>获取产生信号的技能编号。</summary>
        public string AbilityId { get; }

        /// <summary>获取产生信号的效果实例编号。</summary>
        public long OriginEffectInstanceId { get; }

        /// <summary>获取信号对应的世界坐标。</summary>
        public Vector3 Position { get; }

        /// <summary>
        /// 创建一条战斗信号；SignalChainId 为零时由 EffectRuntime 在因果链开始时自动分配。
        /// </summary>
        public EffectSignal(EffectSignalType type, Entity source, Entity target, Entity caster, float requestedValue = 0f, float value = 0f, EffectTag tags = EffectTag.None, string abilityId = null, long originEffectInstanceId = 0L, Vector3 position = default, long signalChainId = 0L, int chainDepth = 0, float interruptPower = 0f, bool wasFatal = false)
        {
            Type = type;
            Source = source;
            Target = target;
            Caster = caster;
            RequestedValue = requestedValue;
            Value = value;
            InterruptPower = Mathf.Max(0f, interruptPower);
            WasFatal = wasFatal;
            Tags = tags;
            AbilityId = abilityId ?? string.Empty;
            OriginEffectInstanceId = originEffectInstanceId;
            Position = position;
            SignalChainId = signalChainId;
            ChainDepth = Mathf.Max(0, chainDepth);
        }

        /// <summary>
        /// 由运行时为根信号补充事务编号，外部系统不能修改已经确定的因果信息。
        /// </summary>
        internal void AssignTransaction(long signalChainId, int chainDepth)
        {
            if (SignalChainId != 0L) return;
            SignalChainId = signalChainId;
            ChainDepth = Mathf.Max(0, chainDepth);
        }

        /// <summary>
        /// 基于当前信号创建同一事务中的子信号，并自动增加触发链深度。
        /// </summary>
        public EffectSignal CreateChild(EffectSignalType type, Entity source, Entity target, Entity caster, float requestedValue = 0f, float value = 0f, EffectTag tags = EffectTag.None, string abilityId = null, long originEffectInstanceId = 0L, Vector3 position = default, float? interruptPower = null, bool? wasFatal = null)
        {
            return new EffectSignal(type, source, target, caster, requestedValue, value, tags, abilityId, originEffectInstanceId, position, SignalChainId, ChainDepth + 1, interruptPower ?? InterruptPower, wasFatal ?? WasFatal);
        }
    }

    /// <summary>
    /// 可序列化数值公式通过固定数据源、倍率和偏移值组合常见战斗数值，避免解析字符串表达式。
    /// </summary>
    [Serializable]
    public sealed class EffectValueFormula
    {
        [SerializeField] private EffectValueSource source = EffectValueSource.Constant;
        [SerializeField] private float multiplier;
        [SerializeField, FormerlySerializedAs("additive")] private float offset;

        /// <summary>
        /// 创建使用常量作为基础值的公式。
        /// </summary>
        public static EffectValueFormula Constant(float value)
        {
            return new EffectValueFormula { source = EffectValueSource.Constant, multiplier = 0f, offset = value };
        }

        /// <summary>
        /// 创建读取来源实体攻击力的公式。
        /// </summary>
        public static EffectValueFormula SourceAttack(float multiplier = 1f, float offset = 0f)
        {
            return new EffectValueFormula { source = EffectValueSource.SourceAttack, multiplier = multiplier, offset = offset };
        }

        /// <summary>
        /// 创建读取信号最终数值的公式。
        /// </summary>
        public static EffectValueFormula SignalValue(float multiplier = 1f, float offset = 0f)
        {
            return new EffectValueFormula { source = EffectValueSource.SignalValue, multiplier = multiplier, offset = offset };
        }

        /// <summary>
        /// 创建读取信号结算前请求数值的公式，适合把命中阶段已经算好的暴击伤害传给即时伤害效果。
        /// </summary>
        public static EffectValueFormula SignalRequestedValue(float multiplier = 1f, float offset = 0f)
        {
            return new EffectValueFormula { source = EffectValueSource.SignalRequestedValue, multiplier = multiplier, offset = offset };
        }

        /// <summary>
        /// 根据操作上下文计算最终数值。
        /// </summary>
        public float Evaluate(EffectOperationContext context)
        {
            float baseValue = ResolveBaseValue(context);
            return baseValue * multiplier + offset;
        }

        /// <summary>
        /// 根据配置的数据源读取未经倍率处理的基础数值。
        /// </summary>
        private float ResolveBaseValue(EffectOperationContext context)
        {
            switch (source)
            {
                case EffectValueSource.Constant: return 1f;
                case EffectValueSource.SignalValue: return context.Signal.Value;
                case EffectValueSource.SignalRequestedValue: return context.Signal.RequestedValue;
                case EffectValueSource.SourceAttack: return ReadProperty(context.Source, property => property.Atk);
                case EffectValueSource.TargetAttack: return ReadProperty(context.Target, property => property.Atk);
                case EffectValueSource.SourceMaxHp: return ReadProperty(context.Source, property => property.MaxHp);
                case EffectValueSource.TargetMaxHp: return ReadProperty(context.Target, property => property.MaxHp);
                case EffectValueSource.SourceCoreEnergy: return ReadProperty(context.Source, property => property.CoreEnergyLimit);
                default: return 0f;
            }
        }

        /// <summary>
        /// 安全读取实体的 PropertyComponent；缺少实体时返回零，缺少组件时沿用框架现有错误日志。
        /// </summary>
        private static float ReadProperty(Entity entity, Func<PropertyComponent, float> reader)
        {
            if (entity == null) return 0f;
            if (!entity.TryGetComp(out PropertyComponent property)) return 0f;
            return reader(property);
        }
    }

    /// <summary>
    /// 可序列化条件定义负责对 EffectSignal 做无副作用判断。
    /// </summary>
    [Serializable]
    public sealed class EffectConditionDefinition
    {
        [SerializeField] private EffectConditionType type = EffectConditionType.Always;
        [SerializeField] private EffectTag tags;
        [SerializeField] private float threshold;

        /// <summary>
        /// 创建一条始终通过的条件。
        /// </summary>
        public static EffectConditionDefinition Always()
        {
            return new EffectConditionDefinition { type = EffectConditionType.Always };
        }

        /// <summary>
        /// 创建一条要求目标存在的条件。
        /// </summary>
        public static EffectConditionDefinition TargetExists()
        {
            return new EffectConditionDefinition { type = EffectConditionType.TargetExists };
        }

        /// <summary>
        /// 创建一条要求信号包含全部指定标签的条件。
        /// </summary>
        public static EffectConditionDefinition HasAllTags(EffectTag tags)
        {
            return new EffectConditionDefinition { type = EffectConditionType.HasAllTags, tags = tags };
        }

        /// <summary>
        /// 创建一条要求信号至少包含一个指定标签的条件。
        /// </summary>
        public static EffectConditionDefinition HasAnyTags(EffectTag tags)
        {
            return new EffectConditionDefinition { type = EffectConditionType.HasAnyTags, tags = tags };
        }

        /// <summary>
        /// 创建一条要求信号不包含任何指定标签的条件。
        /// </summary>
        public static EffectConditionDefinition LacksAnyTags(EffectTag tags)
        {
            return new EffectConditionDefinition { type = EffectConditionType.LacksAnyTags, tags = tags };
        }

        /// <summary>
        /// 创建一条要求信号最终数值大于阈值的条件。
        /// </summary>
        public static EffectConditionDefinition ValueGreaterThan(float threshold)
        {
            return new EffectConditionDefinition { type = EffectConditionType.ValueGreaterThan, threshold = threshold };
        }

        /// <summary>
        /// 判断指定信号是否满足当前条件。
        /// </summary>
        public bool Evaluate(EffectSignal signal)
        {
            switch (type)
            {
                case EffectConditionType.Always: return true;
                case EffectConditionType.SourceExists: return signal.Source != null;
                case EffectConditionType.TargetExists: return signal.Target != null;
                case EffectConditionType.CasterExists: return signal.Caster != null;
                case EffectConditionType.HasAllTags: return (signal.Tags & tags) == tags;
                case EffectConditionType.HasAnyTags: return (signal.Tags & tags) != 0;
                case EffectConditionType.LacksAnyTags: return (signal.Tags & tags) == 0;
                case EffectConditionType.ValueGreaterThan: return signal.Value > threshold;
                case EffectConditionType.ValueGreaterThanOrEqual: return signal.Value >= threshold;
                default: return false;
            }
        }
    }
}
