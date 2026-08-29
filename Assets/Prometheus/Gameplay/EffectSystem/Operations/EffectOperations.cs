using System;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// 所有效果操作的无状态基类；共享定义中的操作不得保存单次施放产生的运行时数据。
    /// </summary>
    [Serializable]
    public abstract class EffectOperation
    {
        /// <summary>
        /// 使用当前效果上下文执行原子行为。
        /// </summary>
        public abstract void Execute(EffectOperationContext context);
    }

    /// <summary>
    /// 操作上下文向原子操作提供运行时、因果信号、实体关系和当前持续效果实例。
    /// </summary>
    public sealed class EffectOperationContext
    {
        /// <summary>获取执行操作的效果运行时。</summary>
        public EffectRuntime Runtime { get; }

        /// <summary>获取当前效果定义。</summary>
        public EffectDefinition Definition { get; }

        /// <summary>获取当前持续效果实例；即时效果执行时为空。</summary>
        public EffectInstance Instance { get; }

        /// <summary>获取驱动本次操作的信号。</summary>
        public EffectSignal Signal { get; }

        /// <summary>获取直接释放当前效果的实体。</summary>
        public Xuan.Prometheus.Logic.Entity Caster { get; }

        /// <summary>获取效果目标实体。</summary>
        public Xuan.Prometheus.Logic.Entity Target { get; }

        /// <summary>获取当前效果因果链的实际源头实体。</summary>
        public Xuan.Prometheus.Logic.Entity Source { get; }

        /// <summary>获取当前层数；即时效果固定为一层。</summary>
        public int Stacks => Instance == null ? 1 : Instance.Stacks;

        /// <summary>
        /// 创建完整操作上下文。
        /// </summary>
        internal EffectOperationContext(EffectRuntime runtime, EffectDefinition definition, EffectInstance instance, EffectSignal signal, Xuan.Prometheus.Logic.Entity caster, Xuan.Prometheus.Logic.Entity target, Xuan.Prometheus.Logic.Entity source)
        {
            Runtime = runtime;
            Definition = definition;
            Instance = instance;
            Signal = signal;
            Caster = caster;
            Target = target;
            Source = source;
        }
    }

    /// <summary>
    /// 指定 DamageOperation 从信号、直接释放者角色元素或固定配置读取最终伤害属性。
    /// </summary>
    public enum DamageAttributeSource
    {
        /// <summary>继承当前 EffectSignal 已经解析的伤害属性。</summary>
        InheritSignal = 0,
        /// <summary>按当前动作类别读取直接释放者经过 Effect 覆盖后的角色出伤属性。</summary>
        CasterElement = 1,
        /// <summary>使用 DamageOperation 自己配置的固定伤害属性。</summary>
        Fixed = 2
    }

    /// <summary>
    /// DamageOperation 计算请求伤害、结算属性克制、修改目标生命值、发布 DamageApplied，并在打断能力严格超过韧性时发布受击事实。
    /// </summary>
    [Serializable]
    public sealed class DamageOperation : EffectOperation
    {
        [SerializeField] private EffectValueFormula amount = new EffectValueFormula();
        /// <summary>配置本次伤害用于对比目标韧性的打断能力；零表示只扣血而不打断。</summary>
        [SerializeField] private EffectValueFormula interruptPower = new EffectValueFormula();
        [SerializeField] private EffectTag additionalTags = EffectTag.Attack;
        /// <summary>配置当前操作解析唯一伤害属性时使用的数据来源。</summary>
        [SerializeField] private DamageAttributeSource damageAttributeSource = DamageAttributeSource.InheritSignal;
        /// <summary>仅在 Fixed 来源下使用的固定伤害属性。</summary>
        [SerializeField] private DamageAttribute fixedDamageAttribute = DamageAttribute.Physical;

        /// <summary>
        /// 创建默认伤害操作，供 Unity 序列化器使用。
        /// </summary>
        public DamageOperation()
        {
        }

        /// <summary>
        /// 创建使用指定伤害公式、标签和打断能力公式的伤害操作。
        /// </summary>
        public DamageOperation(EffectValueFormula damageAmount, EffectTag tags, EffectValueFormula damageInterruptPower = null, DamageAttributeSource attributeSource = DamageAttributeSource.InheritSignal, DamageAttribute fixedAttribute = DamageAttribute.Physical)
        {
            amount = damageAmount ?? EffectValueFormula.Constant(0f);
            additionalTags = tags;
            interruptPower = damageInterruptPower ?? EffectValueFormula.Constant(0f);
            damageAttributeSource = attributeSource;
            fixedDamageAttribute = fixedAttribute;
        }

        /// <summary>
        /// 对目标结算伤害，并将实际生命变化作为后续触发依据。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Target == null) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            float requestedDamage = Mathf.Max(0f, amount.Evaluate(context));
            float resolvedInterruptPower = Mathf.Max(0f, interruptPower.Evaluate(context));
            DamageAttribute resolvedDamageAttribute = ResolveDamageAttribute(context);
            DamageAttributeRelation damageAttributeRelation = DamageAttributeRules.GetRelation(resolvedDamageAttribute, property.ElementAttribute);
            float damageAttributeMultiplier = damageAttributeRelation == DamageAttributeRelation.Advantage ? DamageAttributeRules.AdvantageMultiplier : 1f;
            float attributedDamage = requestedDamage * damageAttributeMultiplier;
            float oldHp = property.Hp;
            float actualDamage = property.OnTakeDamage(attributedDamage, out bool wasFatal);
            PublishHealthEvents(context, property, oldHp, actualDamage, wasFatal);
            PublishStaggeredEvent(context, property, actualDamage, resolvedInterruptPower, wasFatal);
            EffectTag resultTags = context.Signal.Tags | context.Definition.Tags | additionalTags;
            EffectSignal damageSignal = context.Signal.CreateChild(EffectSignalType.DamageApplied, context.Caster, context.Target, context.Source, attributedDamage, actualDamage, resultTags, context.Signal.AbilityId, context.Instance == null ? 0L : context.Instance.InstanceId, context.Signal.Position, resolvedInterruptPower, wasFatal, resolvedDamageAttribute, context.Signal.DamageActionType, damageAttributeRelation, damageAttributeMultiplier);
            context.Runtime.EnqueueSignal(damageSignal);
            if (wasFatal) context.Runtime.EnqueueSignal(context.Signal.CreateChild(EffectSignalType.Killed, context.Caster, context.Target, context.Source, attributedDamage, actualDamage, resultTags, context.Signal.AbilityId, context.Instance == null ? 0L : context.Instance.InstanceId, context.Signal.Position, resolvedInterruptPower, true, resolvedDamageAttribute, context.Signal.DamageActionType, damageAttributeRelation, damageAttributeMultiplier));
        }

        /// <summary>
        /// 根据配置策略解析本次唯一伤害属性；缺少释放者属性组件时安全回退为物理。
        /// </summary>
        private DamageAttribute ResolveDamageAttribute(EffectOperationContext context)
        {
            if (damageAttributeSource == DamageAttributeSource.Fixed) return fixedDamageAttribute;
            if (damageAttributeSource != DamageAttributeSource.CasterElement) return context.Signal.DamageAttribute;
            if (context.Caster == null || !context.Caster.TryGetComp(out PropertyComponent casterProperty)) return DamageAttribute.Physical;
            return casterProperty.ResolveDamageAttribute(context.Signal.DamageActionType);
        }

        /// <summary>
        /// 同步发送生命变化和死亡事实事件；受击控制与表现统一由 Stun Effect 和 ControlState 驱动。
        /// </summary>
        private static void PublishHealthEvents(EffectOperationContext context, PropertyComponent property, float oldHp, float actualDamage, bool wasFatal)
        {
            if (actualDamage <= 0f) return;
            bool hasEntityEvents = context.Target.TryGetComp(out EventComponent eventComponent);
            if (hasEntityEvents) eventComponent.Invoke(new HpChangedEvent { oldHp = oldHp, newHp = property.Hp, maxHp = property.MaxHp });
            if (!wasFatal) return;
            if (hasEntityEvents) eventComponent.Invoke(new DieEvent());
            // 首次致死伤害是统一的死亡事实来源，向 Core.Event 转发实体编号供跨系统响应。
            if (context.Target.EntityId > 0 && Core.Event != null) Core.Event.Invoke(Event.EntityDied, new EntityDiedEvent(context.Target.EntityId));
        }

        /// <summary>仅在非致死实际伤害的打断能力严格大于目标韧性时发布受击事实，受击状态与结束时机交给动画会话维护。</summary>
        private static void PublishStaggeredEvent(EffectOperationContext context, PropertyComponent property, float actualDamage, float resolvedInterruptPower, bool wasFatal)
        {
            if (actualDamage <= 0f || wasFatal || resolvedInterruptPower <= property.Toughness) return;
            if (!context.Target.TryGetComp(out EventComponent eventComponent)) return;
            eventComponent.Invoke(new StaggeredEvent(actualDamage, resolvedInterruptPower, property.Toughness));
        }
    }

    /// <summary>
    /// HealOperation 计算请求治疗量、约束目标生命上限并发布携带实际治疗量的 Healed 信号。
    /// </summary>
    [Serializable]
    public sealed class HealOperation : EffectOperation
    {
        [SerializeField] private EffectValueFormula amount = new EffectValueFormula();
        [SerializeField] private EffectTag additionalTags = EffectTag.Healing;

        /// <summary>
        /// 创建默认治疗操作，供 Unity 序列化器使用。
        /// </summary>
        public HealOperation()
        {
        }

        /// <summary>
        /// 创建使用指定数值公式和标签的治疗操作。
        /// </summary>
        public HealOperation(EffectValueFormula healAmount, EffectTag tags)
        {
            amount = healAmount ?? EffectValueFormula.Constant(0f);
            additionalTags = tags;
        }

        /// <summary>
        /// 对目标结算治疗，并将实际生命变化作为后续触发依据。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Target == null) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            float requestedHeal = Mathf.Max(0f, amount.Evaluate(context));
            float oldHp = property.Hp;
            float actualHeal = property.OnRecoverHp(requestedHeal);
            PublishHpChangedEvent(context, property, oldHp, actualHeal);
            EffectTag resultTags = context.Signal.Tags | context.Definition.Tags | additionalTags;
            context.Runtime.EnqueueSignal(context.Signal.CreateChild(EffectSignalType.Healed, context.Caster, context.Target, context.Source, requestedHeal, actualHeal, resultTags, context.Signal.AbilityId, context.Instance == null ? 0L : context.Instance.InstanceId, context.Signal.Position));
        }

        /// <summary>
        /// 同步发送治疗产生的生命变化事实事件，不重复承担飘字表现职责。
        /// </summary>
        private static void PublishHpChangedEvent(EffectOperationContext context, PropertyComponent property, float oldHp, float actualHeal)
        {
            if (context.Target.TryGetComp(out EventComponent eventComponent)) eventComponent.Invoke(new HpChangedEvent { oldHp = oldHp, newHp = property.Hp, maxHp = property.MaxHp });
        }
    }

    /// <summary>
    /// ControlStateModifierOperation 在持续效果实例中保存控制状态句柄，并在实例移除时自动精确回滚该来源的状态贡献。
    /// </summary>
    [Serializable]
    public sealed class ControlStateModifierOperation : EffectOperation
    {
        /// <summary>指定本操作向目标贡献的一个或多个控制状态。</summary>
        [SerializeField] private ControlState states = ControlState.Stun;

        /// <summary>创建默认施加 Stun 的控制状态操作，供 Unity 序列化器使用。</summary>
        public ControlStateModifierOperation()
        {
        }

        /// <summary>创建施加指定组合状态的控制状态操作。</summary>
        public ControlStateModifierOperation(ControlState controlStates)
        {
            states = controlStates;
        }

        /// <summary>
        /// 为持续效果目标创建状态 Modifier；Attacked 专属于动画生命周期会被排除，即时效果也不会施加永久控制。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            ControlState effectOwnedStates = states & ~ControlState.Attacked;
            if (context.Instance == null || context.Target == null || effectOwnedStates == ControlState.None) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            context.Instance.SetResource(BuildAutomaticKey(effectOwnedStates), new EffectControlStateModifierHandle(property, effectOwnedStates));
        }

        /// <summary>根据状态组合生成同一 EffectInstance 内稳定且可读的资源键。</summary>
        public static string BuildAutomaticKey(ControlState controlStates)
        {
            return $"ControlStateModifier:{controlStates}";
        }
    }

    /// <summary>
    /// 指定 PropertyModifierOperation 使用属性与模式自动生成资源键，还是使用高级自定义键。
    /// </summary>
    public enum PropertyModifierKeyPolicy
    {
        /// <summary>根据 PropertyType 与 PropertyModifierMode 自动生成稳定资源键。</summary>
        Automatic = 0,
        /// <summary>使用配置者提供的自定义资源键，以支持相同属性和模式的多个独立通道。</summary>
        Custom = 1
    }

    /// <summary>
    /// PropertyModifierOperation 通过实例资源句柄应用属性 Boost 或 Offset，叠层时替换旧值，移除实例时自动回滚。
    /// </summary>
    [Serializable]
    [MovedFrom(true, sourceNamespace: "Xuan.Prometheus.Effects", sourceAssembly: "Runtime", sourceClassName: "StatModifierOperation")]
    public sealed class PropertyModifierOperation : EffectOperation
    {
        /// <summary>控制当前操作使用自动资源键还是高级自定义键。</summary>
        [SerializeField] private PropertyModifierKeyPolicy keyPolicy = PropertyModifierKeyPolicy.Automatic;
        /// <summary>仅在 Custom 策略下使用；FormerlySerializedAs 保证旧资产的 modifierKey 可以安全迁移。</summary>
        [SerializeField, FormerlySerializedAs("modifierKey")] private string customModifierKey;
        /// <summary>
        /// 指定该操作需要修改的目标属性。
        /// </summary>
        [SerializeField, FormerlySerializedAs("statType")] private PropertyType propertyType;
        /// <summary>
        /// 指定该操作写入目标属性的 Boost 还是 Offset。
        /// </summary>
        [SerializeField] private PropertyModifierMode modifierMode;
        /// <summary>定义每一层效果对目标属性贡献的 Boost 或 Offset 数值。</summary>
        [SerializeField] private EffectValueFormula valuePerStack = EffectValueFormula.Constant(0f);

        /// <summary>
        /// 创建默认属性修改操作，供 Unity 序列化器使用。
        /// </summary>
        public PropertyModifierOperation()
        {
        }

        /// <summary>
        /// 创建使用属性类型和 Boost 模式自动生成资源键的每层属性修改操作。
        /// </summary>
        public PropertyModifierOperation(PropertyType type, EffectValueFormula perStackValue) : this(type, PropertyModifierMode.Boost, perStackValue)
        {
        }

        /// <summary>
        /// 创建使用属性类型和修改模式自动生成资源键的每层属性修改操作。
        /// </summary>
        public PropertyModifierOperation(PropertyType type, PropertyModifierMode mode, EffectValueFormula perStackValue)
        {
            propertyType = type;
            modifierMode = mode;
            valuePerStack = perStackValue ?? EffectValueFormula.Constant(0f);
        }

        /// <summary>
        /// 创建使用高级自定义资源键的每层属性修改操作；空键会安全回退到自动规则。
        /// </summary>
        public PropertyModifierOperation(string customKey, PropertyType type, PropertyModifierMode mode, EffectValueFormula perStackValue) : this(type, mode, perStackValue)
        {
            keyPolicy = string.IsNullOrWhiteSpace(customKey) ? PropertyModifierKeyPolicy.Automatic : PropertyModifierKeyPolicy.Custom;
            customModifierKey = customKey ?? string.Empty;
        }

        /// <summary>
        /// 使用当前层数重新计算修改量，并用新句柄原子替换旧句柄。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Instance == null || context.Target == null) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            float value = valuePerStack.Evaluate(context) * context.Stacks;
            context.Instance.SetResource(ResolveModifierKey(), new EffectPropertyModifierHandle(property, propertyType, modifierMode, value));
        }

        /// <summary>
        /// 根据策略解析当前实例资源键；Custom 未填写时仍回退到安全的自动键。
        /// </summary>
        private string ResolveModifierKey()
        {
            if (keyPolicy == PropertyModifierKeyPolicy.Custom && !string.IsNullOrWhiteSpace(customModifierKey)) return customModifierKey.Trim();
            return BuildAutomaticKey(propertyType, modifierMode);
        }

        /// <summary>
        /// 使用属性类型与修改通道生成同一 EffectInstance 内稳定且可读的资源键。
        /// </summary>
        public static string BuildAutomaticKey(PropertyType type, PropertyModifierMode mode)
        {
            return $"PropertyModifier:{type}:{mode}";
        }
    }

    /// <summary>
    /// DamageAttributeModifierOperation 为持续 Effect 登记指定动作范围的出伤属性覆盖，并由实例资源生命周期自动回滚。
    /// </summary>
    [Serializable]
    public sealed class DamageAttributeModifierOperation : EffectOperation
    {
        /// <summary>配置 Effect 生效期间覆盖后的出伤属性。</summary>
        [SerializeField] private DamageAttribute damageAttribute = DamageAttribute.Physical;
        /// <summary>配置当前覆盖能够影响的伤害动作范围。</summary>
        [SerializeField] private DamageActionMask actionMask = DamageActionMask.All;
        /// <summary>配置覆盖优先级；数值越大越优先。</summary>
        [SerializeField] private int priority;

        /// <summary>创建默认的全动作物理属性覆盖，供 Unity 序列化器使用。</summary>
        public DamageAttributeModifierOperation()
        {
        }

        /// <summary>创建使用指定伤害属性、动作范围和覆盖优先级的操作。</summary>
        public DamageAttributeModifierOperation(DamageAttribute attribute, DamageActionMask mask, int modifierPriority = 0)
        {
            damageAttribute = attribute;
            actionMask = mask;
            priority = modifierPriority;
        }

        /// <summary>
        /// 为持续效果目标登记属性覆盖；即时效果没有实例资源生命周期，因此不会留下永久覆盖。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Instance == null || context.Target == null || actionMask == DamageActionMask.None) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            context.Instance.SetResource(BuildResourceKey(actionMask), new EffectDamageAttributeModifierHandle(property, damageAttribute, actionMask, priority));
        }

        /// <summary>按动作范围生成同一 EffectInstance 内稳定的覆盖资源键。</summary>
        public static string BuildResourceKey(DamageActionMask mask)
        {
            return $"DamageAttributeModifier:{mask}";
        }
    }

    /// <summary>
    /// ApplyEffectOperation 允许一个效果按配置请求另一个效果，同时仍然经过统一队列和递归保护。
    /// </summary>
    [Serializable]
    public sealed class ApplyEffectOperation : EffectOperation
    {
        [SerializeField] private EffectDefinition effect;
        [SerializeField] private EffectTargetSelector targetSelector = EffectTargetSelector.Target;
        [SerializeField] private int priorityOffset;

        /// <summary>
        /// 创建默认二次效果操作，供 Unity 序列化器使用。
        /// </summary>
        public ApplyEffectOperation()
        {
        }

        /// <summary>
        /// 创建一个通过选择器请求二次效果的操作。
        /// </summary>
        public ApplyEffectOperation(EffectDefinition definition, EffectTargetSelector selector, int requestPriorityOffset = 0)
        {
            effect = definition;
            targetSelector = selector;
            priorityOffset = requestPriorityOffset;
        }

        /// <summary>
        /// 将二次效果加入当前事务队列，避免在操作内部递归执行。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (effect == null) return;
            Xuan.Prometheus.Logic.Entity target = EffectRuntime.SelectTarget(context.Signal, targetSelector);
            context.Runtime.EnqueueEffect(effect, context.Caster, target, context.Source, context.Signal, priorityOffset);
        }
    }

    /// <summary>
    /// EmitSignalOperation 用配置产生新的语义信号，从而连接不直接相互引用的触发规则。
    /// </summary>
    [Serializable]
    public sealed class EmitSignalOperation : EffectOperation
    {
        [SerializeField] private EffectSignalType signalType = EffectSignalType.Manual;
        [SerializeField] private EffectValueFormula value = new EffectValueFormula();
        [SerializeField] private EffectTag additionalTags;

        /// <summary>
        /// 创建默认发信号操作，供 Unity 序列化器使用。
        /// </summary>
        public EmitSignalOperation()
        {
        }

        /// <summary>
        /// 创建使用指定信号类型、数值公式和标签的操作。
        /// </summary>
        public EmitSignalOperation(EffectSignalType type, EffectValueFormula signalValue, EffectTag tags)
        {
            signalType = type;
            value = signalValue ?? EffectValueFormula.Constant(0f);
            additionalTags = tags;
        }

        /// <summary>
        /// 创建同一 SignalChainId 下的子信号并追加到信号队列。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            float resolvedValue = value.Evaluate(context);
            EffectTag resultTags = context.Signal.Tags | context.Definition.Tags | additionalTags;
            context.Runtime.EnqueueSignal(context.Signal.CreateChild(signalType, context.Caster, context.Target, context.Source, resolvedValue, resolvedValue, resultTags, context.Signal.AbilityId, context.Instance == null ? 0L : context.Instance.InstanceId, context.Signal.Position));
        }
    }

    /// <summary>
    /// EffectPropertyModifierHandle 精确记录单个效果实例贡献的属性值，并保证 Dispose 幂等。
    /// </summary>
    internal sealed class EffectPropertyModifierHandle : IDisposable
    {
        private readonly PropertyComponent property;
        private readonly PropertyType propertyType;
        private readonly PropertyModifierMode modifierMode;
        private readonly PropertyModifier modifier;
        private bool disposed;

        /// <summary>
        /// 创建句柄时立即应用属性变化。
        /// </summary>
        public EffectPropertyModifierHandle(PropertyComponent targetProperty, PropertyType targetPropertyType, PropertyModifierMode targetModifierMode, float modifierValue)
        {
            property = targetProperty;
            propertyType = targetPropertyType;
            modifierMode = targetModifierMode;
            modifier = AddValue(modifierValue);
        }

        /// <summary>
        /// 首次释放时准确撤销该句柄应用的属性变化。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (property == null || modifier == null) return;
            property.RemoveModifier(modifier);
        }

        /// <summary>
        /// 将计算结果登记到目标属性的 Boost 或 Offset 通道，并返回用于精确移除的 modifier 引用。
        /// </summary>
        private PropertyModifier AddValue(float modifierValue)
        {
            if (property == null) return null;
            return property.AddModifier(propertyType, modifierMode, modifierValue);
        }
    }

    /// <summary>
    /// EffectDamageAttributeModifierHandle 精确拥有一份出伤属性覆盖，并保证 Effect 移除时只撤销自身贡献。
    /// </summary>
    internal sealed class EffectDamageAttributeModifierHandle : IDisposable
    {
        private readonly PropertyComponent property;
        private readonly DamageAttributeModifier modifier;
        private bool disposed;

        /// <summary>创建句柄时立即向目标 PropertyComponent 登记出伤属性覆盖。</summary>
        public EffectDamageAttributeModifierHandle(PropertyComponent targetProperty, DamageAttribute attribute, DamageActionMask actionMask, int priority)
        {
            property = targetProperty;
            modifier = property == null ? null : property.AddDamageAttributeModifier(attribute, actionMask, priority);
        }

        /// <summary>首次释放时按对象身份移除当前 Effect 持有的属性覆盖。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (property == null || modifier == null) return;
            property.RemoveDamageAttributeModifier(modifier);
        }
    }

    /// <summary>
    /// EffectControlStateModifierHandle 精确记录单个效果实例贡献的控制状态，并保证释放操作幂等。
    /// </summary>
    internal sealed class EffectControlStateModifierHandle : IDisposable
    {
        private readonly PropertyComponent property;
        private readonly ControlStateModifier modifier;
        private bool disposed;

        /// <summary>创建句柄时立即把状态 Modifier 登记到目标 PropertyComponent。</summary>
        public EffectControlStateModifierHandle(PropertyComponent targetProperty, ControlState states)
        {
            property = targetProperty;
            modifier = property == null ? null : property.AddControlStateModifier(states);
        }

        /// <summary>首次释放时只撤销当前句柄拥有的状态贡献，不影响其他来源的同名状态。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (property == null || modifier == null) return;
            property.RemoveControlStateModifier(modifier);
        }
    }

    /// <summary>按照配置公式调整目标核心能量；正数增加、负数扣除，最终值由 PropertyComponent 约束到运行时范围。</summary>
    [Serializable]
    public sealed class CoreEnergyGainOperation : EffectOperation
    {
        /// <summary>保存本次核心能量有符号变化量的计算公式。</summary>
        [SerializeField] private EffectValueFormula amount = new EffectValueFormula();

        /// <summary>
        /// 创建默认核心能量变化操作，供 Unity 序列化器使用。
        /// </summary>
        public CoreEnergyGainOperation()
        {
        }

        /// <summary>
        /// 创建使用指定数值公式的核心能量变化操作；公式可以返回正数或负数。
        /// </summary>
        public CoreEnergyGainOperation(EffectValueFormula changeAmount)
        {
            amount = changeAmount ?? EffectValueFormula.Constant(0f);
        }

        /// <summary>
        /// 对目标结算有符号核心能量变化，PropertyComponent 会将结果约束在零到运行时上限之间。
        /// </summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Target == null) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            float requestedChange = amount.Evaluate(context);
            property.OnGainCoreEnergy(requestedChange);
        }

    }

    /// <summary>按照配置公式增加目标的大招能量，并由 ModifiableProperty 向监听方发布脏通知。</summary>
    [Serializable]
    public sealed class UltEnergyGainOperation : EffectOperation
    {
        [SerializeField] private EffectValueFormula amount = new EffectValueFormula();

        /// <summary>创建默认大招能量操作，供 Unity SerializeReference 实例化。</summary>
        public UltEnergyGainOperation()
        {
        }

        /// <summary>创建使用指定数值公式的大招能量操作。</summary>
        public UltEnergyGainOperation(EffectValueFormula gainAmount)
        {
            amount = gainAmount ?? EffectValueFormula.Constant(0f);
        }

        /// <summary>增加目标大招能量；没有实际变化时属性不会触发脏回调。</summary>
        public override void Execute(EffectOperationContext context)
        {
            if (context.Target == null) return;
            if (!context.Target.TryGetComp(out PropertyComponent property)) return;
            float requestedGain = Mathf.Max(0f, amount.Evaluate(context));
            property.OnGainUltEnergy(requestedGain);
        }
    }
}
