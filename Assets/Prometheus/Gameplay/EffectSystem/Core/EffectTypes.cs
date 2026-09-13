using System;
using UnityEngine;
using Cfg = global::Prometheus.Config;
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
        Manual = 0,
        HitConfirmed = 1,
        DamageApplied = 2,
        Healed = 3,
        Killed = 4,
        EffectApplied = 5,
        EffectStacked = 6,
        EffectRemoved = 7,
        PeriodicTick = 8,
        CoreEnergyGain = 9,
        /// <summary>持续效果被重复施加并成功刷新有限持续时间。</summary>
        EffectRefreshed = 10,
        /// <summary>最终攻击力产生正向变化。</summary>
        AtkGain = 11,
        /// <summary>最终防御力产生正向变化。</summary>
        DefGain = 12,
        /// <summary>最终攻击速度产生正向变化。</summary>
        AtkSpeedGain = 13,
        /// <summary>最终暴击率产生正向变化。</summary>
        CritRateGain = 14,
        /// <summary>最终暴击伤害产生正向变化。</summary>
        CritDmgGain = 15,
        /// <summary>当前生命值产生正向变化；与 Healed 并存以支持统一的属性 Gain 路由。</summary>
        HpGain = 16,
        /// <summary>运行时最大生命值产生正向变化。</summary>
        MaxHpGain = 17,
        /// <summary>运行时核心能量上限产生正向变化。</summary>
        CoreEnergyLimitGain = 18,
        /// <summary>当前大招能量产生正向变化。</summary>
        UltEnergyGain = 19,
        /// <summary>运行时大招能量上限产生正向变化。</summary>
        UltEnergyLimitGain = 20,
        /// <summary>最终韧性产生正向变化。</summary>
        ToughnessGain = 21,
        /// <summary>最终出伤加成产生正向变化。</summary>
        DamageBoostGain = 22,
        /// <summary>最终受伤加成产生正向变化。</summary>
        DamageTakenBoostGain = 23,
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
        /// <summary>
        /// 保留位，当前无人写入。暴击事实走 <see cref="DamageFacts.IsCritical"/> 而不是标签：
        /// 标签会随子信号继承下去，一次暴击之后由它触发的后续伤害会被误标成暴击。
        /// </summary>
        Critical = 1 << 6,
        Healing = 1 << 7,
        Buff = 1 << 8,
        Debuff = 1 << 9,
        Attribute = 1 << 10,
        Control = 1 << 11,
        CoreEnergyGain = 1 << 12,
        UltEnergyGain = 1 << 13,
        /// <summary>标识特殊攻击产生的信号；追加位保证已有标签掩码保持稳定。</summary>
        SpecialAttack = 1 << 14,
        /// <summary>标识大招产生的信号；追加位保证已有标签掩码保持稳定。</summary>
        Ultimate = 1 << 15,
        /// <summary>标识攻击力 Gain；追加位保证已有标签掩码保持稳定。</summary>
        AtkGain = 1 << 16,
        /// <summary>标识防御力 Gain；追加位保证已有标签掩码保持稳定。</summary>
        DefGain = 1 << 17,
        /// <summary>标识攻击速度 Gain；追加位保证已有标签掩码保持稳定。</summary>
        AtkSpeedGain = 1 << 18,
        /// <summary>标识暴击率 Gain；追加位保证已有标签掩码保持稳定。</summary>
        CritRateGain = 1 << 19,
        /// <summary>标识暴击伤害 Gain；追加位保证已有标签掩码保持稳定。</summary>
        CritDmgGain = 1 << 20,
        /// <summary>标识当前生命值 Gain；Healing 继续描述治疗语义，本标签描述统一属性变化语义。</summary>
        HpGain = 1 << 21,
        /// <summary>标识运行时最大生命值 Gain。</summary>
        MaxHpGain = 1 << 22,
        /// <summary>标识运行时核心能量上限 Gain。</summary>
        CoreEnergyLimitGain = 1 << 23,
        /// <summary>标识运行时大招能量上限 Gain。</summary>
        UltEnergyLimitGain = 1 << 24,
        /// <summary>标识韧性 Gain。</summary>
        ToughnessGain = 1 << 25,
        /// <summary>标识出伤加成 Gain。</summary>
        DamageBoostGain = 1 << 26,
        /// <summary>标识受伤加成 Gain。</summary>
        DamageTakenBoostGain = 1 << 27,
        /// <summary>标识角色等级、天赋、装备和武器系统生成的常驻养成效果。</summary>
        Growth = 1 << 28,
    }

    /// <summary>
    /// 指定触发规则相对于规则拥有者监听信号中的哪个角色。
    /// </summary>
    public enum EffectListenScope
    {
        Caster = 0,
        Target = 1,
        Source = 2,
        Any = 3
    }

    /// <summary>
    /// 指定触发后产生的效果应该选择信号中的哪个实体作为目标。
    /// </summary>
    public enum EffectTargetSelector
    {
        Caster = 0,
        Target = 1,
        Source = 2
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
        Definition = 0,
        DefinitionAndSource = 1,
        DefinitionAndCaster = 2
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
    /// 指定数值公式读取常量、信号数据还是实体运行时属性；实体角色与具体属性由独立枚举组合。
    /// </summary>
    public enum EffectValueSource
    {
        /// <summary>使用常量一作为基础值。</summary>
        One = 0,
        /// <summary>读取当前信号的最终实际数值。</summary>
        SignalValue = 1,
        /// <summary>读取当前信号的结算前请求数值。</summary>
        SignalRequestedValue = 2,
        /// <summary>按照 EffectValueEntity 与 EffectPropertyValue 读取实体的运行时属性。</summary>
        Property = 8,
    }

    /// <summary>
    /// 指定属性公式读取直接释放者、效果目标还是整条因果链的实际源头。
    /// </summary>
    public enum EffectValueEntity
    {
        /// <summary>读取直接释放当前效果的实体。</summary>
        Caster = 0,
        /// <summary>读取当前效果的目标实体。</summary>
        Target = 1,
        /// <summary>读取整条因果链的实际源头实体。</summary>
        Source = 2,
    }

    /// <summary>
    /// 标识 Effect 公式可读取的全部 PropertyComponent 运行时数值，包含战斗属性、当前资源与对应上限。
    /// </summary>
    public enum EffectPropertyValue
    {
        /// <summary>最终攻击力。</summary>
        Atk = 0,
        /// <summary>最终防御力。</summary>
        Def = 1,
        /// <summary>当前移动速度。</summary>
        MoveSpeed = 2,
        /// <summary>最终攻击速度。</summary>
        AtkSpeed = 3,
        /// <summary>最终暴击率。</summary>
        CritRate = 4,
        /// <summary>最终暴击伤害。</summary>
        CritDmg = 5,
        /// <summary>当前生命值。</summary>
        Hp = 6,
        /// <summary>运行时最大生命值。</summary>
        MaxHp = 7,
        /// <summary>空中移动速度。</summary>
        AirMoveSpeed = 8,
        /// <summary>跳跃速度。</summary>
        JumpSpeed = 9,
        /// <summary>重力加速度。</summary>
        Gravity = 10,
        /// <summary>当前核心能量。</summary>
        CoreEnergy = 11,
        /// <summary>运行时核心能量上限。</summary>
        CoreEnergyLimit = 12,
        /// <summary>当前大招能量。</summary>
        UltEnergy = 13,
        /// <summary>运行时大招能量上限。</summary>
        UltEnergyLimit = 14,
        /// <summary>最终抗打断等级；取值名沿用 Toughness 以保持已有 Effect 资产的序列化索引稳定。</summary>
        Toughness = 15,
        /// <summary>最终出伤加成。</summary>
        DamageBoost = 16,
        /// <summary>最终受伤加成。</summary>
        DamageTakenBoost = 17,
    }

    /// <summary>
    /// 指定触发条件的判断方式。
    /// </summary>
    public enum EffectConditionType
    {
        Always,
        CasterExists,
        TargetExists,
        SourceExists,
        HasAllTags,
        HasAnyTags,
        LacksAnyTags,
        ValueGreaterThan,
        ValueGreaterThanOrEqual,
        /// <summary>要求信号最终伤害元素等于配置元素；追加值保证已有条件资产索引稳定。</summary>
        DamageElementEquals,
        /// <summary>要求本次伤害触发了元素反应；不区分具体反应。</summary>
        DamageTriggeredReaction,
        /// <summary>要求本次伤害触发的反应等于配置的反应标识。</summary>
        DamageReactionEquals,
        /// <summary>要求本次伤害是暴击。</summary>
        DamageWasCritical
    }

    /// <summary>
    /// 一次伤害的全部结算事实。
    ///
    /// 收成一个结构体而不是继续往 `EffectSignal` 上挂平铺字段：伤害的事实维度会持续增加
    /// （元素、反应、打断等级、暴击、后面还会有击退与承伤来源），而每加一项都要同时改
    /// 构造函数与 `CreateChild` 两处长参数表，十几个参数之后没人能安全地改它。
    ///
    /// 只承载**身份与判定结果**，不承载数值影响：增幅倍率与激化加算已经并入 `Value`，
    /// 再挂一份倍率会让下游有两个真相来源。
    /// </summary>
    public readonly struct DamageFacts
    {
        /// <summary>不携带任何伤害语义的默认事实，供治疗、能量等非伤害信号使用。</summary>
        public static readonly DamageFacts None = new DamageFacts(Cfg.ElementType.Physical, DamageActionType.Effect, null, 0, false, false);

        /// <summary>获取当前伤害经过动作与元素附魔覆盖后使用的唯一元素。</summary>
        public readonly Cfg.ElementType Element;

        /// <summary>获取产生当前伤害的动作类别。</summary>
        public readonly DamageActionType ActionType;

        /// <summary>获取本次伤害触发的元素反应标识；未触发反应时为空串。</summary>
        public readonly string ReactionId;

        /// <summary>
        /// 获取本次伤害的打断等级（08 第 2.1 节）。
        ///
        /// 0 表示不打断（DOT、剧变伤害、部分场域）。它与伤害数值无关：
        /// 打断是 `打断等级 > 目标抗打断等级` 的整数比较。
        /// </summary>
        public readonly int StaggerLevel;

        /// <summary>
        /// 获取本次伤害是否暴击。
        ///
        /// 掷骰在结算时已经发生，结果必须随事实一起发布：飘字样式要用它，
        /// 「暴击时触发」这类武器与圣遗物被动更是只能靠它。
        /// </summary>
        public readonly bool IsCritical;

        /// <summary>获取本次伤害是否首次把目标从存活推进到死亡。</summary>
        public readonly bool WasFatal;

        /// <summary>
        /// 获取本次攻击携带的元素附着强度档位。
        ///
        /// 它与打断等级一样属于**这一段攻击**而不是结算它的 Effect：
        /// 同一份直接伤害 Effect 会被普攻、战技、爆发共用，各自的附着量却完全不同。
        /// </summary>
        public readonly Cfg.GaugeStrength GaugeStrength;

        /// <summary>获取本次攻击的 ICD 策略。</summary>
        public readonly Cfg.IcdPolicy IcdPolicy;

        /// <summary>获取 Independent 策略下的独立 ICD 组号。</summary>
        public readonly int IcdGroupId;

        /// <summary>创建一份伤害事实。</summary>
        public DamageFacts(Cfg.ElementType element, DamageActionType actionType, string reactionId, int staggerLevel, bool isCritical, bool wasFatal,
            Cfg.GaugeStrength gaugeStrength = Cfg.GaugeStrength.None, Cfg.IcdPolicy icdPolicy = Cfg.IcdPolicy.Shared, int icdGroupId = 0)
        {
            Element = element;
            ActionType = actionType;
            ReactionId = reactionId ?? string.Empty;
            StaggerLevel = staggerLevel > 0 ? staggerLevel : 0;
            IsCritical = isCritical;
            WasFatal = wasFatal;
            GaugeStrength = gaugeStrength;
            IcdPolicy = icdPolicy;
            IcdGroupId = icdGroupId;
        }

        /// <summary>基于当前事实派生一份只改动部分项的新事实，供子信号继承未指定的项。</summary>
        public DamageFacts With(Cfg.ElementType? element = null, DamageActionType? actionType = null, string reactionId = null, int? staggerLevel = null, bool? isCritical = null, bool? wasFatal = null,
            Cfg.GaugeStrength? gaugeStrength = null, Cfg.IcdPolicy? icdPolicy = null, int? icdGroupId = null)
        {
            return new DamageFacts(element ?? Element, actionType ?? ActionType, reactionId ?? ReactionId, staggerLevel ?? StaggerLevel, isCritical ?? IsCritical, wasFatal ?? WasFatal,
                gaugeStrength ?? GaugeStrength, icdPolicy ?? IcdPolicy, icdGroupId ?? IcdGroupId);
        }
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

        /// <summary>获取直接释放当前行为的实体。</summary>
        public Entity Caster { get; }

        /// <summary>获取行为直接目标。</summary>
        public Entity Target { get; }

        /// <summary>获取当前因果链的实际源头实体。</summary>
        public Entity Source { get; }

        /// <summary>获取结算前请求数值。</summary>
        public float RequestedValue { get; }

        /// <summary>获取最终实际数值。</summary>
        public float Value { get; }

        /// <summary>获取本次伤害的全部结算事实；非伤害信号为 <see cref="DamageFacts.None"/>。</summary>
        public DamageFacts Damage { get; }

        /// <summary>获取本次伤害的打断等级。</summary>
        public int StaggerLevel => Damage.StaggerLevel;

        /// <summary>获取本次伤害是否暴击。</summary>
        public bool IsCritical => Damage.IsCritical;

        /// <summary>获取本次伤害是否首次把目标从存活推进到死亡。</summary>
        public bool WasFatal => Damage.WasFatal;

        /// <summary>获取当前伤害经过动作与元素附魔覆盖后使用的唯一元素。</summary>
        public Cfg.ElementType DamageElement => Damage.Element;

        /// <summary>获取产生当前伤害的动作类别。</summary>
        public DamageActionType DamageActionType => Damage.ActionType;

        /// <summary>获取本次伤害触发的元素反应标识；未触发反应时为空串。</summary>
        public string ReactionId => Damage.ReactionId;

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
        public EffectSignal(EffectSignalType type, Entity caster, Entity target, Entity source, float requestedValue = 0f, float value = 0f, EffectTag tags = EffectTag.None, string abilityId = null, long originEffectInstanceId = 0L, Vector3 position = default, long signalChainId = 0L, int chainDepth = 0, DamageFacts? damage = null)
        {
            Type = type;
            Caster = caster;
            Target = target;
            Source = source;
            RequestedValue = requestedValue;
            Value = value;
            Damage = damage ?? DamageFacts.None;
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
        /// <param name="damage">子信号的伤害事实；不传则原样继承父信号的事实。</param>
        public EffectSignal CreateChild(EffectSignalType type, Entity caster, Entity target, Entity source, float requestedValue = 0f, float value = 0f, EffectTag tags = EffectTag.None, string abilityId = null, long originEffectInstanceId = 0L, Vector3 position = default, DamageFacts? damage = null)
        {
            return new EffectSignal(type, caster, target, source, requestedValue, value, tags, abilityId, originEffectInstanceId, position, SignalChainId, ChainDepth + 1, damage ?? Damage);
        }
    }

    /// <summary>
    /// 可序列化数值公式通过固定数据源、倍率和偏移值组合常见战斗数值，避免解析字符串表达式。
    /// </summary>
    [Serializable]
    public sealed class EffectValueFormula : ISerializationCallbackReceiver
    {
        /// <summary>旧版 CasterAttack 的序列化整数，仅用于无损迁移已有 Effect 资产。</summary>
        private const int LegacyCasterAttackSource = 3;
        /// <summary>旧版 TargetAttack 的序列化整数，仅用于无损迁移已有 Effect 资产。</summary>
        private const int LegacyTargetAttackSource = 4;
        /// <summary>旧版 CasterMaxHp 的序列化整数，仅用于无损迁移已有 Effect 资产。</summary>
        private const int LegacyCasterMaxHpSource = 5;
        /// <summary>旧版 TargetMaxHp 的序列化整数，仅用于无损迁移已有 Effect 资产。</summary>
        private const int LegacyTargetMaxHpSource = 6;
        /// <summary>旧版 CasterCoreEnergy 的序列化整数，仅用于无损迁移已有 Effect 资产。</summary>
        private const int LegacyCasterCoreEnergySource = 7;

        /// <summary>配置公式使用常量、信号数值还是实体运行时属性。</summary>
        [SerializeField, FormerlySerializedAs("source")] private EffectValueSource baseValueSource = EffectValueSource.One;
        /// <summary>仅在 Property 来源下生效，独立选择 Caster、Target 或 Source。</summary>
        [SerializeField] private EffectValueEntity propertyEntity = EffectValueEntity.Caster;
        /// <summary>仅在 Property 来源下生效，独立选择需要读取的运行时属性。</summary>
        [SerializeField] private EffectPropertyValue propertyValue = EffectPropertyValue.Atk;
        /// <summary>配置基础值的乘算系数。</summary>
        [SerializeField] private float multiplier;
        /// <summary>配置完成乘算后追加的固定偏移。</summary>
        [SerializeField, FormerlySerializedAs("additive")] private float offset;

        /// <summary>
        /// 创建使用常量作为基础值的公式。
        /// </summary>
        public static EffectValueFormula Constant(float value)
        {
            return new EffectValueFormula { baseValueSource = EffectValueSource.One, multiplier = 0f, offset = value };
        }

        /// <summary>
        /// 创建读取直接释放者攻击力的公式。
        /// </summary>
        public static EffectValueFormula CasterAttack(float multiplier = 1f, float offset = 0f)
        {
            return Property(EffectValueEntity.Caster, EffectPropertyValue.Atk, multiplier, offset);
        }

        /// <summary>
        /// 创建按实体角色与运行时属性自由组合的公式；所有当前资源和运行时上限均通过此入口读取。
        /// </summary>
        public static EffectValueFormula Property(EffectValueEntity entity, EffectPropertyValue value, float multiplier = 1f, float offset = 0f)
        {
            return new EffectValueFormula { baseValueSource = EffectValueSource.Property, propertyEntity = entity, propertyValue = value, multiplier = multiplier, offset = offset };
        }

        /// <summary>
        /// 创建读取信号最终数值的公式。
        /// </summary>
        public static EffectValueFormula SignalValue(float multiplier = 1f, float offset = 0f)
        {
            return new EffectValueFormula { baseValueSource = EffectValueSource.SignalValue, multiplier = multiplier, offset = offset };
        }

        /// <summary>
        /// 创建读取信号结算前请求数值的公式，适合把命中阶段已经算好的暴击伤害传给即时伤害效果。
        /// </summary>
        public static EffectValueFormula SignalRequestedValue(float multiplier = 1f, float offset = 0f)
        {
            return new EffectValueFormula { baseValueSource = EffectValueSource.SignalRequestedValue, multiplier = multiplier, offset = offset };
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
            switch (baseValueSource)
            {
                case EffectValueSource.One: return 1f;
                case EffectValueSource.SignalValue: return context.Signal.Value;
                case EffectValueSource.SignalRequestedValue: return context.Signal.RequestedValue;
                case EffectValueSource.Property: return ReadProperty(SelectPropertyEntity(context), propertyValue);
                default: return ResolveLegacyPropertyValue(context);
            }
        }

        /// <summary>
        /// 根据独立实体枚举选择公式需要读取的上下文角色。
        /// </summary>
        private Entity SelectPropertyEntity(EffectOperationContext context)
        {
            switch (propertyEntity)
            {
                case EffectValueEntity.Caster: return context.Caster;
                case EffectValueEntity.Target: return context.Target;
                case EffectValueEntity.Source: return context.Source;
                default: return null;
            }
        }

        /// <summary>
        /// 安全读取实体的 PropertyComponent 运行时副本；缺少实体或组件时返回零，且不会回读 PropertyConfig。
        /// </summary>
        private static float ReadProperty(Entity entity, EffectPropertyValue value)
        {
            if (entity == null) return 0f;
            if (!entity.TryGetComp(out PropertyComponent property)) return 0f;
            switch (value)
            {
                case EffectPropertyValue.Atk: return property.Atk;
                case EffectPropertyValue.Def: return property.Def;
                case EffectPropertyValue.MoveSpeed: return property.MoveSpeed;
                case EffectPropertyValue.AtkSpeed: return property.AtkSpeed;
                case EffectPropertyValue.CritRate: return property.CritRate;
                case EffectPropertyValue.CritDmg: return property.CritDmg;
                case EffectPropertyValue.Hp: return property.Hp;
                case EffectPropertyValue.MaxHp: return property.MaxHp;
                case EffectPropertyValue.AirMoveSpeed: return property.AirMoveSpeed;
                case EffectPropertyValue.JumpSpeed: return property.JumpSpeed;
                case EffectPropertyValue.Gravity: return property.Gravity;
                case EffectPropertyValue.CoreEnergy: return property.CoreEnergy;
                case EffectPropertyValue.CoreEnergyLimit: return property.CoreEnergyLimit;
                case EffectPropertyValue.UltEnergy: return property.UltEnergy;
                case EffectPropertyValue.UltEnergyLimit: return property.UltEnergyLimit;
                case EffectPropertyValue.Toughness: return property.StaggerResistance;
                case EffectPropertyValue.DamageBoost: return property.DamageBonus;
                case EffectPropertyValue.DamageTakenBoost: return property.DamageTakenBonus;
                default: return 0f;
            }
        }

        /// <summary>
        /// 在尚未触发 Unity 反序列化回调的旧 JSON 或旧资产上即时解释组合式来源，避免迁移窗口产生零值。
        /// </summary>
        private float ResolveLegacyPropertyValue(EffectOperationContext context)
        {
            switch ((int)baseValueSource)
            {
                case LegacyCasterAttackSource: return ReadProperty(context.Caster, EffectPropertyValue.Atk);
                case LegacyTargetAttackSource: return ReadProperty(context.Target, EffectPropertyValue.Atk);
                case LegacyCasterMaxHpSource: return ReadProperty(context.Caster, EffectPropertyValue.MaxHp);
                case LegacyTargetMaxHpSource: return ReadProperty(context.Target, EffectPropertyValue.MaxHp);
                case LegacyCasterCoreEnergySource: return ReadProperty(context.Caster, EffectPropertyValue.CoreEnergy);
                default: return 0f;
            }
        }

        /// <summary>
        /// 序列化前不修改公式；迁移只在读取旧数据后执行，避免污染新资产的稳定字段。
        /// </summary>
        public void OnBeforeSerialize()
        {
        }

        /// <summary>
        /// 将旧版组合枚举 3–7 拆解为 Property、实体角色与属性三个正交字段，保证已有资产无损升级。
        /// </summary>
        public void OnAfterDeserialize()
        {
            switch ((int)baseValueSource)
            {
                case LegacyCasterAttackSource: SetMigratedPropertySource(EffectValueEntity.Caster, EffectPropertyValue.Atk); break;
                case LegacyTargetAttackSource: SetMigratedPropertySource(EffectValueEntity.Target, EffectPropertyValue.Atk); break;
                case LegacyCasterMaxHpSource: SetMigratedPropertySource(EffectValueEntity.Caster, EffectPropertyValue.MaxHp); break;
                case LegacyTargetMaxHpSource: SetMigratedPropertySource(EffectValueEntity.Target, EffectPropertyValue.MaxHp); break;
                case LegacyCasterCoreEnergySource: SetMigratedPropertySource(EffectValueEntity.Caster, EffectPropertyValue.CoreEnergy); break;
            }
        }

        /// <summary>
        /// 原子写入迁移后的三个正交配置字段，使后续保存统一使用新版结构。
        /// </summary>
        private void SetMigratedPropertySource(EffectValueEntity entity, EffectPropertyValue value)
        {
            baseValueSource = EffectValueSource.Property;
            propertyEntity = entity;
            propertyValue = value;
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
        [FormerlySerializedAs("damageAttribute")]
        [SerializeField] private Cfg.ElementType damageElement = Cfg.ElementType.Physical;
        /// <summary>仅在 DamageReactionEquals 条件下使用的反应标识，对应 ReactionMatrix 的 reactionId。</summary>
        [SerializeField] private string reactionId = string.Empty;

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
        /// 创建一条要求信号最终伤害属性等于指定属性的条件。
        /// </summary>
        public static EffectConditionDefinition DamageElementEquals(Cfg.ElementType element)
        {
            return new EffectConditionDefinition { type = EffectConditionType.DamageElementEquals, damageElement = element };
        }

        /// <summary>
        /// 创建一条要求本次伤害触发了任意元素反应的条件。
        /// </summary>
        public static EffectConditionDefinition DamageTriggeredReaction()
        {
            return new EffectConditionDefinition { type = EffectConditionType.DamageTriggeredReaction };
        }

        /// <summary>
        /// 创建一条要求本次伤害触发了指定反应的条件；标识对应 ReactionMatrix 的 reactionId。
        /// </summary>
        public static EffectConditionDefinition DamageReactionEquals(string reaction)
        {
            return new EffectConditionDefinition { type = EffectConditionType.DamageReactionEquals, reactionId = reaction ?? string.Empty };
        }

        /// <summary>创建一条要求本次伤害为暴击的条件。</summary>
        public static EffectConditionDefinition DamageWasCritical()
        {
            return new EffectConditionDefinition { type = EffectConditionType.DamageWasCritical };
        }

        /// <summary>
        /// 判断指定信号是否满足当前条件。
        /// </summary>
        public bool Evaluate(EffectSignal signal)
        {
            switch (type)
            {
                case EffectConditionType.Always: return true;
                case EffectConditionType.CasterExists: return signal.Caster != null;
                case EffectConditionType.TargetExists: return signal.Target != null;
                case EffectConditionType.SourceExists: return signal.Source != null;
                case EffectConditionType.HasAllTags: return (signal.Tags & tags) == tags;
                case EffectConditionType.HasAnyTags: return (signal.Tags & tags) != 0;
                case EffectConditionType.LacksAnyTags: return (signal.Tags & tags) == 0;
                case EffectConditionType.ValueGreaterThan: return signal.Value > threshold;
                case EffectConditionType.ValueGreaterThanOrEqual: return signal.Value >= threshold;
                case EffectConditionType.DamageElementEquals: return signal.DamageElement == damageElement;
                case EffectConditionType.DamageTriggeredReaction: return signal.ReactionId.Length > 0;
                case EffectConditionType.DamageReactionEquals: return signal.ReactionId == reactionId;
                case EffectConditionType.DamageWasCritical: return signal.IsCritical;
                default: return false;
            }
        }
    }
}
