using System;
using System.Collections.Generic;
using UnityEngine;
using Cfg = global::Prometheus.Config;

namespace Xuan.Prometheus.Component
{
    /// <summary>
    /// 使用位标记描述不会改变数值、但会限制实体行为能力的控制状态；一个 Modifier 可以同时施加多个状态。
    /// </summary>
    [Flags]
    public enum ControlState
    {
        /// <summary>实体当前不受任何控制状态影响。</summary>
        None = 0,
        /// <summary>眩晕会禁止移动、普通行为和主动技能，但不会停止受击、死亡、物理或 Effect 生命周期。</summary>
        Stun = 1 << 0,
        /// <summary>禁锢只禁止位移相关行为，仍允许普通攻击和主动技能。</summary>
        Root = 1 << 1,
        /// <summary>沉默只禁止主动技能，仍允许移动和普通攻击。</summary>
        Silence = 1 << 2,
        /// <summary>受击状态严格跟随受击动画会话，期间禁止主动行为和移动，但不停止重力、Effect 或死亡流程。</summary>
        Attacked = 1 << 3,
        /// <summary>离场状态禁止角色行为、移动和技能，但不停止 Entity、Effect、冷却或数值生命周期。</summary>
        OffField = 1 << 4
    }

    /// <summary>
    /// 表示一次可按对象身份精确添加和移除的控制状态贡献，多个来源施加同一状态时互不覆盖。
    /// </summary>
    public sealed class ControlStateModifier
    {
        /// <summary>获取该 Modifier 贡献的全部控制状态。</summary>
        public ControlState States { get; }

        /// <summary>创建一个只能由 PropertyComponent 登记的控制状态 Modifier。</summary>
        internal ControlStateModifier(ControlState states)
        {
            States = states;
        }
    }

    /// <summary>
    /// 标识 PropertyComponent 对外提供的可修改属性；枚举值同时作为 Effect 配置中的稳定属性标识。
    /// </summary>
    public enum PropertyType
    {
        Atk,
        Def,
        MoveSpeed,
        AtkSpeed,
        CritRate,
        CritDmg,
        MaxHp,
        AirMoveSpeed,
        JumpSpeed,
        Gravity,
        CoreEnergyLimit,
        UltEnergyLimit,
        /// <summary>
        /// 已被 <see cref="StaggerResistance"/> 取代的旧韧性属性位。
        ///
        /// 旧模型用它做「打断能力 > 韧性」的数值阈值判定，新模型改为分级比较（08 第 2.2 节）。
        /// 枚举值是资产的序列化索引，不能删除，因此保留占位；不要再写入或读取它。
        /// </summary>
        Toughness,
        /// <summary>标识出伤阶段按 (1 + x) 独立乘算的伤害加成系数；追加在枚举末尾以保持已有资产的序列化索引稳定。</summary>
        DamageBoost,
        /// <summary>标识受伤阶段按 (1 + x) 独立乘算的受伤加成系数；追加在枚举末尾以保持已有资产的序列化索引稳定。</summary>
        DamageTakenBoost,

        // 以下属性位按 Docs/Design/Combat/05A 补齐。枚举值是 Effect 资产的序列化索引，
        // 因此只能追加在末尾，禁止插入或重排。

        /// <summary>元素精通；影响全部元素反应强度。</summary>
        ElementalMastery,
        /// <summary>元素充能效率；默认 1 表示百分之百。</summary>
        EnergyRecharge,
        /// <summary>治疗加成。</summary>
        HealingBonus,
        /// <summary>受治疗加成。</summary>
        IncomingHealingBonus,
        /// <summary>护盾强效。</summary>
        ShieldStrength,
        /// <summary>冷却缩减；上限一。</summary>
        CooldownReduction,
        /// <summary>体力消耗降低。</summary>
        StaminaConsumptionReduction,
        /// <summary>元素能量上限；取代 CoreEnergyLimit 与 UltEnergyLimit 的双能量模型。</summary>
        ElementalEnergyLimit,

        /// <summary>火元素伤害加成。</summary>
        PyroDamageBonus,
        /// <summary>水元素伤害加成。</summary>
        HydroDamageBonus,
        /// <summary>雷元素伤害加成。</summary>
        ElectroDamageBonus,
        /// <summary>冰元素伤害加成。</summary>
        CryoDamageBonus,
        /// <summary>草元素伤害加成。</summary>
        DendroDamageBonus,
        /// <summary>风元素伤害加成。</summary>
        AnemoDamageBonus,
        /// <summary>岩元素伤害加成。</summary>
        GeoDamageBonus,
        /// <summary>物理伤害加成。</summary>
        PhysicalDamageBonus,
        /// <summary>全伤害加成；与元素、动作加成同处一个乘区相加。</summary>
        AllDamageBonus,

