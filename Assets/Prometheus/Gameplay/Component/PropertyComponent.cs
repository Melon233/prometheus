using System;
using System.Collections.Generic;
using UnityEngine;

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
        Attacked = 1 << 3
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
        /// <summary>标识抵抗伤害打断的韧性属性；追加在枚举末尾以保持已有资产的序列化索引稳定。</summary>
        Toughness
    }

    /// <summary>
    /// 指定 modifier 修改属性的倍率部分还是最终加算部分。
    /// </summary>
    public enum PropertyModifierMode
    {
        Boost,
        Offset
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

    /// <summary>
    /// 保存单个属性的基础值、modifier 集合与计算结果，并只在基础值或 modifier 变化时重算。
    /// </summary>
    internal sealed class ModifiableProperty
    {
        /// <summary>
        /// 保存当前属性持有的全部 modifier；对象身份保证移除操作不会误删同值 modifier。
        /// </summary>
        private readonly HashSet<PropertyModifier> modifiers = new HashSet<PropertyModifier>();

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

        /// <summary>
        /// 获取按照 BaseValue × Boost + Offset 计算并缓存的最终值。
        /// </summary>
        public float Value { get; private set; }

        /// <summary>
        /// 更新基础值并立即刷新最终值。
        /// </summary>
        public void SetBaseValue(float value)
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

        /// <summary>
        /// 从有效 modifier 重新汇总 Boost 和 Offset，再按 BaseValue × Boost + Offset 更新缓存。
        /// </summary>
        private void Recalculate()
        {
            boost = 1f;
            offset = 0f;
            foreach (PropertyModifier modifier in modifiers)
            {
                if (modifier.Mode == PropertyModifierMode.Boost) boost += modifier.Value;
                else offset += modifier.Value;
            }
            Value = baseValue * boost + offset;
        }
    }

    /// <summary>
    /// 管理实体全部可修改属性，对外只提供已经计算完成的属性结果与 modifier 操作入口。
    /// </summary>
    public class PropertyComponent : MonoComponent
    {
        /// <summary>
        /// 保存 Inspector 配置的基础属性资产；组件在 Start 阶段据此建立全部属性缓存。
        /// </summary>
        [SerializeField] private PropertyConfig propConfig;

        /// <summary>锁定本次 Entity 生命周期已经发生的死亡跃迁，阻止尸体再次受伤、治疗或重复结算死亡。</summary>
        private bool isDead;

        /// <summary>
        /// 保存当前实体持有的全部控制状态 Modifier；对象身份保证重叠来源能够独立移除。
        /// </summary>
        private readonly HashSet<ControlStateModifier> controlStateModifiers = new HashSet<ControlStateModifier>();

        /// <summary>
        /// 保存攻击力的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty atk = new ModifiableProperty();

        /// <summary>
        /// 保存防御力的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty def = new ModifiableProperty();

        /// <summary>
        /// 保存韧性的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty toughness = new ModifiableProperty();

        /// <summary>
        /// 保存当前移动模式速度的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty moveSpeed = new ModifiableProperty();

        /// <summary>
        /// 保存攻击速度的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty atkSpeed = new ModifiableProperty();

        /// <summary>
        /// 保存暴击率的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty critRate = new ModifiableProperty();

        /// <summary>
        /// 保存暴击伤害的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty critDmg = new ModifiableProperty();

        /// <summary>
        /// 保存最大生命值的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty maxHp = new ModifiableProperty();

        /// <summary>
        /// 保存空中移动速度的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty airMoveSpeed = new ModifiableProperty();

        /// <summary>
        /// 保存跳跃速度的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty jumpSpeed = new ModifiableProperty();

        /// <summary>
        /// 保存重力加速度的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty gravity = new ModifiableProperty();

        /// <summary>
        /// 保存核心能量上限的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty coreEnergyLimit = new ModifiableProperty();

        /// <summary>
        /// 保存终结技能量上限的基础值、Boost、Offset 和最终值。
        /// </summary>
        private readonly ModifiableProperty ultEnergyLimit = new ModifiableProperty();

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的攻击力。
        /// </summary>
        public float Atk => atk.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的防御力。
        /// </summary>
        public float Def => def.Value;

        /// <summary>
        /// 获取已经应用 Boost 和 Offset 的韧性。
        /// </summary>
        public float Toughness => toughness.Value;

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

        /// <summary>
        /// 获取实体当前生命值；生命变化只能通过伤害、治疗或初始化入口执行。
        /// </summary>
        public float Hp { get; private set; }

        /// <summary>
        /// 获取实体是否已经没有生命值。
        /// </summary>
        public bool NoHp => Hp <= 0f;

        /// <summary>获取当前 Entity 生命周期是否已经完成唯一一次存活到死亡跃迁。</summary>
        public bool IsDead => isDead;

        /// <summary>获取全部 ControlStateModifier 合并后的当前控制状态。</summary>
        public ControlState ActiveControlStates { get; private set; }

        /// <summary>获取实体是否可以执行普通攻击、转向和 AI 决策等主动行为；Stun 或受击动画存续时均不可行动。</summary>
        public bool CanAct => !isDead && !HasAnyControlState(ControlState.Stun | ControlState.Attacked);

        /// <summary>获取实体当前是否处于由受击动画生命周期持有的受击状态。</summary>
        public bool IsAttacked => HasAnyControlState(ControlState.Attacked);

        /// <summary>获取实体是否可以执行地面移动、空中横移、跳跃、闪避、巡逻或追击。</summary>
        public bool CanMove => CanAct && !HasAnyControlState(ControlState.Root);

        /// <summary>获取实体是否可以释放主动技能；普通攻击不受 Silence 单独影响。</summary>
        public bool CanUseActiveSkill => CanAct && !HasAnyControlState(ControlState.Silence);

        /// <summary>
        /// 在 Unity Start 阶段根据 Inspector 配置建立全部属性缓存，并将当前生命值初始化为 MaxHp。
        /// </summary>
        private void Start()
        {
            RefreshBaseValuesInternal();
            Hp = MaxHp;
            isDead = Hp <= 0f;
        }

        /// <summary>
        /// 重新读取当前 PropertyConfig 的全部基础值并刷新缓存，适用于运行时主动修改配置数据之后。
        /// </summary>
        public void RefreshBaseValues()
        {
            RefreshBaseValuesInternal();
            Hp = Mathf.Min(Hp, MaxHp);
        }

        /// <summary>
        /// 修改指定属性的基础值并立即更新其缓存结果；移动模式切换通过此入口更新 MoveSpeed。
        /// </summary>
        public void SetBaseValue(PropertyType type, float value)
        {
            GetProperty(type).SetBaseValue(value);
            if (type == PropertyType.MaxHp) Hp = Mathf.Min(Hp, maxHp.Value);
        }

        /// <summary>
        /// 为指定属性添加一个 Boost 或 Offset modifier，并返回用于精确移除的对象引用。
        /// </summary>
        public PropertyModifier AddModifier(PropertyType type, PropertyModifierMode mode, float value)
        {
            ModifiableProperty property = GetProperty(type);
            PropertyModifier modifier = new PropertyModifier(type, mode, value);
            property.AddModifier(modifier);
            if (type == PropertyType.MaxHp) Hp = Mathf.Min(Hp, maxHp.Value);
            return modifier;
        }

        /// <summary>
        /// 按对象身份移除指定 modifier，并在最大生命值变化时约束当前生命值不超过新的 MaxHp。
        /// </summary>
        public bool RemoveModifier(PropertyModifier modifier)
        {
            if (modifier == null) return false;
            bool removed = GetProperty(modifier.Type).RemoveModifier(modifier);
            if (removed && modifier.Type == PropertyType.MaxHp) Hp = Mathf.Min(Hp, maxHp.Value);
            return removed;
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
        /// 结算一次非负伤害、返回实际扣除生命值，并在运行模式中显示经过安全修正的预计伤害飘字。
        /// </summary>
        public float OnTakeDamage(float damage)
        {
            return OnTakeDamage(damage, out _);
        }

        /// <summary>结算一次非负伤害，显示经过安全修正的预计伤害飘字，并以原子返回值指出本次结算是否首次把目标从存活推进到死亡；返回值仍为受剩余生命值限制的实际扣血量。</summary>
        public float OnTakeDamage(float damage, out bool wasFatal)
        {
            wasFatal = false;
            if (isDead) return 0f;
            float safeDamage = Mathf.Max(0f, damage);
            float oldHp = Hp;
            Hp = Mathf.Max(0f, oldHp - safeDamage);
            float actualDamage = oldHp - Hp;
            wasFatal = oldHp > 0f && Hp <= 0f;
            if (wasFatal) isDead = true;
            if (Application.isPlaying && safeDamage > 0f) FloatTextKit.Ins.CastNumberText(safeDamage, transform.position);
            return actualDamage;
        }

        /// <summary>
        /// 结算一次非负治疗、返回实际恢复生命值，并在运行模式中显示实际治疗飘字。
        /// </summary>
        public float OnRecoverHp(float recover)
        {
            if (isDead) return 0f;
            float safeRecover = Mathf.Max(0f, recover);
            float oldHp = Hp;
            Hp = Mathf.Min(MaxHp, oldHp + safeRecover);
            float actualRecover = Hp - oldHp;
            if (Application.isPlaying) FloatTextKit.Ins.CastNumberText(actualRecover, transform.position, true);
            return actualRecover;
        }

        /// <summary>
        /// 基于缓存的 Atk、CritRate 和 CritDmg 生成一次攻击伤害。
        /// </summary>
        public float GetCalculatedDamage()
        {
            return Atk * (1f + (CritRate >= UnityEngine.Random.Range(0f, 1f) ? CritDmg : 0f));
        }

        /// <summary>
        /// 返回目标属性对象；调用方必须遵守 Unity 生命周期，在 Start 完成后使用运行时属性。
        /// </summary>
        private ModifiableProperty GetProperty(PropertyType type)
        {
            switch (type)
            {
                case PropertyType.Atk: return atk;
                case PropertyType.Def: return def;
                case PropertyType.MoveSpeed: return moveSpeed;
                case PropertyType.AtkSpeed: return atkSpeed;
                case PropertyType.CritRate: return critRate;
                case PropertyType.CritDmg: return critDmg;
                case PropertyType.MaxHp: return maxHp;
                case PropertyType.AirMoveSpeed: return airMoveSpeed;
                case PropertyType.JumpSpeed: return jumpSpeed;
                case PropertyType.Gravity: return gravity;
                case PropertyType.CoreEnergyLimit: return coreEnergyLimit;
                case PropertyType.UltEnergyLimit: return ultEnergyLimit;
                case PropertyType.Toughness: return toughness;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported property type.");
            }
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
            toughness.SetBaseValue(propConfig == null ? 0f : propConfig.toughness);
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
