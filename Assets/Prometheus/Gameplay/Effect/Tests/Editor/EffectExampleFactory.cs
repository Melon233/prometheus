using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Effects
{
    /// <summary>
    /// EffectExampleFactory 是仅供 EditMode 测试和示例资产生成菜单使用的测试工厂，正式运行时不得依赖该类型。
    /// </summary>
    public static class EffectExampleFactory
    {
        /// <summary>单次攻击伤害效果的稳定编号。</summary>
        public const string DirectDamageId = "Example.DirectAttackDamage";

        /// <summary>燃烧 DOT 效果的稳定编号。</summary>
        public const string BurningId = "Example.Burning";

        /// <summary>可叠层战意增益效果的稳定编号。</summary>
        public const string CombatFlowId = "Example.CombatFlow";

        /// <summary>眩晕控制效果的稳定编号。</summary>
        public const string StunId = "Example.Stun";

        /// <summary>
        /// 创建不会写入工程资产的完整示例对象，供 EditMode 自动化测试隔离使用。
        /// </summary>
        public static EffectExampleBundle CreateInMemory()
        {
            EffectDefinition directDamage = CreateTransient<EffectDefinition>("DirectAttackDamage");
            EffectDefinition burning = CreateTransient<EffectDefinition>("Burning");
            EffectDefinition combatFlow = CreateTransient<EffectDefinition>("CombatFlow");
            EffectDefinition stun = CreateTransient<EffectDefinition>("Stun");
            EffectTriggerSet attackTriggers = CreateTransient<EffectTriggerSet>("AttackTriggers");
            EffectTriggerSet combatFlowTriggers = CreateTransient<EffectTriggerSet>("CombatFlowTriggers");
            EffectLibrary library = CreateTransient<EffectLibrary>("EffectLibrary");
            Configure(directDamage, burning, combatFlow, stun, attackTriggers, combatFlowTriggers, library);
            return new EffectExampleBundle(directDamage, burning, combatFlow, stun, attackTriggers, combatFlowTriggers, library);
        }

        /// <summary>
        /// 将单次伤害、燃烧 DOT 和可叠层属性增益的完整规则写入指定对象。
        /// </summary>
        public static void Configure(EffectDefinition directDamage, EffectDefinition burning, EffectDefinition combatFlow, EffectDefinition stun, EffectTriggerSet attackTriggers, EffectTriggerSet combatFlowTriggers, EffectLibrary library)
        {
            if (directDamage == null || burning == null || combatFlow == null || stun == null || attackTriggers == null || combatFlowTriggers == null || library == null) throw new ArgumentNullException(nameof(directDamage), "Effect example assets must all be assigned.");
            ConfigureDirectDamage(directDamage);
            ConfigureBurning(burning);
            ConfigureCombatFlow(combatFlow);
            ConfigureStun(stun);
            ConfigureAttackTriggers(attackTriggers, directDamage, burning);
            ConfigureCombatFlowTriggers(combatFlowTriggers, combatFlow);
            ConfigureLibrary(library, directDamage, burning, combatFlow, stun, attackTriggers, combatFlowTriggers);
        }

        /// <summary>
        /// 仅在 Editor 测试程序集内通过 Unity 序列化写入 Library 的私有资产引用，避免运行时类型暴露测试专用修改接口。
        /// </summary>
        private static void ConfigureLibrary(EffectLibrary library, EffectDefinition directDamage, EffectDefinition burning, EffectDefinition combatFlow, EffectDefinition stun, EffectTriggerSet attackTriggers, EffectTriggerSet combatFlowTriggers)
        {
            SerializedObject serializedLibrary = new SerializedObject(library);
            serializedLibrary.FindProperty("directDamage").objectReferenceValue = directDamage;
            serializedLibrary.FindProperty("burning").objectReferenceValue = burning;
            serializedLibrary.FindProperty("combatFlow").objectReferenceValue = combatFlow;
            serializedLibrary.FindProperty("stun").objectReferenceValue = stun;
            serializedLibrary.FindProperty("attackTriggers").objectReferenceValue = attackTriggers;
            serializedLibrary.FindProperty("combatFlowTriggers").objectReferenceValue = combatFlowTriggers;
            serializedLibrary.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 配置按命中信号请求值造成一次即时伤害的效果，使旧攻击逻辑已经算好的暴击结果可以无损迁移。
        /// </summary>
        private static void ConfigureDirectDamage(EffectDefinition definition)
        {
            List<EffectOperation> applyOperations = new List<EffectOperation> { new DamageOperation(EffectValueFormula.SignalRequestedValue(), EffectTag.Attack, EffectValueFormula.Constant(2f)) };
            definition.ConfigureForTests(DirectDamageId, EffectTag.Attack, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, applyOperations, null, null, null);
        }

        /// <summary>
        /// 配置持续十秒、每秒造成十点伤害并由同一施法者刷新持续时间的燃烧效果。
        /// </summary>
        private static void ConfigureBurning(EffectDefinition definition)
        {
            List<EffectOperation> tickOperations = new List<EffectOperation> { new DamageOperation(EffectValueFormula.Constant(10f), EffectTag.Fire | EffectTag.Dot | EffectTag.Periodic, EffectValueFormula.Constant(0f)) };
            definition.ConfigureForTests(BurningId, EffectTag.Fire | EffectTag.Dot | EffectTag.Debuff, EffectDurationType.Duration, 10f, 1f, EffectStackPolicy.RefreshDuration, EffectStackKeyPolicy.DefinitionAndCaster, 1, EffectExecutionPhase.Apply, 20, null, null, tickOperations, null);
        }

        /// <summary>
        /// 配置持续三秒、最多五层、每层增加百分之十攻击和百分之五攻速的战意效果。
        /// </summary>
        private static void ConfigureCombatFlow(EffectDefinition definition)
        {
            List<EffectOperation> applyOperations = CreateCombatFlowPropertyOperations();
            List<EffectOperation> stackOperations = CreateCombatFlowPropertyOperations();
            definition.ConfigureForTests(CombatFlowId, EffectTag.Buff | EffectTag.Attribute, EffectDurationType.Duration, 3f, 0f, EffectStackPolicy.AddStackAndRefreshDuration, EffectStackKeyPolicy.Definition, 5, EffectExecutionPhase.AfterApply, 10, applyOperations, stackOperations, null, null);
        }

        /// <summary>
        /// 创建战意首次应用和每次叠层共用的属性修改操作。
        /// </summary>
        private static List<EffectOperation> CreateCombatFlowPropertyOperations()
        {
            return new List<EffectOperation> { new PropertyModifierOperation(PropertyType.Atk, EffectValueFormula.Constant(0.1f)), new PropertyModifierOperation(PropertyType.AtkSpeed, EffectValueFormula.Constant(0.05f)) };
        }

        /// <summary>
        /// 配置持续三秒并通过实例资源句柄禁止目标主动行为的眩晕效果。
        /// </summary>
        private static void ConfigureStun(EffectDefinition definition)
        {
            List<EffectOperation> applyOperations = new List<EffectOperation> { new ControlStateModifierOperation(ControlState.Stun) };
            definition.ConfigureForTests(StunId, EffectTag.Debuff | EffectTag.Control, EffectDurationType.Duration, 3f, 0f, EffectStackPolicy.RefreshDuration, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 30, applyOperations, null, null, null);
        }

        /// <summary>
        /// 配置命中伤害与 Fire 附加燃烧；伤害打断事件由 DamageOperation 在严格超过目标韧性时直接发布。
        /// </summary>
        private static void ConfigureAttackTriggers(EffectTriggerSet triggerSet, EffectDefinition directDamage, EffectDefinition burning)
        {
            EffectTriggerDefinition damageTrigger = new EffectTriggerDefinition();
            damageTrigger.ConfigureForTests("Example.OnAttackHit.Damage", EffectSignalType.HitConfirmed, EffectListenScope.Source, EffectTargetSelector.Target, 1f, 0f, true, 0, new[] { EffectConditionDefinition.TargetExists(), EffectConditionDefinition.HasAnyTags(EffectTag.Attack) }, new[] { directDamage });
            EffectTriggerDefinition burningTrigger = new EffectTriggerDefinition();
            burningTrigger.ConfigureForTests("Example.OnFireHit.Burning", EffectSignalType.HitConfirmed, EffectListenScope.Source, EffectTargetSelector.Target, 1f, 0f, true, 0, new[] { EffectConditionDefinition.TargetExists(), EffectConditionDefinition.HasAnyTags(EffectTag.Fire) }, new[] { burning });
            triggerSet.ConfigureForTests(new[] { damageTrigger, burningTrigger });
        }

        /// <summary>
        /// 配置造成实际攻击伤害后给来源叠加战意，同时明确排除 DOT 伤害。
        /// </summary>
        private static void ConfigureCombatFlowTriggers(EffectTriggerSet triggerSet, EffectDefinition combatFlow)
        {
            EffectTriggerDefinition combatFlowTrigger = new EffectTriggerDefinition();
            combatFlowTrigger.ConfigureForTests("Example.OnAttackDamage.CombatFlow", EffectSignalType.DamageApplied, EffectListenScope.Source, EffectTargetSelector.Source, 1f, 0f, true, 0, new[] { EffectConditionDefinition.ValueGreaterThan(0f), EffectConditionDefinition.HasAllTags(EffectTag.Attack), EffectConditionDefinition.LacksAnyTags(EffectTag.Dot) }, new[] { combatFlow });
            triggerSet.ConfigureForTests(new[] { combatFlowTrigger });
        }

        /// <summary>
        /// 创建带 HideAndDontSave 标记的临时 ScriptableObject。
        /// </summary>
        private static T CreateTransient<T>(string objectName) where T : ScriptableObject
        {
            T instance = ScriptableObject.CreateInstance<T>();
            instance.name = objectName;
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }
    }

    /// <summary>
    /// EffectExampleBundle 保存工厂创建的一整套示例对象，并在临时模式下统一销毁它们。
    /// </summary>
    public sealed class EffectExampleBundle : IDisposable
    {
        /// <summary>获取单次伤害定义。</summary>
        public EffectDefinition DirectDamage { get; }

        /// <summary>获取燃烧定义。</summary>
        public EffectDefinition Burning { get; }

        /// <summary>获取战意定义。</summary>
        public EffectDefinition CombatFlow { get; }

        /// <summary>获取眩晕定义。</summary>
        public EffectDefinition Stun { get; }

        /// <summary>获取攻击触发集合。</summary>
        public EffectTriggerSet AttackTriggers { get; }

        /// <summary>获取战意触发集合。</summary>
        public EffectTriggerSet CombatFlowTriggers { get; }

        /// <summary>获取示例库。</summary>
        public EffectLibrary Library { get; }

        /// <summary>
        /// 创建示例对象集合。
        /// </summary>
        public EffectExampleBundle(EffectDefinition directDamage, EffectDefinition burning, EffectDefinition combatFlow, EffectDefinition stun, EffectTriggerSet attackTriggers, EffectTriggerSet combatFlowTriggers, EffectLibrary library)
        {
            DirectDamage = directDamage;
            Burning = burning;
            CombatFlow = combatFlow;
            Stun = stun;
            AttackTriggers = attackTriggers;
            CombatFlowTriggers = combatFlowTriggers;
            Library = library;
        }

        /// <summary>
        /// 销毁工厂创建的临时对象；工程中的持久化资产不会由该集合销毁。
        /// </summary>
        public void Dispose()
        {
            DestroyTransient(Library);
            DestroyTransient(CombatFlowTriggers);
            DestroyTransient(AttackTriggers);
            DestroyTransient(Stun);
            DestroyTransient(CombatFlow);
            DestroyTransient(Burning);
            DestroyTransient(DirectDamage);
        }

        /// <summary>
        /// 根据当前是否处于运行模式选择安全销毁 API。
        /// </summary>
        private static void DestroyTransient(UnityEngine.Object instance)
        {
            if (instance == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(instance);
            else UnityEngine.Object.DestroyImmediate(instance);
        }
    }
}