        /// <summary>普通攻击伤害加成。</summary>
        NormalAttackBonus,
        /// <summary>重击伤害加成。</summary>
        ChargedAttackBonus,
        /// <summary>下落攻击伤害加成。</summary>
        PlungeAttackBonus,
        /// <summary>元素战技伤害加成。</summary>
        SkillBonus,
        /// <summary>元素爆发伤害加成。</summary>
        BurstBonus,

        /// <summary>火元素抗性。</summary>
        PyroResistance,
        /// <summary>水元素抗性。</summary>
        HydroResistance,
        /// <summary>雷元素抗性。</summary>
        ElectroResistance,
        /// <summary>冰元素抗性。</summary>
        CryoResistance,
        /// <summary>草元素抗性。</summary>
        DendroResistance,
        /// <summary>风元素抗性。</summary>
        AnemoResistance,
        /// <summary>岩元素抗性。</summary>
        GeoResistance,
        /// <summary>物理抗性。</summary>
        PhysicalResistance,

        /// <summary>火元素抗性削减。</summary>
        PyroResistanceReduction,
        /// <summary>水元素抗性削减。</summary>
        HydroResistanceReduction,
        /// <summary>雷元素抗性削减。</summary>
        ElectroResistanceReduction,
        /// <summary>冰元素抗性削减。</summary>
        CryoResistanceReduction,
        /// <summary>草元素抗性削减。</summary>
        DendroResistanceReduction,
        /// <summary>风元素抗性削减。</summary>
        AnemoResistanceReduction,
        /// <summary>岩元素抗性削减。</summary>
        GeoResistanceReduction,
        /// <summary>物理抗性削减。</summary>
        PhysicalResistanceReduction,

        /// <summary>减防；与无视防御在防御区中独立相乘。</summary>
        DefenseReduction,
        /// <summary>无视防御；与减防在防御区中独立相乘。</summary>
        DefenseIgnore,
        /// <summary>受到伤害降低；与抗性区独立。</summary>
        DamageReduction,

        /// <summary>抗打断等级；分级硬直模型中与攻击打断等级比较。</summary>
        StaggerResistance,
        /// <summary>霸体覆盖值；非零时覆盖抗打断等级。</summary>
        SuperArmor,
        /// <summary>
        /// 已废弃的逐角色体力上限位。
        ///
        /// 体力池全队共享、上限全局唯一且只由七天神像提升，因此上限归 `IStaminaSystem` 持有，
        /// 不存在「某个角色的体力上限」。枚举值是资产的序列化索引，不能删除，因此保留占位。
        /// 角色能影响的只有消耗侧，见 <see cref="StaminaConsumptionReduction"/>。
        /// </summary>
        Stamina,

        // 反应加成（05A 第 3.5 节）。每个反应一个独立槽位，由 ReactionMatrix 的 reactionBonusAttr 列
        // 按名字定位；剧变类反应还会额外叠加 AllTransformativeBonus。

        /// <summary>蒸发增幅倍率的加项。</summary>
        VaporizeBonus,
        /// <summary>融化增幅倍率的加项。</summary>
        MeltBonus,
        /// <summary>超载反应加成。</summary>
        OverloadedBonus,
        /// <summary>超导反应加成。</summary>
        SuperconductBonus,
        /// <summary>感电反应加成。</summary>
        ElectroChargedBonus,
        /// <summary>扩散反应加成。</summary>
        SwirlBonus,
        /// <summary>碎冰反应加成。</summary>
        ShatteredBonus,
        /// <summary>燃烧反应加成。</summary>
        BurningBonus,
        /// <summary>绽放反应加成。</summary>
        BloomBonus,
        /// <summary>超绽放反应加成。</summary>
        HyperbloomBonus,
        /// <summary>烈绽放反应加成。</summary>
        BurgeonBonus,
        /// <summary>结晶护盾量加成。</summary>
        CrystallizeBonus,
        /// <summary>超激化加成。</summary>
        AggravateBonus,
        /// <summary>蔓激化加成。</summary>
        SpreadBonus,
        /// <summary>全部剧变反应加成；与单项加成相加而非相乘。</summary>
        AllTransformativeBonus
    }

    /// <summary>
    /// 指定 modifier 修改属性的倍率部分还是最终加算部分。
    /// </summary>
    public enum PropertyModifierMode
    {
        /// <summary>写入百分比通道；最终值按 BaseValue × (1 + ΣBoost) 缩放。</summary>
        Boost,
        /// <summary>写入固定值通道；最终值在缩放之后加算。</summary>
        Offset,
        /// <summary>
        /// 写入基础值通道；与配置基础值相加后再一起被 Boost 缩放。
        /// 角色等级与突破提供的基础属性、武器基础攻击力都走这里——
        /// 它们必须被攻击力%这类词条放大，而 Offset 不会。追加在末尾以保持序列化索引稳定。
        /// </summary>
        Base
    }

    /// <summary>
    /// 表示一次可被精确添加和移除的属性修改；相同数值的 modifier 仍可按对象身份独立管理。
    /// </summary>
    public sealed class PropertyModifier
    {
        /// <summary>
        /// 获取该 modifier 影响的属性。
        /// </summary>
        public PropertyType Type { get; }

        /// <summary>
        /// 获取该 modifier 修改 Boost 还是 Offset。
        /// </summary>
        public PropertyModifierMode Mode { get; }

        /// <summary>
        /// 获取该 modifier 对目标通道贡献的数值。
        /// </summary>
        public float Value { get; }

        /// <summary>
        /// 创建一个只能由 PropertyComponent 登记的属性 modifier。
        /// </summary>
        internal PropertyModifier(PropertyType type, PropertyModifierMode mode, float value)
        {
            Type = type;
            Mode = mode;
            Value = value;
        }
    }

    /// <summary>表示不依赖 PropertyType 的通用可修改数值贡献，供天赋 Component 自己持有的增益系数使用。</summary>
    internal sealed class ModifiableValueModifier
    {
        /// <summary>创建一份固定模式和数值的通用 Modifier。</summary>
        public ModifiableValueModifier(PropertyModifierMode mode, float value)
        {
            Mode = mode;
            Value = value;
        }

        /// <summary>获取该 Modifier 写入 Boost 还是 Offset 通道。</summary>
        public PropertyModifierMode Mode { get; }

        /// <summary>获取该 Modifier 的稳定贡献数值。</summary>
        public float Value { get; }
    }

    /// <summary>
    /// 保存单个属性的基础值、modifier 集合与计算结果，并只在基础值或 modifier 变化时重算。
    /// </summary>
    public sealed class ModifiableProperty
    {
        /// <summary>
        /// 保存当前属性持有的全部 modifier；对象身份保证移除操作不会误删同值 modifier。
        /// </summary>
        private readonly HashSet<PropertyModifier> modifiers = new HashSet<PropertyModifier>();

        /// <summary>保存不绑定 PropertyType 的通用 Modifier，使其他 Component 也能复用同一可监听计算模型。</summary>
        private readonly HashSet<ModifiableValueModifier> valueModifiers = new HashSet<ModifiableValueModifier>();

        /// <summary>
        /// 保存不含 modifier 的基础值。
        /// </summary>
        private float baseValue;

        /// <summary>
        /// 保存包含默认倍率 1 的累计 Boost。
        /// </summary>
        private float boost = 1f;

        /// <summary>
        /// 保存全部加算 modifier 的累计 Offset。
        /// </summary>
        private float offset;

        /// <summary>保存全部 Base 通道 modifier 的累计值；它与配置基础值相加后再被 Boost 缩放。</summary>
        private float baseAddition;

        /// <summary>保存当前属性的全部脏回调；只有最终值实际变化时才会通知。</summary>
        private event Action Dirty;

        /// <summary>
        /// 获取按照 BaseValue × Boost + Offset 计算并缓存的最终值。
        /// </summary>
        public float Value { get; private set; }

        /// <summary>监听最终值变化并返回可释放句柄；默认立即回调一次，使 UI 无需额外请求初始化快照。</summary>
        public ListenHandle Listen(Action onDirty, bool invokeImmediately = true)
        {
            if (onDirty == null) throw new ArgumentNullException(nameof(onDirty));
            Dirty += onDirty;
            ListenHandle handle = new ListenHandle(() => Dirty -= onDirty);
            try
            {
                if (invokeImmediately) onDirty.Invoke();
                return handle;
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 更新基础值并立即刷新最终值。
        /// </summary>
        public void SetBaseValue(float value)
        {
            baseValue = value;
            Recalculate();
        }

        /// <summary>直接写入不叠加 Modifier 的运行时数值，适用于生命、能量和冷却等当前状态字段。</summary>
        internal void SetValue(float value)
        {
            baseValue = value;
            Recalculate();
        }

        /// <summary>
        /// 登记 modifier 并立即刷新最终值。
        /// </summary>
        public void AddModifier(PropertyModifier modifier)
        {
            if (modifier == null || !modifiers.Add(modifier)) return;
            Recalculate();
        }

        /// <summary>
        /// 按对象身份移除 modifier，并仅在实际移除成功时刷新最终值。
        /// </summary>
        public bool RemoveModifier(PropertyModifier modifier)
        {
            if (modifier == null || !modifiers.Remove(modifier)) return false;
            Recalculate();
            return true;
        }

        /// <summary>添加一份通用数值 Modifier，并返回供 EffectInstance 精确释放的对象身份句柄。</summary>
        internal ModifiableValueModifier AddValueModifier(PropertyModifierMode mode, float value)
        {
            ModifiableValueModifier modifier = new ModifiableValueModifier(mode, value);
            valueModifiers.Add(modifier);
            Recalculate();
            return modifier;
        }

        /// <summary>按对象身份移除通用数值 Modifier，其他同值来源不会受到影响。</summary>
        internal bool RemoveValueModifier(ModifiableValueModifier modifier)
        {
            if (modifier == null || !valueModifiers.Remove(modifier)) return false;
            Recalculate();
            return true;
        }

        /// <summary>
        /// 从有效 modifier 重新汇总三个通道，再按 (BaseValue + ΣBase) × Boost + Offset 更新缓存。
        /// </summary>
        private void Recalculate()
        {
            float previousValue = Value;
            boost = 1f;
            offset = 0f;
            baseAddition = 0f;
            foreach (PropertyModifier modifier in modifiers) Accumulate(modifier.Mode, modifier.Value);
            foreach (ModifiableValueModifier modifier in valueModifiers) Accumulate(modifier.Mode, modifier.Value);
            Value = (baseValue + baseAddition) * boost + offset;
            if (!Mathf.Approximately(previousValue, Value)) Dirty?.Invoke();
        }

        /// <summary>把一份 modifier 的数值累加到它所属的通道。</summary>
        private void Accumulate(PropertyModifierMode mode, float value)
        {
            switch (mode)
            {
                case PropertyModifierMode.Boost: boost += value; break;
                case PropertyModifierMode.Base: baseAddition += value; break;
                default: offset += value; break;
            }
        }
    }

    /// <summary>
    /// 管理实体全部可修改属性，对外只提供已经计算完成的属性结果与 modifier 操作入口。
    /// </summary>
    public class PropertyComponent : Component, IEntityBinderComponent, IControlStateProvider
    {
        /// <summary>
        /// 保存 Inspector 配置的基础属性资产；组件在 Start 阶段据此建立全部属性缓存。
        /// </summary>
        private PropertyConfig propConfig;

        /// <summary>锁定本次 Entity 生命周期已经发生的死亡跃迁，阻止尸体再次受伤、治疗或重复结算死亡。</summary>
        private bool isDead;

        /// <summary>
        /// 保存当前实体持有的全部控制状态 Modifier；对象身份保证重叠来源能够独立移除。
        /// </summary>
        private readonly HashSet<ControlStateModifier> controlStateModifiers = new HashSet<ControlStateModifier>();

        /// <summary>
        /// 保存 Effect 添加的全部出伤属性覆盖；解析时按优先级和加入顺序确定唯一结果。
        /// </summary>
        private readonly HashSet<ElementInfusionModifier> elementInfusions = new HashSet<ElementInfusionModifier>();

        /// <summary>
        /// 为伤害属性覆盖分配单调递增序号，使同优先级后应用者稳定胜出。
        /// </summary>
        private long nextElementInfusionSequence;

        /// <summary>
        /// 按 PropertyType 索引保存全部可修改属性。
        /// 用数组而不是逐个字段，是因为属性位会随玩法扩张持续增加；
        /// 逐字段写法要求每加一个属性位就同步改「字段、switch 分支、访问器」三处样板，
        /// 而这三处本可以由枚举索引直接得到。
        /// </summary>
        private readonly ModifiableProperty[] properties = CreateProperties();

        /// <summary>为每个 PropertyType 创建一个属性实例；数组长度由枚举决定，新增属性位无需改动本方法。</summary>
        private static ModifiableProperty[] CreateProperties()
        {
            int count = Enum.GetValues(typeof(PropertyType)).Length;
            ModifiableProperty[] created = new ModifiableProperty[count];
            for (int index = 0; index < count; index++) created[index] = new ModifiableProperty();
            return created;
        }

        /// <summary>攻击力。</summary>
        private ModifiableProperty atk => properties[(int)PropertyType.Atk];
        /// <summary>防御力。</summary>
        private ModifiableProperty def => properties[(int)PropertyType.Def];
        /// <summary>抗打断等级。</summary>
        private ModifiableProperty staggerResistance => properties[(int)PropertyType.StaggerResistance];
        /// <summary>霸体覆盖值。</summary>
        private ModifiableProperty superArmor => properties[(int)PropertyType.SuperArmor];
        /// <summary>出伤阶段独立乘区系数。</summary>
        private ModifiableProperty damageBoost => properties[(int)PropertyType.DamageBoost];
        /// <summary>受伤阶段独立乘区系数。</summary>
        private ModifiableProperty damageTakenBoost => properties[(int)PropertyType.DamageTakenBoost];
        /// <summary>当前移动模式速度。</summary>
        private ModifiableProperty moveSpeed => properties[(int)PropertyType.MoveSpeed];
        /// <summary>攻击速度。</summary>
        private ModifiableProperty atkSpeed => properties[(int)PropertyType.AtkSpeed];
        /// <summary>暴击率。</summary>
        private ModifiableProperty critRate => properties[(int)PropertyType.CritRate];
        /// <summary>暴击伤害。</summary>
        private ModifiableProperty critDmg => properties[(int)PropertyType.CritDmg];
        /// <summary>最大生命值。</summary>
        private ModifiableProperty maxHp => properties[(int)PropertyType.MaxHp];
        /// <summary>空中移动速度。</summary>
        private ModifiableProperty airMoveSpeed => properties[(int)PropertyType.AirMoveSpeed];
        /// <summary>跳跃速度。</summary>
        private ModifiableProperty jumpSpeed => properties[(int)PropertyType.JumpSpeed];
        /// <summary>重力加速度。</summary>
        private ModifiableProperty gravity => properties[(int)PropertyType.Gravity];
        /// <summary>核心能量上限。</summary>
        private ModifiableProperty coreEnergyLimit => properties[(int)PropertyType.CoreEnergyLimit];
        /// <summary>终结技能量上限。</summary>
        private ModifiableProperty ultEnergyLimit => properties[(int)PropertyType.UltEnergyLimit];

        /// <summary>保存当前生命值，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty hp = new ModifiableProperty();

        /// <summary>保存当前核心能量，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty coreEnergy = new ModifiableProperty();

        /// <summary>保存当前大招能量，并通过统一属性脏监听向 UI 暴露变化。</summary>
        private readonly ModifiableProperty ultEnergy = new ModifiableProperty();

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的攻击力。
        /// </summary>
        public float Atk => atk.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的防御力。
        /// </summary>
        public float Def => def.Value;

        /// <summary>获取配置与运行时修改之后的抗打断等级。</summary>
        public float StaggerResistance => staggerResistance.Value;

        /// <summary>获取霸体覆盖值；零表示当前不处于霸体。</summary>
        public float SuperArmor => superArmor.Value;

        /// <summary>
        /// 获取参与打断判定的最终抗打断等级。
        ///
        /// 霸体**覆盖**而不是叠加抗打断等级（05A 第 3.6 节）：霸体的语义是「这段时间内任何东西都打不断我」，
        /// 若改成相加，一个本身抗打断为 0 的小怪进入霸体后仍可能被高等级攻击打断。
        /// </summary>
        public float EffectiveStaggerResistance => !Mathf.Approximately(SuperArmor, 0f) ? SuperArmor : StaggerResistance;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的出伤独立乘区加成系数 x。
        /// </summary>
        public float DamageBonus => damageBoost.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的受伤独立乘区加成系数 x。
        /// </summary>
        public float DamageTakenBonus => damageTakenBoost.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的当前移动速度。
        /// </summary>
        public float MoveSpeed => moveSpeed.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的攻击速度。
        /// </summary>
        public float AtkSpeed => atkSpeed.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的暴击率。
        /// </summary>
        public float CritRate => critRate.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的暴击伤害。
        /// </summary>
        public float CritDmg => critDmg.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的最大生命值。
        /// </summary>
        public float MaxHp => maxHp.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的空中移动速度。
        /// </summary>
        public float AirMoveSpeed => airMoveSpeed.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的跳跃速度。
        /// </summary>
        public float JumpSpeed => jumpSpeed.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的重力加速度。
        /// </summary>
        public float Gravity => gravity.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的核心能量上限。
        /// </summary>
        public float CoreEnergyLimit => coreEnergyLimit.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的终结技能量上限。
        /// </summary>
        public float UltEnergyLimit => ultEnergyLimit.Value;

        /// <summary>获取当前生命字段的可监听属性对象。</summary>
        public ModifiableProperty HpProperty => hp;

        /// <summary>获取最大生命字段的可监听属性对象。</summary>
        public ModifiableProperty MaxHpProperty => maxHp;

        /// <summary>获取当前核心能量字段的可监听属性对象。</summary>
        public ModifiableProperty CoreEnergyProperty => coreEnergy;

        /// <summary>获取核心能量上限字段的可监听属性对象。</summary>
        public ModifiableProperty CoreEnergyLimitProperty => coreEnergyLimit;

        /// <summary>获取当前大招能量字段的可监听属性对象。</summary>
        public ModifiableProperty UltEnergyProperty => ultEnergy;

        /// <summary>获取大招能量上限字段的可监听属性对象。</summary>
        public ModifiableProperty UltEnergyLimitProperty => ultEnergyLimit;

        /// <summary>获取 PropertyConfig 配置的角色基础元素；缺少配置时安全回退为物理。</summary>
        public Cfg.ElementType Element => propConfig == null ? Cfg.ElementType.Physical : propConfig.elementAttribute;

        public float CoreEnergy => coreEnergy.Value;
        public float UltEnergy => ultEnergy.Value;
        /// <summary>
        /// 获取实体当前生命值；生命变化只能通过伤害、治疗或初始化入口执行。
        /// </summary>
        public float Hp => hp.Value;

        /// <summary>
        /// 获取实体是否已经没有生命值。
        /// </summary>
        public bool NoHp => Hp <= 0f;

        /// <summary>获取当前 Entity 生命周期是否已经完成唯一一次存活到死亡跃迁。</summary>
        public bool IsDead => isDead;

        /// <summary>获取当前大招能量是否已经达到正数上限；死亡实体即使保留能量也不能释放大招。</summary>
        public bool IsUltEnergyFull => !isDead && UltEnergyLimit > 0f && UltEnergy >= UltEnergyLimit;

        /// <summary>获取全部 ControlStateModifier 合并后的当前控制状态。</summary>
        public ControlState ActiveControlStates { get; private set; }

        /// <summary>获取实体是否可以执行普通攻击、转向和 AI 决策等主动行为；Stun、受击或离场状态存续时均不可行动。</summary>
        public bool CanAct => !isDead && !HasAnyControlState(ControlState.Stun | ControlState.Attacked | ControlState.OffField);

        /// <summary>获取实体当前是否处于由受击动画生命周期持有的受击状态。</summary>
        public bool IsAttacked => HasAnyControlState(ControlState.Attacked);

        /// <summary>获取实体是否可以执行地面移动、空中横移、跳跃、闪避、巡逻或追击。</summary>
        public bool CanMove => CanAct && !HasAnyControlState(ControlState.Root);

        /// <summary>获取实体是否可以释放主动技能；普通攻击不受 Silence 单独影响。</summary>
        public bool CanUseActiveSkill => CanAct && !HasAnyControlState(ControlState.Silence);

        /// <summary>使用显式属性配置建立纯 C# 运行态；GameObject 角色通常由 Bind 从 CharacterBinder 调用该入口。</summary>
        public void Initialize(PropertyConfig config)
        {
            propConfig = config != null ? config : throw new ArgumentNullException(nameof(config));
            RefreshBaseValuesInternal();
            hp.SetValue(MaxHp);
            isDead = Hp <= 0f;
        }

        /// <summary>
        /// 在 GameObjectLogic 完成根 Binder 校验后建立全部属性缓存，使同帧隐藏的后备成员也拥有完整运行态。
        /// </summary>
        public void Bind(Logic.EntityBinder binder)
        {
            CharacterBinder characterBinder = binder as CharacterBinder ?? throw new InvalidOperationException($"PropertyComponent requires CharacterBinder but received '{binder?.GetType().FullName}'.");
            Initialize(characterBinder.PropertyConfig);
        }

        /// <summary>解除只读配置引用并清空本 Entity 生命周期持有的控制状态与元素附魔。</summary>
        public void Unbind()
        {
            propConfig = null;
            controlStateModifiers.Clear();
            elementInfusions.Clear();
            ActiveControlStates = ControlState.None;
        }

        /// <summary>
        /// 重新读取当前 PropertyConfig 的全部基础值并刷新缓存，适用于运行时主动修改配置数据之后。
        /// </summary>
        public void RefreshBaseValues()
        {
            RefreshBaseValuesInternal();
            hp.SetValue(Mathf.Min(Hp, MaxHp));
            ClampEnergyToLimits();
        }

        /// <summary>
        /// 修改指定属性的基础值并立即更新其缓存结果；移动模式切换通过此入口更新 MoveSpeed。
        /// </summary>
        public void SetBaseValue(PropertyType type, float value)
        {
            GetProperty(type).SetBaseValue(value);
            if (type == PropertyType.MaxHp) hp.SetValue(Mathf.Min(Hp, maxHp.Value));
            if (type == PropertyType.CoreEnergyLimit || type == PropertyType.UltEnergyLimit) ClampEnergyToLimits();
        }

        /// <summary>
        /// 为指定属性添加一个 Boost 或 Offset modifier，并返回用于精确移除的对象引用。
        /// </summary>
        public PropertyModifier AddModifier(PropertyType type, PropertyModifierMode mode, float value)
        {
            ModifiableProperty property = GetProperty(type);
            PropertyModifier modifier = new PropertyModifier(type, mode, value);
            property.AddModifier(modifier);
            if (type == PropertyType.MaxHp) hp.SetValue(Mathf.Min(Hp, maxHp.Value));
            if (type == PropertyType.CoreEnergyLimit || type == PropertyType.UltEnergyLimit) ClampEnergyToLimits();
            return modifier;
        }

        /// <summary>
        /// 按对象身份移除指定 modifier，并在最大生命值变化时约束当前生命值不超过新的 MaxHp。
        /// </summary>
        public bool RemoveModifier(PropertyModifier modifier)
        {
            if (modifier == null) return false;
            bool removed = GetProperty(modifier.Type).RemoveModifier(modifier);
            if (removed && modifier.Type == PropertyType.MaxHp) hp.SetValue(Mathf.Min(Hp, maxHp.Value));
            if (removed && (modifier.Type == PropertyType.CoreEnergyLimit || modifier.Type == PropertyType.UltEnergyLimit)) ClampEnergyToLimits();
            return removed;
        }

        /// <summary>
        /// 添加一份指定动作范围的元素附魔，并返回只能按对象身份精确移除的句柄。
        /// </summary>
        public ElementInfusionModifier AddElementInfusion(Cfg.ElementType element, DamageActionMask actionMask, int priority)
        {
            ElementInfusionModifier modifier = new ElementInfusionModifier(element, actionMask, priority, ++nextElementInfusionSequence);
            elementInfusions.Add(modifier);
            return modifier;
        }

        /// <summary>
        /// 按对象身份移除元素附魔，不影响其他 Effect 提供的同元素或同优先级附魔。
        /// </summary>
        public bool RemoveElementInfusion(ElementInfusionModifier modifier)
        {
            return modifier != null && elementInfusions.Remove(modifier);
        }

        /// <summary>
        /// 先解析动作基础元素，再让匹配动作范围的最高优先级附魔覆盖最终出伤元素。
        /// </summary>
        public Cfg.ElementType ResolveDamageElement(DamageActionType actionType)
        {
            Cfg.ElementType resolvedElement = DamageElementRules.GetBaseElement(actionType, Element);
            DamageActionMask requiredMask = DamageElementRules.GetActionMask(actionType);
            ElementInfusionModifier selectedModifier = null;
            foreach (ElementInfusionModifier modifier in elementInfusions)
            {
                if ((modifier.ActionMask & requiredMask) == 0) continue;
                if (selectedModifier != null && modifier.Priority < selectedModifier.Priority) continue;
                if (selectedModifier != null && modifier.Priority == selectedModifier.Priority && modifier.Sequence < selectedModifier.Sequence) continue;
                selectedModifier = modifier;
            }
            return selectedModifier == null ? resolvedElement : selectedModifier.Element;
        }

        /// <summary>
        /// 添加一份控制状态贡献并返回身份句柄；传入组合标记可以由同一个 Effect 同时施加多种限制。
        /// </summary>
        public ControlStateModifier AddControlStateModifier(ControlState states)
        {
            ControlStateModifier modifier = new ControlStateModifier(states);
            controlStateModifiers.Add(modifier);
            RecalculateControlStates();
            return modifier;
        }

        /// <summary>
        /// 按对象身份移除指定控制状态贡献，并仅在实际移除成功时重新计算聚合状态。
        /// </summary>
        public bool RemoveControlStateModifier(ControlStateModifier modifier)
        {
            if (modifier == null || !controlStateModifiers.Remove(modifier)) return false;
            RecalculateControlStates();
            return true;
        }

        /// <summary>
        /// 判断当前实体是否至少持有参数中的一种控制状态；None 永远返回 false。
        /// </summary>
        public bool HasAnyControlState(ControlState states)
        {
            return states != ControlState.None && (ActiveControlStates & states) != ControlState.None;
        }

        /// <summary>
        /// 结算一次非负伤害、返回受剩余生命值截断后的实际扣血量，并在运行模式中显示截断前的预计伤害飘字。
        /// </summary>
        public float OnTakeDamage(float damage)
        {
            return OnTakeDamage(damage, out _);
        }

        /// <summary>将传入伤害按受伤独立乘区 (1 + DamageTakenBonus) 结算一次，显示受剩余生命值截断前的预计伤害飘字，并以原子返回值指出本次结算是否首次把目标从存活推进到死亡；返回值仍为受剩余生命值限制的实际扣血量。</summary>
        public float OnTakeDamage(float damage, out bool wasFatal)
        {
            wasFatal = false;
            if (isDead) return 0f;
            float safeDamage = Mathf.Max(0f, damage * (1f + DamageTakenBonus));
            float oldHp = Hp;
            hp.SetValue(Mathf.Max(0f, oldHp - safeDamage));
            float actualDamage = oldHp - Hp;
            wasFatal = oldHp > 0f && Hp <= 0f;
            if (wasFatal) isDead = true;
            if (Application.isPlaying && safeDamage > 0f) FloatTextKit.Ins.CastNumberText(safeDamage, Entity.bindGo.transform.position);
            return actualDamage;
        }

        /// <summary>
        /// 结算一次非负治疗、返回受生命上限截断后的实际恢复生命值，并在运行模式中显示截断前的请求治疗量。
        /// </summary>
        public float OnRecoverHp(float recover)
        {
            if (isDead) return 0f;
            float safeRecover = Mathf.Max(0f, recover);
            float oldHp = Hp;
            hp.SetValue(Mathf.Min(MaxHp, oldHp + safeRecover));
            float actualRecover = Hp - oldHp;
            if (Application.isPlaying) FloatTextKit.Ins.CastNumberText(safeRecover, Entity.bindGo.transform.position, true);
            return actualRecover;
        }
        /// <summary>按有符号变化量调整当前核心能量并约束在零到运行时上限之间，返回本次实际变化量；负返回值表示能量被扣除。</summary>
        public float OnGainCoreEnergy(float energy)
        {
            if (isDead) return 0f;
            float oldCoreEnergy = CoreEnergy;
            coreEnergy.SetValue(Mathf.Clamp(CoreEnergy + energy, 0f, Mathf.Max(0f, CoreEnergyLimit)));
            return CoreEnergy - oldCoreEnergy;
        }

        /// <summary>增加非负大招能量并按当前大招能量上限截断，返回本次实际增加量。</summary>
        public float OnGainUltEnergy(float energy)
        {
            if (isDead) return 0f;
            float safeEnergy = Mathf.Max(0f, energy);
            float oldUltEnergy = UltEnergy;
            ultEnergy.SetValue(Mathf.Min(UltEnergyLimit, UltEnergy + safeEnergy));
            return UltEnergy - oldUltEnergy;
        }

        /// <summary>在大招成功取得动画会话后清空全部大招能量，并返回清空前的能量值供事件快照使用。</summary>
        public float ConsumeAllUltEnergy()
        {
            float consumedEnergy = UltEnergy;
            ultEnergy.SetValue(0f);
            return consumedEnergy;
        }
        /// <summary>
        /// 按属性位读取已应用全部通道的最终值。
        ///
        /// 05A 补齐的元素伤害加成、元素抗性与抗性削减是**按元素选槽**的，槽位只能在运行时确定，
        /// 因此这些属性不提供命名访问器，统一由本入口读取。
        /// </summary>
        public float GetValue(PropertyType type)
        {
            return GetProperty(type).Value;
        }

        /// <summary>
        /// 返回目标属性对象；调用方必须遵守 Unity 生命周期，在 Start 完成后使用运行时属性。
        /// </summary>
        private ModifiableProperty GetProperty(PropertyType type)
        {
            int index = (int)type;
            if (index < 0 || index >= properties.Length) throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported property type.");
            return properties[index];
        }

        /// <summary>属性上限变化后同步约束两种当前能量，保证运行态始终处于零到对应上限之间。</summary>
        private void ClampEnergyToLimits()
        {
            coreEnergy.SetValue(Mathf.Clamp(CoreEnergy, 0f, Mathf.Max(0f, CoreEnergyLimit)));
            ultEnergy.SetValue(Mathf.Clamp(UltEnergy, 0f, Mathf.Max(0f, UltEnergyLimit)));
        }

        /// <summary>
        /// 从全部有效 Modifier 重新合并状态，并通过实体 EventComponent 发布唯一一次状态变化事实。
        /// </summary>
        private void RecalculateControlStates()
        {
            ControlState previousStates = ActiveControlStates;
            ControlState nextStates = ControlState.None;
            foreach (ControlStateModifier modifier in controlStateModifiers) nextStates |= modifier.States;
            if (previousStates == nextStates) return;
            ActiveControlStates = nextStates;
            if (Entity != null && Entity.TryGetComp(out EventComponent eventComponent)) eventComponent.Invoke(new ControlStateChangedEvent(previousStates, nextStates));
        }

        /// <summary>
        /// 将 PropertyConfig 中的基础数据写入各属性；MoveSpeed 默认采用跑步速度，随后可由移动模式覆盖。
        /// </summary>
        private void RefreshBaseValuesInternal()
        {
            atk.SetBaseValue(propConfig == null ? 0f : propConfig.atk);
            def.SetBaseValue(propConfig == null ? 0f : propConfig.def);
            staggerResistance.SetBaseValue(propConfig == null ? 0f : propConfig.staggerResistance);
            // 霸体只由运行时效果施加，没有静态配置来源，因此每次刷新都回到非霸体。
            superArmor.SetBaseValue(0f);
            // 伤害加成不属于静态配置，每次刷新都从零基础值开始，仅接受运行时修改。
            damageBoost.SetBaseValue(0f);
            damageTakenBoost.SetBaseValue(0f);
            moveSpeed.SetBaseValue(propConfig == null ? 0f : propConfig.runSpeed);
            atkSpeed.SetBaseValue(propConfig == null ? 1f : propConfig.atkSpeed);
            critRate.SetBaseValue(propConfig == null ? 0f : propConfig.critRate);
            critDmg.SetBaseValue(propConfig == null ? 0f : propConfig.critDmg);
            maxHp.SetBaseValue(propConfig == null ? 0f : propConfig.hp);
            airMoveSpeed.SetBaseValue(propConfig == null ? 0f : propConfig.airMoveSpeed);
            jumpSpeed.SetBaseValue(propConfig == null ? 0f : propConfig.jumpSpeed);
            gravity.SetBaseValue(propConfig == null ? 0f : propConfig.gravity);
            coreEnergyLimit.SetBaseValue(propConfig == null ? 0f : propConfig.coreEnergyLimit);
            ultEnergyLimit.SetBaseValue(propConfig == null ? 0f : propConfig.ultEnergyLimit);
        }
    }
}
