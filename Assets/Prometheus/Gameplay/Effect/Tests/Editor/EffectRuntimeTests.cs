using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Effects.Tests
{
    /// <summary>
    /// EffectRuntimeTests 验证单次伤害、燃烧 DOT、可叠层属性增益和控制状态能在同一事务模型中正确组合。
    /// </summary>
    public sealed class EffectRuntimeTests
    {
        private GameObject sourceObject;
        private GameObject targetObject;
        private PropertyConfig sourceConfig;
        private PropertyConfig targetConfig;
        private PropertyComponent sourceProperty;
        private PropertyComponent targetProperty;
        private TestEntity sourceEntity;
        private TestEntity targetEntity;
        private EffectRuntime runtime;
        private EffectExampleBundle examples;
        private IDisposable registrations;

        /// <summary>
        /// 为每个测试创建彼此隔离的实体、属性、效果定义和确定性运行时。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            sourceObject = new GameObject("EffectTest.Source");
            targetObject = new GameObject("EffectTest.Target");
            sourceConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            targetConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            sourceConfig.atk = 20f;
            sourceConfig.def = 10f;
            sourceConfig.runSpeed = 3f;
            sourceConfig.toughness = 1f;
            sourceConfig.hp = 100f;
            targetConfig.atk = 5f;
            targetConfig.toughness = 1f;
            targetConfig.hp = 100f;
            sourceProperty = sourceObject.AddComponent<PropertyComponent>();
            targetProperty = targetObject.AddComponent<PropertyComponent>();
            sourceProperty.InitializeForTests(sourceConfig);
            targetProperty.InitializeForTests(targetConfig);
            sourceEntity = new TestEntity(sourceObject, sourceProperty);
            targetEntity = new TestEntity(targetObject, targetProperty);
            runtime = new EffectRuntime(12345);
            examples = EffectExampleFactory.CreateInMemory();
        }

        /// <summary>
        /// 按依赖顺序释放运行时、临时 ScriptableObject 和 GameObject，确保属性句柄先完成回滚。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            registrations?.Dispose();
            registrations = null;
            runtime?.Dispose();
            runtime = null;
            examples?.Dispose();
            examples = null;
            UnityEngine.Object.DestroyImmediate(sourceConfig);
            UnityEngine.Object.DestroyImmediate(targetConfig);
            UnityEngine.Object.DestroyImmediate(sourceObject);
            UnityEngine.Object.DestroyImmediate(targetObject);
        }

        /// <summary>
        /// 验证普通攻击使用命中信号已经算好的请求伤害，而不是在效果内部重新读取攻击力。
        /// </summary>
        [Test]
        public void DirectAttackDamage_IsInstantAndUsesSignalRequestedValue()
        {
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            HpChangedEvent observedHpChange = null;
            int staggeredCount = 0;
            targetEvents.AddListener<HpChangedEvent>(change => observedHpChange = change);
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 37f, 37f, EffectTag.Attack | EffectTag.NormalAttack));
            Assert.That(targetProperty.Hp, Is.EqualTo(63f).Within(0.0001f));
            Assert.That(observedHpChange, Is.Not.Null);
            Assert.That(observedHpChange.oldHp, Is.EqualTo(100f).Within(0.0001f));
            Assert.That(observedHpChange.newHp, Is.EqualTo(63f).Within(0.0001f));
            Assert.That(observedHpChange.maxHp, Is.EqualTo(100f).Within(0.0001f));
            Assert.That(staggeredCount, Is.EqualTo(1), "正式默认直接伤害的打断能力 2 严格超过韧性 1，因此必须触发受击事件。");
            Assert.That(runtime.GetActiveEffects(targetEntity), Is.Empty, "即时伤害和受击事件都不得留下持续 Effect 实例。");
        }

        /// <summary>
        /// 验证五行单向克制、光暗互克和物理中立组成唯一克制矩阵，所有未列出的组合都不得产生减伤或额外增伤。
        /// </summary>
        [Test]
        public void DamageAttributeRules_ApplyOnlyConfiguredAdvantages()
        {
            Array attributes = Enum.GetValues(typeof(DamageAttribute));
            foreach (DamageAttribute attackAttribute in attributes)
            {
                foreach (DamageAttribute targetAttribute in attributes)
                {
                    bool expectedAdvantage = attackAttribute == DamageAttribute.Fire && targetAttribute == DamageAttribute.Ice || attackAttribute == DamageAttribute.Ice && targetAttribute == DamageAttribute.Grass || attackAttribute == DamageAttribute.Grass && targetAttribute == DamageAttribute.Lightning || attackAttribute == DamageAttribute.Lightning && targetAttribute == DamageAttribute.Water || attackAttribute == DamageAttribute.Water && targetAttribute == DamageAttribute.Fire || attackAttribute == DamageAttribute.Light && targetAttribute == DamageAttribute.Dark || attackAttribute == DamageAttribute.Dark && targetAttribute == DamageAttribute.Light;
                    DamageAttributeRelation expectedRelation = expectedAdvantage ? DamageAttributeRelation.Advantage : DamageAttributeRelation.Neutral;
                    float expectedMultiplier = expectedAdvantage ? DamageAttributeRules.AdvantageMultiplier : 1f;
                    Assert.That(DamageAttributeRules.GetRelation(attackAttribute, targetAttribute), Is.EqualTo(expectedRelation), $"Unexpected relation for {attackAttribute} -> {targetAttribute}.");
                    Assert.That(DamageAttributeRules.GetMultiplier(attackAttribute, targetAttribute), Is.EqualTo(expectedMultiplier).Within(0.0001f), $"Unexpected multiplier for {attackAttribute} -> {targetAttribute}.");
                }
            }
        }

        /// <summary>
        /// 验证普攻与特殊攻击从物理开始，技能与大招读取角色元素，并由最高优先级和同优先级后加入的 Effect 覆盖。
        /// </summary>
        [Test]
        public void DamageAttributeResolution_UsesActionDefaultsAndDeterministicModifiers()
        {
            sourceConfig.elementAttribute = DamageAttribute.Water;
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.NormalAttack), Is.EqualTo(DamageAttribute.Physical));
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.SpecialAttack), Is.EqualTo(DamageAttribute.Physical));
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Water));
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Ultimate), Is.EqualTo(DamageAttribute.Water));
            DamageAttributeModifier allFire = sourceProperty.AddDamageAttributeModifier(DamageAttribute.Fire, DamageActionMask.All, 1);
            DamageAttributeModifier skillDark = sourceProperty.AddDamageAttributeModifier(DamageAttribute.Dark, DamageActionMask.Skill, 10);
            DamageAttributeModifier skillLight = sourceProperty.AddDamageAttributeModifier(DamageAttribute.Light, DamageActionMask.Skill, 10);
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.NormalAttack), Is.EqualTo(DamageAttribute.Fire));
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Light));
            Assert.That(sourceProperty.RemoveDamageAttributeModifier(skillLight), Is.True);
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Dark));
            Assert.That(sourceProperty.RemoveDamageAttributeModifier(skillDark), Is.True);
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Fire));
            Assert.That(sourceProperty.RemoveDamageAttributeModifier(allFire), Is.True);
            Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Water));
        }

        /// <summary>
        /// 验证 DamageOperation 只应用一次克制独立乘区，并在 DamageApplied 中携带最终属性、关系、倍率和实际伤害。
        /// </summary>
        [Test]
        public void DamageOperation_AppliesAttributeAdvantageOnceAndPublishesResolvedContext()
        {
            targetConfig.elementAttribute = DamageAttribute.Ice;
            DamageSignalCaptureOperation capture = new DamageSignalCaptureOperation();
            EffectDefinition damageEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectDefinition captureEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectTriggerSet triggerSet = ScriptableObject.CreateInstance<EffectTriggerSet>();
            damageEffect.name = "Tests.FireDamage";
            captureEffect.name = "Tests.CaptureDamageApplied";
            triggerSet.name = "Tests.CaptureDamageAppliedSet";
            damageEffect.ConfigureForTests("Tests.FireDamage", EffectTag.Attack, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new DamageOperation(EffectValueFormula.Constant(10f), EffectTag.Attack, EffectValueFormula.Constant(0f), DamageAttributeSource.Fixed, DamageAttribute.Fire) }, null, null, null);
            captureEffect.ConfigureForTests("Tests.CaptureDamageApplied", EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.AfterApply, 0, new EffectOperation[] { capture }, null, null, null);
            EffectTriggerDefinition captureTrigger = new EffectTriggerDefinition();
            captureTrigger.ConfigureForTests("Tests.OnDamageApplied.Capture", EffectSignalType.DamageApplied, EffectListenScope.Target, EffectTargetSelector.Target, 1f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), new[] { captureEffect });
            triggerSet.ConfigureForTests(new[] { captureTrigger });
            IDisposable captureRegistration = runtime.RegisterTriggerSet(targetEntity, triggerSet);
            try
            {
                runtime.ApplyEffect(damageEffect, sourceEntity, targetEntity, sourceEntity);
                Assert.That(targetProperty.Hp, Is.EqualTo(87f).Within(0.0001f));
                Assert.That(capture.Signal, Is.Not.Null);
                Assert.That(capture.Signal.RequestedValue, Is.EqualTo(13f).Within(0.0001f));
                Assert.That(capture.Signal.Value, Is.EqualTo(13f).Within(0.0001f));
                Assert.That(capture.Signal.DamageAttribute, Is.EqualTo(DamageAttribute.Fire));
                Assert.That(capture.Signal.DamageActionType, Is.EqualTo(DamageActionType.Effect));
                Assert.That(capture.Signal.DamageAttributeRelation, Is.EqualTo(DamageAttributeRelation.Advantage));
                Assert.That(capture.Signal.DamageAttributeMultiplier, Is.EqualTo(1.3f).Within(0.0001f));
            }
            finally
            {
                captureRegistration.Dispose();
                UnityEngine.Object.DestroyImmediate(triggerSet);
                UnityEngine.Object.DestroyImmediate(captureEffect);
                UnityEngine.Object.DestroyImmediate(damageEffect);
            }
        }

        /// <summary>
        /// 验证持续 Effect 的伤害属性覆盖只影响配置动作范围，并在效果移除时自动恢复角色原有解析结果。
        /// </summary>
        [Test]
        public void DamageAttributeModifierEffect_AppliesByScopeAndRollsBackOnRemove()
        {
            sourceConfig.elementAttribute = DamageAttribute.Water;
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.FireNormalAttackInfusion";
            definition.ConfigureForTests("Tests.FireNormalAttackInfusion", EffectTag.Buff | EffectTag.Attribute, EffectDurationType.Permanent, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new DamageAttributeModifierOperation(DamageAttribute.Fire, DamageActionMask.NormalAttack, 5) }, null, null, null);
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, sourceEntity, sourceEntity);
                Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.NormalAttack), Is.EqualTo(DamageAttribute.Fire));
                Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Water));
                runtime.RemoveAll(sourceEntity);
                Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.NormalAttack), Is.EqualTo(DamageAttribute.Physical));
                Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Water));
            }
            finally
            {
                runtime.RemoveAll(sourceEntity);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证伤害 Effect 从施法者读取技能属性时会使用持续 Effect 的覆盖结果，并在克制目标时进入独立 1.3 倍乘区。
        /// </summary>
        [Test]
        public void CasterElementDamage_UsesEffectModifiedSkillAttribute()
        {
            sourceConfig.elementAttribute = DamageAttribute.Water;
            targetConfig.elementAttribute = DamageAttribute.Lightning;
            EffectDefinition infusionEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectDefinition damageEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectTriggerSet triggerSet = ScriptableObject.CreateInstance<EffectTriggerSet>();
            infusionEffect.name = "Tests.GrassSkillInfusion";
            damageEffect.name = "Tests.CasterElementDamage";
            triggerSet.name = "Tests.CasterElementDamageSet";
            infusionEffect.ConfigureForTests("Tests.GrassSkillInfusion", EffectTag.Buff | EffectTag.Attribute, EffectDurationType.Permanent, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new DamageAttributeModifierOperation(DamageAttribute.Grass, DamageActionMask.Skill, 10) }, null, null, null);
            damageEffect.ConfigureForTests("Tests.CasterElementDamage", EffectTag.Attack | EffectTag.Skill, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new DamageOperation(EffectValueFormula.SignalRequestedValue(), EffectTag.Attack | EffectTag.Skill, EffectValueFormula.Constant(0f), DamageAttributeSource.CasterElement) }, null, null, null);
            EffectTriggerDefinition damageTrigger = new EffectTriggerDefinition();
            damageTrigger.ConfigureForTests("Tests.OnSkillHit.CasterElementDamage", EffectSignalType.HitConfirmed, EffectListenScope.Caster, EffectTargetSelector.Target, 1f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), new[] { damageEffect });
            triggerSet.ConfigureForTests(new[] { damageTrigger });
            IDisposable damageRegistration = runtime.RegisterTriggerSet(sourceEntity, triggerSet);
            try
            {
                runtime.ApplyEffect(infusionEffect, sourceEntity, sourceEntity, sourceEntity);
                runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack | EffectTag.Skill, "Tests.Skill", damageAttribute: DamageAttribute.Physical, damageActionType: DamageActionType.Skill));
                Assert.That(sourceProperty.ResolveDamageAttribute(DamageActionType.Skill), Is.EqualTo(DamageAttribute.Grass));
                Assert.That(targetProperty.Hp, Is.EqualTo(87f).Within(0.0001f));
            }
            finally
            {
                damageRegistration.Dispose();
                runtime.RemoveAll(sourceEntity);
                UnityEngine.Object.DestroyImmediate(triggerSet);
                UnityEngine.Object.DestroyImmediate(damageEffect);
                UnityEngine.Object.DestroyImmediate(infusionEffect);
            }
        }

        /// <summary>验证首次致死伤害只发布一次 DieEvent 和 Killed Signal，后续命中与治疗都不能让尸体再次结算死亡。</summary>
        [Test]
        public void FatalDamage_EmitsDeathAndKilledExactlyOnce()
        {
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int deathCount = 0;
            int hpChangedCount = 0;
            int staggeredCount = 0;
            int fatalDamageSignalCount = 0;
            EffectSignal fatalDamageSignal = null;
            targetEvents.AddListener<DieEvent>(_ => deathCount++);
            targetEvents.AddListener<HpChangedEvent>(_ => hpChangedCount++);
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            runtime.SignalProcessed += signal =>
            {
                if (signal.Type != EffectSignalType.DamageApplied || signal.Value <= 0f) return;
                fatalDamageSignalCount++;
                fatalDamageSignal = signal;
            };
            CountingOperation killedCounter = new CountingOperation();
            EffectDefinition killedCounterEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            killedCounterEffect.name = "Tests.KilledCounterEffect";
            killedCounterEffect.ConfigureForTests("Tests.KilledCounterEffect", EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.AfterApply, 0, new EffectOperation[] { killedCounter }, null, null, null);
            EffectTriggerDefinition killedTrigger = new EffectTriggerDefinition();
            killedTrigger.ConfigureForTests("Tests.OnKilled.Count", EffectSignalType.Killed, EffectListenScope.Caster, EffectTargetSelector.Caster, 1f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), new[] { killedCounterEffect });
            EffectTriggerSet killedTriggerSet = ScriptableObject.CreateInstance<EffectTriggerSet>();
            killedTriggerSet.name = "Tests.KilledTriggerSet";
            killedTriggerSet.ConfigureForTests(new[] { killedTrigger });
            IDisposable killedRegistration = runtime.RegisterTriggerSet(sourceEntity, killedTriggerSet);
            try
            {
                EffectSignal fatalHit = new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 150f, 150f, EffectTag.Attack | EffectTag.NormalAttack);
                runtime.Publish(fatalHit);
                runtime.Publish(fatalHit);
                Assert.That(targetProperty.Hp, Is.EqualTo(0f));
                Assert.That(targetProperty.IsDead, Is.True);
                Assert.That(deathCount, Is.EqualTo(1));
                Assert.That(hpChangedCount, Is.EqualTo(1));
                Assert.That(staggeredCount, Is.Zero, "致死伤害不得发布受击表现事件。");
                Assert.That(fatalDamageSignalCount, Is.EqualTo(1), "首次致命伤害必须产生一条可供独立音频表现消费的实际伤害事实。");
                Assert.That(fatalDamageSignal, Is.Not.Null);
                Assert.That(fatalDamageSignal.WasFatal, Is.True);
                Assert.That(fatalDamageSignal.Value, Is.EqualTo(100f).Within(0.0001f));
                Assert.That(killedCounter.ExecutionCount, Is.EqualTo(1));
                Assert.That(runtime.GetActiveEffects(targetEntity), Is.Empty, "致死伤害不得创建受击或控制 Effect。");
                Assert.That(targetProperty.OnRecoverHp(50f), Is.EqualTo(0f));
                Assert.That(targetProperty.Hp, Is.EqualTo(0f));
            }
            finally
            {
                killedRegistration.Dispose();
                UnityEngine.Object.DestroyImmediate(killedTriggerSet);
                UnityEngine.Object.DestroyImmediate(killedCounterEffect);
            }
        }

        /// <summary>
        /// 验证严格超过韧性的实际伤害只发布受击事件，重复伤害重复发布，并且不会创建 Stun Effect。
        /// </summary>
        [Test]
        public void HitReaction_QualifyingDamagePublishesEveryTimeWithoutStunEffect()
        {
            targetProperty.SetBaseValue(PropertyType.Toughness, 1f);
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int stateChangeCount = 0;
            int staggeredCount = 0;
            targetEvents.AddListener<ControlStateChangedEvent>(_ => stateChangeCount++);
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 1f, 1f, EffectTag.Attack | EffectTag.NormalAttack));
            Assert.That(staggeredCount, Is.EqualTo(1));
            runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 1f, 1f, EffectTag.Attack | EffectTag.NormalAttack));
            Assert.That(staggeredCount, Is.EqualTo(2), "每次严格超过韧性的实际伤害都必须独立发布受击事件。");
            Assert.That(runtime.GetStackCount(targetEntity, EffectExampleFactory.StunId), Is.Zero);
            Assert.That(runtime.GetActiveEffects(targetEntity), Is.Empty);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
            Assert.That(targetProperty.CanAct, Is.True);
            Assert.That(targetProperty.CanMove, Is.True);
            Assert.That(targetProperty.CanUseActiveSkill, Is.True);
            Assert.That(stateChangeCount, Is.Zero, "没有 AttackedLogic 的纯 Effect 测试实体只接收事实事件，不应由伤害系统写入控制状态。");
        }

        /// <summary>验证打断能力低于、等于和高于韧性的三个边界，只有严格高于时才发布受击事件。</summary>
        [TestCase(2.01f, false)]
        [TestCase(2f, false)]
        [TestCase(1.5f, true)]
        public void HitReaction_InterruptPowerMustStrictlyExceedTargetToughness(float toughness, bool shouldReact)
        {
            targetProperty.SetBaseValue(PropertyType.Toughness, toughness);
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int staggeredCount = 0;
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack | EffectTag.NormalAttack));
            Assert.That(staggeredCount > 0, Is.EqualTo(shouldReact));
            Assert.That(runtime.GetStackCount(targetEntity, EffectExampleFactory.StunId), Is.Zero);
        }

        /// <summary>验证配置了打断能力但实际伤害为零时不发布受击事件。</summary>
        [Test]
        public void HitReaction_ZeroActualDamageDoesNotReact()
        {
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int staggeredCount = 0;
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            runtime.Publish(new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, 0f, 0f, EffectTag.Attack | EffectTag.NormalAttack));
            Assert.That(staggeredCount, Is.Zero);
        }

        /// <summary>验证打断能力为零的 DOT 即使造成实际伤害也不会发布受击事件。</summary>
        [Test]
        public void HitReaction_DotWithZeroInterruptPowerDoesNotReact()
        {
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int staggeredCount = 0;
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            runtime.ApplyEffect(examples.Burning, sourceEntity, targetEntity, sourceEntity);
            runtime.Tick(1.01f);
            Assert.That(targetProperty.Hp, Is.EqualTo(90f).Within(0.0001f));
            Assert.That(staggeredCount, Is.Zero);
        }

        /// <summary>
        /// 验证普通实体增加核心能量不依赖全局 EventKit，EffectRuntime 可以在独立测试和非玩家实体上安全运行。
        /// </summary>
        [Test]
        public void CoreEnergyGain_NonPlayerTargetDoesNotRequireGlobalEventKit()
        {
            IEventKit previousEventKit = Core.Event;
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.CoreEnergyGain";
            definition.ConfigureForTests("Tests.CoreEnergyGain", EffectTag.Buff | EffectTag.CoreEnergyGain, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new CoreEnergyGainOperation(EffectValueFormula.Constant(20f)) }, null, null, null);
            try
            {
                Core.Event = null;
                targetProperty.SetBaseValue(PropertyType.CoreEnergyLimit, 100f);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(targetProperty.CoreEnergy, Is.EqualTo(20f).Within(0.0001f));
            }
            finally
            {
                Core.Event = previousEventKit;
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>验证 CoreEnergyGainOperation 接受负数变化量、在零处截断并正常触发运行时属性脏通知。</summary>
        [Test]
        public void CoreEnergyGain_NegativeValueConsumesEnergyAndClampsAtZero()
        {
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.CoreEnergyConsume";
            definition.ConfigureForTests("Tests.CoreEnergyConsume", EffectTag.Buff, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new CoreEnergyGainOperation(EffectValueFormula.Constant(-100f)) }, null, null, null);
            int dirtyCount = 0;
            ListenHandle listenHandle = null;
            try
            {
                targetProperty.SetBaseValue(PropertyType.CoreEnergyLimit, 100f);
                targetProperty.OnGainCoreEnergy(60f);
                listenHandle = targetProperty.CoreEnergyProperty.Listen(() => dirtyCount++, false);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(targetProperty.CoreEnergy, Is.Zero);
                Assert.That(dirtyCount, Is.EqualTo(1));
            }
            finally
            {
                listenHandle?.Dispose();
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>验证正式满核心能量链路会同时获得 Boost 并通过 EmptyCoreEnergy 清空运行时能量。</summary>
        [Test]
        public void PersistentCoreEnergyFlow_FullEnergyAppliesBoostAndClearsRuntimeEnergy()
        {
            const string libraryPath = "Assets/BundleResources/Config/Effect/EffectLibrary.asset";
            const string boostPath = "Assets/BundleResources/Config/Effect/EffectDefinitions/Boost.asset";
            const string energyGainPath = "Assets/BundleResources/Config/Effect/EffectDefinitions/EnergyGain.asset";
            EffectLibrary persistentLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(libraryPath);
            EffectDefinition boostDefinition = AssetDatabase.LoadAssetAtPath<EffectDefinition>(boostPath);
            EffectDefinition energyGainDefinition = AssetDatabase.LoadAssetAtPath<EffectDefinition>(energyGainPath);
            Assert.That(persistentLibrary, Is.Not.Null);
            Assert.That(boostDefinition, Is.Not.Null);
            Assert.That(energyGainDefinition, Is.Not.Null);
            EffectSignal damageSignal = new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack, "Tests.CoreEnergy");
            float configuredEnergyGain = Mathf.Max(0f, ReadConfiguredOperationFormula<CoreEnergyGainOperation>(energyGainDefinition, damageSignal, "amount"));
            float configuredFullEnergyThreshold = ReadConfiguredConditionThreshold(persistentLibrary.CombatFlowTriggers, EffectSignalType.CoreEnergyGain, EffectConditionType.ValueGreaterThanOrEqual);
            Assert.That(configuredEnergyGain, Is.GreaterThan(0f), "正式 EnergyGain 必须配置正数核心能量增量，否则无法验证满能量链路。");
            Assert.That(configuredFullEnergyThreshold, Is.GreaterThan(0f), "正式满核心能量触发规则必须配置正数阈值。");
            int requiredDamageSignals = Mathf.CeilToInt(configuredFullEnergyThreshold / configuredEnergyGain);
            registrations = runtime.RegisterTriggerSet(sourceEntity, persistentLibrary.CombatFlowTriggers);
            sourceProperty.SetBaseValue(PropertyType.CoreEnergyLimit, configuredFullEnergyThreshold);
            for (int index = 0; index < requiredDamageSignals; index++) runtime.Publish(new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, damageSignal.RequestedValue, damageSignal.Value, damageSignal.Tags, $"{damageSignal.AbilityId}.{index}"));
            Assert.That(runtime.GetStackCount(sourceEntity, boostDefinition.EffectId), Is.EqualTo(1));
            Assert.That(sourceProperty.CoreEnergy, Is.Zero);
        }

        /// <summary>验证大招能量操作每次增加配置值、按上限截断，并且普通实体不依赖全局 HUD 事件。</summary>
        [Test]
        public void UltEnergyGain_UsesConfiguredAmountAndClampsToLimit()
        {
            IEventKit previousEventKit = Core.Event;
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.UltEnergyGain";
            definition.ConfigureForTests("Tests.UltEnergyGain", EffectTag.Buff | EffectTag.UltEnergyGain, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new UltEnergyGainOperation(EffectValueFormula.Constant(5f)) }, null, null, null);
            try
            {
                Core.Event = null;
                targetProperty.SetBaseValue(PropertyType.UltEnergyLimit, 10f);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(targetProperty.UltEnergy, Is.EqualTo(5f).Within(0.0001f));
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(targetProperty.UltEnergy, Is.EqualTo(10f).Within(0.0001f));
                Assert.That(targetProperty.IsUltEnergyFull, Is.True);
            }
            finally
            {
                Core.Event = previousEventKit;
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>验证正式 CombatFlowTriggers 仅让造成正数实际伤害的普通攻击获得 UltEnergyGain 资产当前配置的能量，技能伤害不会误充能。</summary>
        [Test]
        public void PersistentCombatFlow_NormalAttackDamageGainsConfiguredUltEnergyOnly()
        {
            const string libraryPath = "Assets/BundleResources/Config/Effect/EffectLibrary.asset";
            const string ultEnergyGainPath = "Assets/BundleResources/Config/Effect/EffectDefinitions/UltEnergyGain.asset";
            EffectLibrary persistentLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(libraryPath);
            EffectDefinition ultEnergyGainDefinition = AssetDatabase.LoadAssetAtPath<EffectDefinition>(ultEnergyGainPath);
            Assert.That(persistentLibrary, Is.Not.Null);
            Assert.That(ultEnergyGainDefinition, Is.Not.Null);
            EffectSignal normalAttackDamage = new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack | EffectTag.NormalAttack, "Tests.NormalAttack", damageActionType: DamageActionType.NormalAttack);
            float configuredUltEnergyGain = Mathf.Max(0f, ReadConfiguredOperationFormula<UltEnergyGainOperation>(ultEnergyGainDefinition, normalAttackDamage, "amount"));
            Assert.That(configuredUltEnergyGain, Is.GreaterThan(0f), "正式 UltEnergyGain 必须配置正数大招能量增量，否则该触发链路没有可验证结果。");
            registrations = runtime.RegisterTriggerSet(sourceEntity, persistentLibrary.CombatFlowTriggers);
            sourceProperty.SetBaseValue(PropertyType.UltEnergyLimit, configuredUltEnergyGain + 1f);
            float expectedUltEnergyAfterNormalAttack = Mathf.Min(sourceProperty.UltEnergyLimit, sourceProperty.UltEnergy + configuredUltEnergyGain);
            runtime.Publish(normalAttackDamage);
            Assert.That(sourceProperty.UltEnergy, Is.EqualTo(expectedUltEnergyAfterNormalAttack).Within(0.0001f));
            EffectSignal skillDamage = new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack | EffectTag.Skill, "Tests.Skill", damageActionType: DamageActionType.Skill);
            runtime.Publish(skillDamage);
            Assert.That(sourceProperty.UltEnergy, Is.EqualTo(expectedUltEnergyAfterNormalAttack).Within(0.0001f));
        }

        /// <summary>
        /// 验证燃烧每秒产生一次 DOT 伤害，并在同一施法者再次添加时刷新而不是复制实例。
        /// </summary>
        [Test]
        public void Burning_TicksAndRefreshesByCaster()
        {
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            examples.Library.PublishFireAttackForTests(runtime, sourceEntity, targetEntity);
            Assert.That(runtime.GetStackCount(targetEntity, EffectExampleFactory.BurningId), Is.EqualTo(1));
            runtime.Tick(1.01f);
            Assert.That(targetProperty.Hp, Is.EqualTo(70f).Within(0.0001f));
            examples.Library.PublishFireAttackForTests(runtime, sourceEntity, targetEntity);
            Assert.That(runtime.GetStackCount(targetEntity, EffectExampleFactory.BurningId), Is.EqualTo(1));
            Assert.That(runtime.GetActiveEffects(targetEntity).Count, Is.EqualTo(1), "目标只应持有刷新后的 Burning，受击不再创建持续 Effect。");
        }

        /// <summary>
        /// 验证 RefreshDuration 只执行刷新分支并保留已有 Tick 进度，不会执行 OnStack 或推迟下一次周期结算。
        /// </summary>
        [Test]
        public void RefreshDuration_OnlyRefreshesDurationAndPreservesTickCadence()
        {
            CountingOperation stackCounter = new CountingOperation();
            CountingOperation refreshCounter = new CountingOperation();
            CountingOperation tickCounter = new CountingOperation();
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.RefreshDuration";
            definition.ConfigureForTests("Tests.RefreshDuration", EffectTag.Buff, EffectDurationType.Duration, 2f, 1f, EffectStackPolicy.RefreshDuration, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, null, new EffectOperation[] { stackCounter }, new EffectOperation[] { tickCounter }, null, refreshOperations: new EffectOperation[] { refreshCounter });
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                runtime.Tick(0.75f);
                EffectInstance instance = runtime.GetActiveEffects(targetEntity)[0];
                Assert.That(instance.ElapsedTime, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(instance.TickElapsedTime, Is.EqualTo(0.75f).Within(0.0001f));
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(instance.ElapsedTime, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(instance.TickElapsedTime, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(instance.Stacks, Is.EqualTo(1));
                Assert.That(stackCounter.ExecutionCount, Is.Zero);
                Assert.That(refreshCounter.ExecutionCount, Is.EqualTo(1));
                runtime.Tick(0.26f);
                Assert.That(tickCounter.ExecutionCount, Is.EqualTo(1), "刷新持续时间不得重置 TickElapsedTime，否则会错误推迟下一次 Tick。");
            }
            finally
            {
                runtime.RemoveAll(targetEntity);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>验证持续 Effect 的应用、计时、叠层刷新和移除都会为正确 Owner 发布活动效果脏通知。</summary>
        [Test]
        public void PersistentEffect_EmitsOwnerDirtyAcrossCompleteLifetime()
        {
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.ObservableBuff";
            definition.ConfigureForTests("Tests.ObservableBuff", EffectTag.Buff, EffectDurationType.Duration, 3f, 0f, EffectStackPolicy.AddStackAndRefreshDuration, EffectStackKeyPolicy.Definition, 2, EffectExecutionPhase.Apply, 0, null, null, null, null);
            int ownerDirtyCount = 0;
            runtime.ActiveEffectsChanged += (owner, _) =>
            {
                if (ReferenceEquals(owner, targetEntity)) ownerDirtyCount++;
            };
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(ownerDirtyCount, Is.EqualTo(1), "首次应用持续 Buff 必须通知所属实体。");
                runtime.Tick(0.5f);
                Assert.That(ownerDirtyCount, Is.EqualTo(2), "有限持续时间推进必须刷新 HUD 遮罩。");
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(ownerDirtyCount, Is.EqualTo(3), "同一次叠层与刷新事务只应产生一次列表脏通知。");
                EffectInstance instance = runtime.GetActiveEffects(targetEntity)[0];
                runtime.RemoveEffect(instance);
                Assert.That(ownerDirtyCount, Is.EqualTo(4), "移除持续 Buff 必须立即刷新列表成员。");
            }
            finally
            {
                runtime.RemoveAll(targetEntity);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证 AddStackAndRefreshDuration 未满层时同时执行两个分支，满层后只刷新并且不再发送 EffectStacked。
        /// </summary>
        [Test]
        public void AddStackAndRefreshDuration_EmitsStackOnlyWhenCountChangesButRefreshesAtCap()
        {
            CountingOperation stackCounter = new CountingOperation();
            CountingOperation refreshCounter = new CountingOperation();
            CountingOperation stackedSignalCounter = new CountingOperation();
            CountingOperation refreshedSignalCounter = new CountingOperation();
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectDefinition stackedSignalEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectDefinition refreshedSignalEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            EffectTriggerSet triggerSet = ScriptableObject.CreateInstance<EffectTriggerSet>();
            definition.name = "Tests.AddStackAndRefresh";
            stackedSignalEffect.name = "Tests.CountEffectStacked";
            refreshedSignalEffect.name = "Tests.CountEffectRefreshed";
            triggerSet.name = "Tests.ReapplySignals";
            definition.ConfigureForTests("Tests.AddStackAndRefresh", EffectTag.Buff, EffectDurationType.Duration, 3f, 0f, EffectStackPolicy.AddStackAndRefreshDuration, EffectStackKeyPolicy.Definition, 2, EffectExecutionPhase.Apply, 0, null, new EffectOperation[] { stackCounter }, null, null, refreshOperations: new EffectOperation[] { refreshCounter });
            stackedSignalEffect.ConfigureForTests("Tests.CountEffectStacked", EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { stackedSignalCounter }, null, null, null);
            refreshedSignalEffect.ConfigureForTests("Tests.CountEffectRefreshed", EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { refreshedSignalCounter }, null, null, null);
            EffectTriggerDefinition stackedTrigger = new EffectTriggerDefinition();
            EffectTriggerDefinition refreshedTrigger = new EffectTriggerDefinition();
            stackedTrigger.ConfigureForTests("Tests.OnEffectStacked", EffectSignalType.EffectStacked, EffectListenScope.Target, EffectTargetSelector.Target, 1f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), new[] { stackedSignalEffect });
            refreshedTrigger.ConfigureForTests("Tests.OnEffectRefreshed", EffectSignalType.EffectRefreshed, EffectListenScope.Target, EffectTargetSelector.Target, 1f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), new[] { refreshedSignalEffect });
            triggerSet.ConfigureForTests(new[] { stackedTrigger, refreshedTrigger });
            IDisposable signalRegistrations = runtime.RegisterTriggerSet(targetEntity, triggerSet);
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                runtime.Tick(0.5f);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(runtime.GetStackCount(targetEntity, definition.EffectId), Is.EqualTo(2));
                Assert.That(stackCounter.ExecutionCount, Is.EqualTo(1));
                Assert.That(refreshCounter.ExecutionCount, Is.EqualTo(1));
                Assert.That(stackedSignalCounter.ExecutionCount, Is.EqualTo(1));
                Assert.That(refreshedSignalCounter.ExecutionCount, Is.EqualTo(1));
                runtime.Tick(0.5f);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                EffectInstance instance = runtime.GetActiveEffects(targetEntity)[0];
                Assert.That(instance.ElapsedTime, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(instance.Stacks, Is.EqualTo(2));
                Assert.That(stackCounter.ExecutionCount, Is.EqualTo(1), "满层后的重复施加不得再次执行 OnStack Operations。");
                Assert.That(refreshCounter.ExecutionCount, Is.EqualTo(2));
                Assert.That(stackedSignalCounter.ExecutionCount, Is.EqualTo(1), "满层后的重复施加不得发送 EffectStacked。");
                Assert.That(refreshedSignalCounter.ExecutionCount, Is.EqualTo(2));
            }
            finally
            {
                signalRegistrations.Dispose();
                runtime.RemoveAll(targetEntity);
                UnityEngine.Object.DestroyImmediate(triggerSet);
                UnityEngine.Object.DestroyImmediate(refreshedSignalEffect);
                UnityEngine.Object.DestroyImmediate(stackedSignalEffect);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证 Permanent 不存在可刷新的有限时长，因此 RefreshDuration 不执行刷新生命周期。
        /// </summary>
        [Test]
        public void PermanentRefreshDuration_DoesNotExecuteRefreshBranch()
        {
            CountingOperation refreshCounter = new CountingOperation();
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.PermanentRefresh";
            definition.ConfigureForTests("Tests.PermanentRefresh", EffectTag.Buff, EffectDurationType.Permanent, 0f, 0f, EffectStackPolicy.RefreshDuration, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, null, null, null, null, refreshOperations: new EffectOperation[] { refreshCounter });
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, sourceEntity);
                Assert.That(runtime.GetActiveEffects(targetEntity).Count, Is.EqualTo(1));
                Assert.That(refreshCounter.ExecutionCount, Is.Zero);
            }
            finally
            {
                runtime.RemoveAll(targetEntity);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证每个属性都按照 BaseValue × Boost + Offset 更新命名结果，并且同值 modifier 可按身份独立移除。
        /// </summary>
        [Test]
        public void PropertyModifiers_UpdateNamedValuesWithBoostAndOffsetAndRemoveByIdentity()
        {
            Assert.That(sourceProperty.Atk, Is.EqualTo(20f).Within(0.0001f));
            PropertyModifier firstAtkBoost = sourceProperty.AddModifier(PropertyType.Atk, PropertyModifierMode.Boost, 0.1f);
            PropertyModifier secondAtkBoost = sourceProperty.AddModifier(PropertyType.Atk, PropertyModifierMode.Boost, 0.1f);
            PropertyModifier atkOffset = sourceProperty.AddModifier(PropertyType.Atk, PropertyModifierMode.Offset, 5f);
            Assert.That(sourceProperty.Atk, Is.EqualTo(29f).Within(0.0001f));
            Assert.That(sourceProperty.RemoveModifier(firstAtkBoost), Is.True);
            Assert.That(sourceProperty.Atk, Is.EqualTo(27f).Within(0.0001f));
            Assert.That(sourceProperty.RemoveModifier(firstAtkBoost), Is.False);
            Assert.That(sourceProperty.Atk, Is.EqualTo(27f).Within(0.0001f));
            Assert.That(sourceProperty.RemoveModifier(atkOffset), Is.True);
            Assert.That(sourceProperty.Atk, Is.EqualTo(22f).Within(0.0001f));
            Assert.That(sourceProperty.RemoveModifier(secondAtkBoost), Is.True);
            Assert.That(sourceProperty.Atk, Is.EqualTo(20f).Within(0.0001f));
            PropertyModifier defBoost = sourceProperty.AddModifier(PropertyType.Def, PropertyModifierMode.Boost, 0.5f);
            PropertyModifier defOffset = sourceProperty.AddModifier(PropertyType.Def, PropertyModifierMode.Offset, 2f);
            Assert.That(sourceProperty.Def, Is.EqualTo(17f).Within(0.0001f));
            Assert.That(sourceProperty.RemoveModifier(defBoost), Is.True);
            Assert.That(sourceProperty.RemoveModifier(defOffset), Is.True);
            PropertyModifier moveSpeedBoost = sourceProperty.AddModifier(PropertyType.MoveSpeed, PropertyModifierMode.Boost, 0.5f);
            PropertyModifier moveSpeedOffset = sourceProperty.AddModifier(PropertyType.MoveSpeed, PropertyModifierMode.Offset, 1f);
            Assert.That(sourceProperty.MoveSpeed, Is.EqualTo(5.5f).Within(0.0001f));
            Assert.That(sourceProperty.RemoveModifier(moveSpeedBoost), Is.True);
            Assert.That(sourceProperty.RemoveModifier(moveSpeedOffset), Is.True);
            PropertyModifier toughnessBoost = targetProperty.AddModifier(PropertyType.Toughness, PropertyModifierMode.Boost, 0.5f);
            PropertyModifier toughnessOffset = targetProperty.AddModifier(PropertyType.Toughness, PropertyModifierMode.Offset, 1f);
            Assert.That(targetProperty.Toughness, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(targetProperty.RemoveModifier(toughnessBoost), Is.True);
            Assert.That(targetProperty.RemoveModifier(toughnessOffset), Is.True);
            Assert.That(targetProperty.Toughness, Is.EqualTo(1f).Within(0.0001f));
        }

        /// <summary>
        /// 验证伤害加成在攻击力和暴击结算之后作为独立乘区只乘算一次。
        /// </summary>
        [Test]
        public void DamageBonus_MultipliesCalculatedDamageAsIndependentZoneOnce()
        {
            Assert.That(sourceProperty.DamageBonus, Is.EqualTo(0f).Within(0.0001f));
            sourceProperty.SetBaseValue(PropertyType.CritRate, 0f);
            sourceProperty.SetBaseValue(PropertyType.CritDmg, 0f);
            sourceProperty.AddModifier(PropertyType.Atk, PropertyModifierMode.Boost, 0.5f);
            sourceProperty.AddModifier(PropertyType.DamageBoost, PropertyModifierMode.Offset, 0.5f);
            Assert.That(sourceProperty.Atk, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(sourceProperty.DamageBonus, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(sourceProperty.GetCalculatedDamage(), Is.EqualTo(45f).Within(0.0001f));
        }

        /// <summary>
        /// 验证受伤加成只在 OnTakeDamage 入口作为独立乘区结算一次，并返回乘算后的实际扣血量。
        /// </summary>
        [Test]
        public void DamageTakenBonus_MultipliesIncomingDamageAsIndependentZoneOnce()
        {
            Assert.That(targetProperty.DamageTakenBonus, Is.EqualTo(0f).Within(0.0001f));
            targetProperty.AddModifier(PropertyType.DamageTakenBoost, PropertyModifierMode.Offset, 0.5f);
            float actualDamage = targetProperty.OnTakeDamage(20f);
            Assert.That(targetProperty.DamageTakenBonus, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(actualDamage, Is.EqualTo(30f).Within(0.0001f));
            Assert.That(targetProperty.Hp, Is.EqualTo(70f).Within(0.0001f));
        }

        /// <summary>
        /// 验证相同状态的多个 Modifier 按对象身份独立移除，并验证 Root、Silence 与 Stun 的能力矩阵。
        /// </summary>
        [Test]
        public void ControlStateModifiers_CombineAndRemoveByIdentity()
        {
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int stateChangeCount = 0;
            targetEvents.AddListener<ControlStateChangedEvent>(_ => stateChangeCount++);
            ControlStateModifier firstRoot = targetProperty.AddControlStateModifier(ControlState.Root);
            ControlStateModifier secondRoot = targetProperty.AddControlStateModifier(ControlState.Root);
            ControlStateModifier silence = targetProperty.AddControlStateModifier(ControlState.Silence);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.Root | ControlState.Silence));
            Assert.That(targetProperty.CanAct, Is.True);
            Assert.That(targetProperty.CanMove, Is.False);
            Assert.That(targetProperty.CanUseActiveSkill, Is.False);
            Assert.That(targetProperty.RemoveControlStateModifier(firstRoot), Is.True);
            Assert.That(targetProperty.HasAnyControlState(ControlState.Root), Is.True);
            Assert.That(targetProperty.RemoveControlStateModifier(secondRoot), Is.True);
            Assert.That(targetProperty.CanMove, Is.True);
            ControlStateModifier stun = targetProperty.AddControlStateModifier(ControlState.Stun);
            Assert.That(targetProperty.CanAct, Is.False);
            Assert.That(targetProperty.CanMove, Is.False);
            Assert.That(targetProperty.CanUseActiveSkill, Is.False);
            Assert.That(targetProperty.RemoveControlStateModifier(stun), Is.True);
            Assert.That(targetProperty.RemoveControlStateModifier(silence), Is.True);
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
            Assert.That(targetProperty.RemoveControlStateModifier(silence), Is.False);
            Assert.That(stateChangeCount, Is.EqualTo(6));
        }

        /// <summary>
        /// 验证 Entity 依据 Logic 的能力需求统一启停行为，同时保证 Root、Silence 与 Attacked 只影响各自职责。
        /// </summary>
        [Test]
        public void Entity_ControlRequirementsGateOnlyMatchingLogic()
        {
            DefaultActTestLogic actLogic = new DefaultActTestLogic();
            MoveRequirementTestLogic moveLogic = new MoveRequirementTestLogic();
            ActiveSkillRequirementTestLogic skillLogic = new ActiveSkillRequirementTestLogic();
            targetEntity.AddLogic(actLogic);
            targetEntity.AddLogic(moveLogic);
            targetEntity.AddLogic(skillLogic);
            AssetKit lifecycleAssetKit = new AssetKit();
            GameplayKit lifecycleGameplayKit = new GameplayKit(lifecycleAssetKit);
            lifecycleGameplayKit.AddEntity(targetEntity);
            try
            {
                targetEntity.AfterNew();
                targetEntity.OnUpdate(0.1f);
                Assert.That(actLogic.UpdateCount, Is.EqualTo(1));
                Assert.That(moveLogic.UpdateCount, Is.EqualTo(1));
                Assert.That(skillLogic.UpdateCount, Is.EqualTo(1));
                ControlStateModifier root = targetProperty.AddControlStateModifier(ControlState.Root);
                targetEntity.OnUpdate(0.1f);
                Assert.That(actLogic.UpdateCount, Is.EqualTo(2));
                Assert.That(moveLogic.DisableCount, Is.EqualTo(1));
                Assert.That(moveLogic.UpdateCount, Is.EqualTo(1));
                Assert.That(skillLogic.UpdateCount, Is.EqualTo(2));
                ControlStateModifier silence = targetProperty.AddControlStateModifier(ControlState.Silence);
                targetEntity.OnUpdate(0.1f);
                Assert.That(actLogic.UpdateCount, Is.EqualTo(3));
                Assert.That(skillLogic.DisableCount, Is.EqualTo(1));
                Assert.That(skillLogic.UpdateCount, Is.EqualTo(2));
                targetProperty.RemoveControlStateModifier(root);
                targetProperty.RemoveControlStateModifier(silence);
                targetEntity.OnUpdate(0.1f);
                Assert.That(actLogic.UpdateCount, Is.EqualTo(4));
                Assert.That(moveLogic.UpdateCount, Is.EqualTo(2));
                Assert.That(skillLogic.UpdateCount, Is.EqualTo(3));
                ControlStateModifier attacked = targetProperty.AddControlStateModifier(ControlState.Attacked);
                targetEntity.OnUpdate(0.1f);
                Assert.That(actLogic.DisableCount, Is.EqualTo(1));
                Assert.That(moveLogic.DisableCount, Is.EqualTo(2));
                Assert.That(skillLogic.DisableCount, Is.EqualTo(2));
                Assert.That(actLogic.UpdateCount, Is.EqualTo(4));
                Assert.That(moveLogic.UpdateCount, Is.EqualTo(2));
                Assert.That(skillLogic.UpdateCount, Is.EqualTo(3));
                targetProperty.RemoveControlStateModifier(attacked);
                targetEntity.OnUpdate(0.1f);
                Assert.That(actLogic.UpdateCount, Is.EqualTo(5));
                Assert.That(moveLogic.UpdateCount, Is.EqualTo(3));
                Assert.That(skillLogic.UpdateCount, Is.EqualTo(4));
            }
            finally
            {
                lifecycleGameplayKit.Dispose();
                lifecycleAssetKit.Dispose();
            }
        }

        /// <summary>
        /// 验证攻击伤害叠加战意、DOT 不触发战意，并在持续时间结束时准确回滚全部属性增益。
        /// </summary>
        [Test]
        public void CombatFlow_StacksFromAttackButIgnoresDotAndRemovesModifiers()
        {
            registrations = examples.Library.RegisterAllForTests(runtime, sourceEntity);
            examples.Library.PublishFireAttackForTests(runtime, sourceEntity, targetEntity);
            Assert.That(targetProperty.Hp, Is.EqualTo(80f).Within(0.0001f));
            Assert.That(runtime.GetStackCount(sourceEntity, EffectExampleFactory.CombatFlowId), Is.EqualTo(1));
            Assert.That(sourceProperty.Atk, Is.EqualTo(22f).Within(0.0001f));
            Assert.That(sourceProperty.AtkSpeed, Is.EqualTo(1.05f).Within(0.0001f));
            runtime.Tick(1.01f);
            Assert.That(targetProperty.Hp, Is.EqualTo(70f).Within(0.0001f));
            Assert.That(runtime.GetStackCount(sourceEntity, EffectExampleFactory.CombatFlowId), Is.EqualTo(1));
            examples.Library.PublishFireAttackForTests(runtime, sourceEntity, targetEntity);
            Assert.That(targetProperty.Hp, Is.EqualTo(48f).Within(0.0001f));
            Assert.That(runtime.GetStackCount(sourceEntity, EffectExampleFactory.CombatFlowId), Is.EqualTo(2));
            Assert.That(sourceProperty.Atk, Is.EqualTo(24f).Within(0.0001f));
            Assert.That(sourceProperty.AtkSpeed, Is.EqualTo(1.1f).Within(0.0001f));
            runtime.Tick(3.01f);
            Assert.That(targetProperty.Hp, Is.EqualTo(18f).Within(0.0001f));
            Assert.That(runtime.GetStackCount(sourceEntity, EffectExampleFactory.CombatFlowId), Is.EqualTo(0));
            Assert.That(sourceProperty.Atk, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(sourceProperty.AtkSpeed, Is.EqualTo(1f).Within(0.0001f));
        }

        /// <summary>
        /// 验证编辑器生成的持久化资产能重新加载全部 ScriptableObject 引用和 SerializeReference 操作，并按照资产当前数值执行。
        /// </summary>
        [Test]
        public void GeneratedExampleAssets_LoadAndExecuteAfterSerialization()
        {
            const string libraryPath = "Assets/BundleResources/Config/Effect/EffectLibrary.asset";
            EffectLibrary persistentLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(libraryPath);
            Assert.That(persistentLibrary, Is.Not.Null);
            Assert.That(persistentLibrary.DirectDamage, Is.Not.Null);
            Assert.That(persistentLibrary.Burning, Is.Not.Null);
            Assert.That(persistentLibrary.CombatFlow, Is.Not.Null);
            Assert.That(persistentLibrary.Stun, Is.Not.Null);
            Assert.That(persistentLibrary.CombatFlow.BuffIcon, Is.Not.Null, "正式战意 Buff 必须配置 HUD 图标。");
            EffectDefinition boostDefinition = AssetDatabase.LoadAssetAtPath<EffectDefinition>("Assets/BundleResources/Config/Effect/EffectDefinitions/Boost.asset");
            Assert.That(boostDefinition, Is.Not.Null);
            Assert.That(boostDefinition.BuffIcon, Is.Not.Null, "正式 Boost Buff 必须配置 HUD 图标。");
            targetProperty.SetBaseValue(PropertyType.Toughness, 1f);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int staggeredCount = 0;
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            float sourceAttackBeforeEffect = sourceProperty.Atk;
            float sourceAttackSpeedBeforeEffect = sourceProperty.AtkSpeed;
            float targetHpBeforeEffect = targetProperty.Hp;
            EffectSignal fireAttackSignal = new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, sourceAttackBeforeEffect, sourceAttackBeforeEffect, EffectTag.Attack | EffectTag.NormalAttack, "Example.FireAttack", damageAttribute: DamageAttribute.Fire, damageActionType: DamageActionType.NormalAttack);
            float configuredDamage = ReadConfiguredDamage(persistentLibrary.DirectDamage, fireAttackSignal);
            float expectedActualDamage = Mathf.Min(targetHpBeforeEffect, Mathf.Max(0f, configuredDamage));
            EffectSignal damageAppliedSignal = fireAttackSignal.CreateChild(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, configuredDamage, expectedActualDamage, fireAttackSignal.Tags | persistentLibrary.DirectDamage.Tags);
            int expectedCombatFlowStacks = expectedActualDamage > 0f ? 1 : 0;
            float expectedAttack = expectedCombatFlowStacks == 0 ? sourceAttackBeforeEffect : CalculateConfiguredPropertyValue(persistentLibrary.CombatFlow, PropertyType.Atk, sourceAttackBeforeEffect, damageAppliedSignal);
            float expectedAttackSpeed = expectedCombatFlowStacks == 0 ? sourceAttackSpeedBeforeEffect : CalculateConfiguredPropertyValue(persistentLibrary.CombatFlow, PropertyType.AtkSpeed, sourceAttackSpeedBeforeEffect, damageAppliedSignal);
            registrations = persistentLibrary.RegisterAllForTests(runtime, sourceEntity);
            persistentLibrary.PublishFireAttackForTests(runtime, sourceEntity, targetEntity);
            Assert.That(targetProperty.Hp, Is.EqualTo(targetHpBeforeEffect - expectedActualDamage).Within(0.0001f));
            Assert.That(runtime.GetStackCount(targetEntity, persistentLibrary.Burning.EffectId), Is.EqualTo(1));
            Assert.That(runtime.GetStackCount(sourceEntity, persistentLibrary.CombatFlow.EffectId), Is.EqualTo(expectedCombatFlowStacks));
            Assert.That(sourceProperty.Atk, Is.EqualTo(expectedAttack).Within(0.0001f));
            Assert.That(sourceProperty.AtkSpeed, Is.EqualTo(expectedAttackSpeed).Within(0.0001f));
            Assert.That(staggeredCount, Is.EqualTo(1), "持久化直接伤害配置的打断能力必须在严格超过韧性时发布受击事件。");
            Assert.That(runtime.GetStackCount(targetEntity, persistentLibrary.Stun.EffectId), Is.Zero, "伤害受击链不得自动创建 Stun Effect。");
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
            runtime.ApplyEffect(persistentLibrary.Stun, sourceEntity, targetEntity, sourceEntity);
            Assert.That(runtime.GetStackCount(targetEntity, persistentLibrary.Stun.EffectId), Is.EqualTo(1), "Stun 仍可作为独立控制效果被显式应用。");
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.Stun));
            Assert.That(persistentLibrary.Stun.DurationType, Is.EqualTo(EffectDurationType.Duration));
            runtime.Tick(persistentLibrary.Stun.Duration + 0.01f);
            Assert.That(runtime.GetStackCount(targetEntity, persistentLibrary.Stun.EffectId), Is.EqualTo(0));
            Assert.That(targetProperty.ActiveControlStates, Is.EqualTo(ControlState.None));
        }

        /// <summary>
        /// 验证新增 PropertyModifierOperation 默认展开 valuePerStack、Multiplier 为零，并能通过编辑器剪贴板完整复制粘贴配置。
        /// </summary>
        [Test]
        public void PropertyModifierEditor_DefaultsAndClipboardRoundTripAreCorrect()
        {
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            Type editorType = FindType("Xuan.Prometheus.Effects.Editor.EffectDefinitionEditor");
            UnityEditor.Editor definitionEditor = null;
            string previousClipboard = EditorGUIUtility.systemCopyBuffer;
            try
            {
                Assert.That(editorType, Is.Not.Null);
                definitionEditor = UnityEditor.Editor.CreateEditor(definition, editorType);
                MethodInfo addOperation = editorType.GetMethod("AddOperation", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo copyConfiguration = editorType.GetMethod("CopyPropertyModifierConfiguration", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo pasteConfiguration = editorType.GetMethod("PastePropertyModifierConfiguration", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(addOperation, Is.Not.Null);
                Assert.That(copyConfiguration, Is.Not.Null);
                Assert.That(pasteConfiguration, Is.Not.Null);
                addOperation.Invoke(definitionEditor, new object[] { "onApplyOperations", new PropertyModifierOperation() });
                addOperation.Invoke(definitionEditor, new object[] { "onApplyOperations", new PropertyModifierOperation("Copied.Property", PropertyType.Def, PropertyModifierMode.Offset, EffectValueFormula.CasterAttack(2f, 3f)) });
                definitionEditor.serializedObject.Update();
                SerializedProperty list = definitionEditor.serializedObject.FindProperty("onApplyOperations");
                SerializedProperty defaultElement = list.GetArrayElementAtIndex(0);
                SerializedProperty defaultValuePerStack = defaultElement.FindPropertyRelative("valuePerStack");
                Assert.That(defaultElement.managedReferenceFullTypename, Does.Contain(nameof(PropertyModifierOperation)));
                Assert.That(defaultElement.FindPropertyRelative("keyPolicy").enumValueIndex, Is.EqualTo((int)PropertyModifierKeyPolicy.Automatic));
                Assert.That(defaultElement.FindPropertyRelative("customModifierKey").stringValue, Is.Empty);
                Assert.That(PropertyModifierOperation.BuildAutomaticKey(PropertyType.Atk, PropertyModifierMode.Boost), Is.EqualTo("PropertyModifier:Atk:Boost"));
                Assert.That(PropertyModifierOperation.BuildAutomaticKey(PropertyType.Atk, PropertyModifierMode.Boost), Is.Not.EqualTo(PropertyModifierOperation.BuildAutomaticKey(PropertyType.Atk, PropertyModifierMode.Offset)));
                Assert.That(PropertyModifierOperation.BuildAutomaticKey(PropertyType.Atk, PropertyModifierMode.Boost), Is.Not.EqualTo(PropertyModifierOperation.BuildAutomaticKey(PropertyType.Def, PropertyModifierMode.Boost)));
                Assert.That(defaultValuePerStack.isExpanded, Is.True);
                Assert.That(defaultValuePerStack.FindPropertyRelative("multiplier").floatValue, Is.EqualTo(0f));
                Assert.That(defaultValuePerStack.FindPropertyRelative("offset"), Is.Not.Null);
                Assert.That(defaultValuePerStack.FindPropertyRelative("additive"), Is.Null);
                copyConfiguration.Invoke(definitionEditor, new object[] { "onApplyOperations", 1 });
                pasteConfiguration.Invoke(definitionEditor, new object[] { "onApplyOperations", 0 });
                definitionEditor.serializedObject.Update();
                list = definitionEditor.serializedObject.FindProperty("onApplyOperations");
                SerializedProperty pastedElement = list.GetArrayElementAtIndex(0);
                SerializedProperty pastedValuePerStack = pastedElement.FindPropertyRelative("valuePerStack");
                Assert.That(pastedElement.FindPropertyRelative("keyPolicy").enumValueIndex, Is.EqualTo((int)PropertyModifierKeyPolicy.Custom));
                Assert.That(pastedElement.FindPropertyRelative("customModifierKey").stringValue, Is.EqualTo("Copied.Property"));
                Assert.That(pastedElement.FindPropertyRelative("propertyType").enumValueIndex, Is.EqualTo((int)PropertyType.Def));
                Assert.That(pastedElement.FindPropertyRelative("modifierMode").enumValueIndex, Is.EqualTo((int)PropertyModifierMode.Offset));
                Assert.That(pastedValuePerStack.FindPropertyRelative("baseValueSource").intValue, Is.EqualTo((int)EffectValueSource.Property));
                Assert.That(pastedValuePerStack.FindPropertyRelative("propertyEntity").enumValueIndex, Is.EqualTo((int)EffectValueEntity.Caster));
                Assert.That(pastedValuePerStack.FindPropertyRelative("propertyValue").enumValueIndex, Is.EqualTo((int)EffectPropertyValue.Atk));
                Assert.That(pastedValuePerStack.FindPropertyRelative("multiplier").floatValue, Is.EqualTo(2f));
                Assert.That(pastedValuePerStack.FindPropertyRelative("offset").floatValue, Is.EqualTo(3f));
                Assert.That(pastedValuePerStack.isExpanded, Is.True);
            }
            finally
            {
                EditorGUIUtility.systemCopyBuffer = previousClipboard;
                if (definitionEditor != null) UnityEngine.Object.DestroyImmediate(definitionEditor);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证 Caster 始终表示直接释放者、Source 始终表示因果链源头，并锁定旧资产所依赖的枚举序号。
        /// </summary>
        [Test]
        public void CasterAndSource_UseDirectActorAndOriginSemanticsWithoutChangingSerializedEnumValues()
        {
            RelationCaptureOperation capture = new RelationCaptureOperation();
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.RelationCapture";
            definition.ConfigureForTests(string.Empty, EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { capture }, null, null, null);
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, targetEntity);
                Assert.That(capture.Caster, Is.SameAs(sourceEntity));
                Assert.That(capture.Target, Is.SameAs(targetEntity));
                Assert.That(capture.Source, Is.SameAs(targetEntity));
                Assert.That(definition.EffectId, Is.EqualTo(definition.name));
                Assert.That((int)EffectListenScope.Caster, Is.EqualTo(0));
                Assert.That((int)EffectListenScope.Source, Is.EqualTo(2));
                Assert.That((int)EffectTargetSelector.Caster, Is.EqualTo(0));
                Assert.That((int)EffectTargetSelector.Source, Is.EqualTo(2));
                Assert.That((int)EffectStackKeyPolicy.DefinitionAndSource, Is.EqualTo(1));
                Assert.That((int)EffectStackKeyPolicy.DefinitionAndCaster, Is.EqualTo(2));
                Assert.That((int)EffectValueSource.One, Is.EqualTo(0));
                Assert.That((int)EffectValueSource.Property, Is.EqualTo(8));
                Assert.That((int)EffectValueEntity.Caster, Is.EqualTo(0));
                Assert.That((int)EffectValueEntity.Source, Is.EqualTo(2));
                Assert.That((int)EffectConditionType.CasterExists, Is.EqualTo(1));
                Assert.That((int)EffectConditionType.SourceExists, Is.EqualTo(3));
                Assert.That((int)EffectConditionType.DamageAttributeEquals, Is.EqualTo(9));
                Assert.That((int)EffectSignalType.EffectStacked, Is.EqualTo(6));
                Assert.That((int)EffectSignalType.EffectRefreshed, Is.EqualTo(10));
                Assert.That((int)DamageAttribute.Physical, Is.EqualTo(0));
                Assert.That((int)DamageAttribute.Dark, Is.EqualTo(7));
                Assert.That((int)EffectTag.SpecialAttack, Is.EqualTo(1 << 14));
                Assert.That((int)EffectTag.Ultimate, Is.EqualTo(1 << 15));
                EffectTriggerDefinition automaticTrigger = new EffectTriggerDefinition();
                automaticTrigger.ConfigureForTests(string.Empty, EffectSignalType.DamageApplied, EffectListenScope.Caster, EffectTargetSelector.Target, 0.25f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), Array.Empty<EffectDefinition>());
                Assert.That(automaticTrigger.TriggerId, Is.EqualTo(nameof(EffectSignalType.DamageApplied)));
                Assert.That(automaticTrigger.Probability, Is.EqualTo(0.25f).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证所有战斗属性 Gain 在信号和标签枚举中成对存在，防止新增属性后只补一侧导致 Trigger 无法完整路由。
        /// </summary>
        [Test]
        public void PropertyGainSignalsAndTags_CoverEveryCombatPropertyValue()
        {
            string[] gainNames = { nameof(EffectSignalType.AtkGain), nameof(EffectSignalType.DefGain), nameof(EffectSignalType.AtkSpeedGain), nameof(EffectSignalType.CritRateGain), nameof(EffectSignalType.CritDmgGain), nameof(EffectSignalType.HpGain), nameof(EffectSignalType.MaxHpGain), nameof(EffectSignalType.CoreEnergyGain), nameof(EffectSignalType.CoreEnergyLimitGain), nameof(EffectSignalType.UltEnergyGain), nameof(EffectSignalType.UltEnergyLimitGain), nameof(EffectSignalType.ToughnessGain), nameof(EffectSignalType.DamageBoostGain), nameof(EffectSignalType.DamageTakenBoostGain) };
            foreach (string gainName in gainNames)
            {
                Assert.That(Enum.IsDefined(typeof(EffectSignalType), gainName), Is.True, $"EffectSignalType is missing '{gainName}'.");
                Assert.That(Enum.IsDefined(typeof(EffectTag), gainName), Is.True, $"EffectTag is missing '{gainName}'.");
            }
            Assert.That((int)EffectSignalType.CoreEnergyGain, Is.EqualTo(9), "Existing signal serialization values must remain stable.");
            Assert.That((int)EffectTag.CoreEnergyGain, Is.EqualTo(1 << 12), "Existing tag serialization bits must remain stable.");
            Assert.That((int)EffectTag.UltEnergyGain, Is.EqualTo(1 << 13), "Existing tag serialization bits must remain stable.");
            Assert.That((int)EffectTag.DamageTakenBoostGain, Is.EqualTo(1 << 27), "New Gain tags must use unique appended bits.");
        }

        /// <summary>
        /// 验证实体来源和运行时属性可以正交组合，并覆盖 PropertyComponent 的全部最终值、当前资源和运行时上限。
        /// </summary>
        [Test]
        public void EffectValueFormula_PropertySourceCombinesEveryEntityWithEveryRuntimeValue()
        {
            GameObject originObject = new GameObject("EffectTest.Origin");
            PropertyConfig originConfig = ScriptableObject.CreateInstance<PropertyConfig>();
            originConfig.atk = 301f;
            PropertyComponent originProperty = originObject.AddComponent<PropertyComponent>();
            originProperty.InitializeForTests(originConfig);
            TestEntity originEntity = new TestEntity(originObject, originProperty);
            targetProperty.SetBaseValue(PropertyType.Atk, 201f);
            sourceProperty.SetBaseValue(PropertyType.Atk, 101f);
            sourceProperty.SetBaseValue(PropertyType.Def, 102f);
            sourceProperty.SetBaseValue(PropertyType.MoveSpeed, 103f);
            sourceProperty.SetBaseValue(PropertyType.AtkSpeed, 104f);
            sourceProperty.SetBaseValue(PropertyType.CritRate, 105f);
            sourceProperty.SetBaseValue(PropertyType.CritDmg, 106f);
            sourceProperty.SetBaseValue(PropertyType.MaxHp, 107f);
            sourceProperty.SetBaseValue(PropertyType.AirMoveSpeed, 108f);
            sourceProperty.SetBaseValue(PropertyType.JumpSpeed, 109f);
            sourceProperty.SetBaseValue(PropertyType.Gravity, 110f);
            sourceProperty.SetBaseValue(PropertyType.CoreEnergyLimit, 111f);
            sourceProperty.SetBaseValue(PropertyType.UltEnergyLimit, 112f);
            sourceProperty.SetBaseValue(PropertyType.Toughness, 113f);
            sourceProperty.SetBaseValue(PropertyType.DamageBoost, 114f);
            sourceProperty.SetBaseValue(PropertyType.DamageTakenBoost, 115f);
            sourceProperty.OnGainCoreEnergy(12f);
            sourceProperty.OnGainUltEnergy(13f);
            EffectPropertyValue[] propertyValues = (EffectPropertyValue[])Enum.GetValues(typeof(EffectPropertyValue));
            float[] expectedPropertyValues = { 101f, 102f, 103f, 104f, 105f, 106f, 100f, 107f, 108f, 109f, 110f, 12f, 111f, 13f, 112f, 113f, 114f, 115f };
            EffectValueFormula[] formulas = new EffectValueFormula[propertyValues.Length + 3];
            formulas[0] = EffectValueFormula.Property(EffectValueEntity.Caster, EffectPropertyValue.Atk);
            formulas[1] = EffectValueFormula.Property(EffectValueEntity.Target, EffectPropertyValue.Atk);
            formulas[2] = EffectValueFormula.Property(EffectValueEntity.Source, EffectPropertyValue.Atk);
            for (int index = 0; index < propertyValues.Length; index++) formulas[index + 3] = EffectValueFormula.Property(EffectValueEntity.Caster, propertyValues[index]);
            FormulaCaptureOperation capture = new FormulaCaptureOperation(formulas);
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.PropertyFormulaMatrix";
            definition.ConfigureForTests(string.Empty, EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { capture }, null, null, null);
            try
            {
                runtime.ApplyEffect(definition, sourceEntity, targetEntity, originEntity);
                Assert.That(capture.Values[0], Is.EqualTo(101f).Within(0.0001f));
                Assert.That(capture.Values[1], Is.EqualTo(201f).Within(0.0001f));
                Assert.That(capture.Values[2], Is.EqualTo(301f).Within(0.0001f));
                Assert.That(capture.Values.Length, Is.EqualTo(expectedPropertyValues.Length + 3));
                for (int index = 0; index < expectedPropertyValues.Length; index++) Assert.That(capture.Values[index + 3], Is.EqualTo(expectedPropertyValues[index]).Within(0.0001f), $"Unexpected runtime value for {propertyValues[index]}.");
                Assert.That(sourceConfig.hp, Is.EqualTo(100f).Within(0.0001f), "The config remains an initialization seed while MaxHp is changed at runtime.");
                Assert.That(sourceConfig.coreEnergyLimit, Is.EqualTo(0f).Within(0.0001f), "The runtime CoreEnergyLimit must not be read back from config.");
                Assert.That(sourceConfig.ultEnergyLimit, Is.EqualTo(0f).Within(0.0001f), "The runtime UltEnergyLimit must not be read back from config.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(originConfig);
                UnityEngine.Object.DestroyImmediate(originObject);
            }
        }

        /// <summary>
        /// 验证 Inspector 只显示当前持续时间与堆叠策略确实可能执行的 Stack 和 Refresh 操作列表。
        /// </summary>
        [Test]
        public void EffectDefinitionEditor_ShowsOnlyReachableReapplyOperationBranches()
        {
            Type editorType = FindType("Xuan.Prometheus.Effects.Editor.EffectDefinitionEditor");
            Assert.That(editorType, Is.Not.Null);
            MethodInfo executesOnStackOperations = editorType.GetMethod("ExecutesOnStackOperations", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo executesOnRefreshOperations = editorType.GetMethod("ExecutesOnRefreshOperations", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(executesOnStackOperations, Is.Not.Null);
            Assert.That(executesOnRefreshOperations, Is.Not.Null);
            Assert.That(executesOnStackOperations.Invoke(null, new object[] { EffectStackPolicy.AddStack, 1 }), Is.False);
            Assert.That(executesOnStackOperations.Invoke(null, new object[] { EffectStackPolicy.AddStack, 2 }), Is.True);
            Assert.That(executesOnStackOperations.Invoke(null, new object[] { EffectStackPolicy.RefreshDuration, 99 }), Is.False);
            Assert.That(executesOnStackOperations.Invoke(null, new object[] { EffectStackPolicy.AddStackAndRefreshDuration, 1 }), Is.False);
            Assert.That(executesOnStackOperations.Invoke(null, new object[] { EffectStackPolicy.AddStackAndRefreshDuration, 2 }), Is.True);
            Assert.That(executesOnStackOperations.Invoke(null, new object[] { EffectStackPolicy.Reject, 99 }), Is.False);
            Assert.That(executesOnRefreshOperations.Invoke(null, new object[] { EffectDurationType.Duration, EffectStackPolicy.RefreshDuration }), Is.True);
            Assert.That(executesOnRefreshOperations.Invoke(null, new object[] { EffectDurationType.Duration, EffectStackPolicy.AddStackAndRefreshDuration }), Is.True);
            Assert.That(executesOnRefreshOperations.Invoke(null, new object[] { EffectDurationType.Duration, EffectStackPolicy.AddStack }), Is.False);
            Assert.That(executesOnRefreshOperations.Invoke(null, new object[] { EffectDurationType.Permanent, EffectStackPolicy.RefreshDuration }), Is.False);
        }

        /// <summary>
        /// 验证公式、条件和 Trigger Drawer 使用新的紧凑行高，防止后续修改重新引入逐字段纵向排列。
        /// </summary>
        [Test]
        public void EffectInspectorDrawers_UseCompactRowHeights()
        {
            EffectTriggerDefinition trigger = new EffectTriggerDefinition();
            trigger.ConfigureForTests("Tests.CompactLayout", EffectSignalType.DamageApplied, EffectListenScope.Caster, EffectTargetSelector.Target, 0.5f, 1f, true, 10, new[] { EffectConditionDefinition.HasAnyTags(EffectTag.Attack) }, Array.Empty<EffectDefinition>());
            EffectDefinition definition = ScriptableObject.CreateInstance<EffectDefinition>();
            definition.name = "Tests.CompactInspector";
            definition.ConfigureForTests("Tests.CompactInspector", EffectTag.Buff, EffectDurationType.Duration, 1f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.Apply, 0, new EffectOperation[] { new PropertyModifierOperation(PropertyType.Atk, EffectValueFormula.Constant(0.1f)) }, null, null, null, triggers: new[] { trigger });
            try
            {
                SerializedObject serializedDefinition = new SerializedObject(definition);
                serializedDefinition.Update();
                SerializedProperty formula = serializedDefinition.FindProperty("onApplyOperations").GetArrayElementAtIndex(0).FindPropertyRelative("valuePerStack");
                SerializedProperty serializedTrigger = serializedDefinition.FindProperty("grantedTriggers").GetArrayElementAtIndex(0);
                SerializedProperty condition = serializedTrigger.FindPropertyRelative("conditions").GetArrayElementAtIndex(0);
                PropertyDrawer formulaDrawer = CreatePropertyDrawer("Xuan.Prometheus.Effects.Editor.EffectValueFormulaDrawer");
                PropertyDrawer conditionDrawer = CreatePropertyDrawer("Xuan.Prometheus.Effects.Editor.EffectConditionDefinitionDrawer");
                PropertyDrawer triggerDrawer = CreatePropertyDrawer("Xuan.Prometheus.Effects.Editor.EffectTriggerDefinitionDrawer");
                float lineHeight = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;
                formula.isExpanded = true;
                Assert.That(formulaDrawer.GetPropertyHeight(formula, new GUIContent("Value Per Stack")), Is.EqualTo(lineHeight * 2f + spacing).Within(0.0001f));
                condition.isExpanded = false;
                Assert.That(conditionDrawer.GetPropertyHeight(condition, new GUIContent("Element 0")), Is.EqualTo(lineHeight).Within(0.0001f));
                condition.isExpanded = true;
                Assert.That(conditionDrawer.GetPropertyHeight(condition, new GUIContent("Element 0")), Is.EqualTo(lineHeight).Within(0.0001f));
                serializedTrigger.isExpanded = true;
                float expectedTriggerHeight = lineHeight + (lineHeight + spacing) * 7f + spacing + EditorGUI.GetPropertyHeight(serializedTrigger.FindPropertyRelative("conditions"), true) + spacing + EditorGUI.GetPropertyHeight(serializedTrigger.FindPropertyRelative("effects"), true);
                Assert.That(triggerDrawer.GetPropertyHeight(serializedTrigger, new GUIContent("Element 0")), Is.EqualTo(expectedTriggerHeight).Within(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        /// <summary>
        /// 验证 EffectSystem 已经成为由 GameplayKit 托管的普通单局 System，不再依赖 MonoBehaviour 单例生命周期。
        /// </summary>
        [Test]
        public void EffectSystem_IsPlainGameplaySystemInsteadOfMonoBehaviour()
        {
            Assert.That(typeof(XSystem).IsAssignableFrom(typeof(EffectSystem)), Is.True);
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(EffectSystem)), Is.False);
        }

        /// <summary>
        /// 验证正式运行时定义不再公开测试写入入口，测试配置只能由 Editor 测试程序集中的扩展方法完成。
        /// </summary>
        [Test]
        public void RuntimeTypes_DoNotExposeTestOnlyHelpersOrState()
        {
            Type[] runtimeDefinitionTypes = { typeof(EffectDefinition), typeof(EffectTriggerDefinition), typeof(EffectTriggerSet) };
            foreach (Type runtimeDefinitionType in runtimeDefinitionTypes)
            {
                MethodInfo configureMethod = runtimeDefinitionType.GetMethod("Configure", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                MethodInfo testConfigureMethod = runtimeDefinitionType.GetMethod("ConfigureForTests", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                Assert.That(configureMethod, Is.Null, $"Runtime type '{runtimeDefinitionType.FullName}' must not expose the test-only Configure method.");
                Assert.That(testConfigureMethod, Is.Null, $"Runtime type '{runtimeDefinitionType.FullName}' must not contain the test-only extension implementation.");
            }
            BindingFlags declaredInstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Assert.That(typeof(PropertyComponent).GetMethod("Configure", declaredInstanceMembers), Is.Null, "PropertyComponent must be initialized by Unity lifecycle instead of a test-only Configure method.");
            Assert.That(typeof(PropertyComponent).GetField("propertiesInitialized", declaredInstanceMembers), Is.Null, "PropertyComponent must not retain the test-only lazy property initialization flag.");
            Assert.That(typeof(PropertyComponent).GetField("hpInitialized", declaredInstanceMembers), Is.Null, "PropertyComponent must not retain the test-only HP initialization flag.");
            Assert.That(typeof(EffectLibrary).GetMethod("RegisterAll", declaredInstanceMembers), Is.Null, "EffectLibrary must not expose the test-only combined registration helper.");
            Assert.That(typeof(EffectLibrary).GetMethod("PublishFireAttack", declaredInstanceMembers), Is.Null, "EffectLibrary must not expose a test-only example attack publisher.");
        }

        /// <summary>
        /// 验证 GameplayKit 使用启动参数注入的持久化 Effect Library，而不是创建不会响应资产修改的内存回退配置。
        /// </summary>
        [Test]
        public void GameplayKit_UsesConfiguredPersistentEffectLibrary()
        {
            const string libraryPath = "Assets/BundleResources/Config/Effect/EffectLibrary.asset";
            EffectLibrary persistentLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(libraryPath);
            GameObject runtimeRootObject = new GameObject("EffectTest.GameplayRoot");
            AssetKit assetKit = new AssetKit();
            GameplayKit gameplayKit = new GameplayKit(assetKit);
            try
            {
                Assert.That(persistentLibrary, Is.Not.Null);
                GameplayStartupOptions options = new GameplayStartupOptions(AssetKit.DefaultPackageName, runtimeRootObject.transform, persistentLibrary, "Character_Yefa", "Enemy_Slime", Array.Empty<Transform>(), 0);
                gameplayKit.Configure(options);
                Assert.That(gameplayKit.GetSystem<EffectSystem>().DefaultLibrary, Is.SameAs(persistentLibrary));
                Assert.That(gameplayKit.GetSystem<CombatAudioPresentationSystem>(), Is.Not.Null, "正式 GameplayKit 必须注册单局伤害音频表现系统。");
            }
            finally
            {
                gameplayKit.Dispose();
                assetKit.Dispose();
                UnityEngine.Object.DestroyImmediate(runtimeRootObject);
            }
        }

        /// <summary>验证伤害音频表现只消费实际 DamageApplied，且致命伤害不会因死亡动画抢占受击动画而丢失音效。</summary>
        [Test]
        public void CombatAudioPresentation_PlaysEveryPositiveDamageIncludingFatalExactlyOnce()
        {
            EffectLibrary library = ScriptableObject.CreateInstance<EffectLibrary>();
            AssetKit assetKit = new AssetKit();
            GameplayKit gameplayKit = new GameplayKit(assetKit);
            EffectSystem effectSystem = new EffectSystem(library);
            int playCount = 0;
            FmodAudioEvent playedEvent = FmodAudioEvent.None;
            Vector3 playedPosition = default;
            CombatAudioPresentationSystem audioSystem = new CombatAudioPresentationSystem(FmodAudioEvent.CombatSharedHit_Flesh, (audioEvent, worldPosition) =>
            {
                playCount++;
                playedEvent = audioEvent;
                playedPosition = worldPosition;
                return true;
            });
            try
            {
                gameplayKit.AddSystem(effectSystem);
                gameplayKit.AddSystem(audioSystem);
                effectSystem.AfterNew(gameplayKit);
                audioSystem.AfterNew(gameplayKit);
                Vector3 fatalHitPosition = new Vector3(3f, 2f, 1f);
                effectSystem.Runtime.Publish(new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 150f, 100f, EffectTag.Attack, position: fatalHitPosition, wasFatal: true));
                Assert.That(playCount, Is.EqualTo(1), "致命实际伤害必须绕过受击动画并恰好播放一次命中音效。");
                Assert.That(playedEvent, Is.EqualTo(FmodAudioEvent.CombatSharedHit_Flesh));
                Assert.That(playedPosition, Is.EqualTo(fatalHitPosition));
                effectSystem.Runtime.Publish(new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack, position: Vector3.one));
                Assert.That(playCount, Is.EqualTo(2), "非致命实际伤害必须使用同一个表现入口且恰好播放一次。");
                effectSystem.Runtime.Publish(new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 0f, EffectTag.Attack, position: Vector3.one));
                effectSystem.Runtime.Publish(new EffectSignal(EffectSignalType.Healed, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Healing, position: Vector3.one));
                Assert.That(playCount, Is.EqualTo(2), "零实际伤害和非伤害结果信号不得播放命中音效。");
                audioSystem.Dispose();
                effectSystem.Runtime.Publish(new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack, position: Vector3.one));
                Assert.That(playCount, Is.EqualTo(2), "系统释放后必须解除 EffectRuntime 订阅。");
            }
            finally
            {
                gameplayKit.Dispose();
                assetKit.Dispose();
                UnityEngine.Object.DestroyImmediate(library);
            }
        }

        /// <summary>验证只读表现观察者之间相互隔离，单个音频或特效模块异常不会中断信号事务和其他观察者。</summary>
        [Test]
        public void SignalProcessed_FailingPresentationObserverDoesNotBreakCombatTransaction()
        {
            int successfulObserverCount = 0;
            runtime.SignalProcessed += _ => throw new InvalidOperationException("Expected presentation failure.");
            runtime.SignalProcessed += _ => successfulObserverCount++;
            Assert.DoesNotThrow(() => runtime.Publish(new EffectSignal(EffectSignalType.DamageApplied, sourceEntity, targetEntity, sourceEntity, 10f, 10f, EffectTag.Attack)));
            Assert.That(successfulObserverCount, Is.EqualTo(1));
        }

        /// <summary>
        /// 验证 EffectLogic 只能直接保存玩法 Component，防止 EffectRuntime 或 IDisposable 等状态重新泄漏回 Logic。
        /// </summary>
        [Test]
        public void EffectLogic_DeclaresOnlyGameplayComponentFields()
        {
            FieldInfo[] fields = typeof(EffectLogic).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(fields, Is.Not.Empty);

            foreach (FieldInfo field in fields)
                Assert.That(typeof(IComponent).IsAssignableFrom(field.FieldType), Is.True, $"EffectLogic field '{field.Name}' must implement IComponent, but its type is '{field.FieldType.FullName}'.");
        }

        /// <summary>
        /// 从持久化 EffectDefinition 的伤害操作读取数值公式，并使用本次测试信号计算配置伤害。
        /// </summary>
        private static float ReadConfiguredDamage(EffectDefinition definition, EffectSignal signal)
        {
            return Mathf.Max(0f, ReadConfiguredOperationFormula<DamageOperation>(definition, signal, "amount"));
        }

        /// <summary>从正式 EffectDefinition 的指定操作读取公式并独立计算当前配置值，防止资产集成测试复制容易过期的数值常量。</summary>
        private static float ReadConfiguredOperationFormula<TOperation>(EffectDefinition definition, EffectSignal signal, string formulaPropertyName) where TOperation : EffectOperation
        {
            Assert.That(definition, Is.Not.Null);
            Assert.That(signal, Is.Not.Null);
            Assert.That(formulaPropertyName, Is.Not.Empty);
            SerializedObject serializedDefinition = new SerializedObject(definition);
            SerializedProperty operations = serializedDefinition.FindProperty("onApplyOperations");
            int matchingOperationCount = 0;
            float configuredValue = 0f;
            for (int index = 0; index < operations.arraySize; index++)
            {
                SerializedProperty operation = operations.GetArrayElementAtIndex(index);
                if (!(operation.managedReferenceValue is TOperation)) continue;
                matchingOperationCount++;
                configuredValue += EvaluateConfiguredFormula(operation.FindPropertyRelative(formulaPropertyName), signal);
            }
            Assert.That(matchingOperationCount, Is.EqualTo(1), $"Persistent effect '{definition.EffectId}' must contain exactly one {typeof(TOperation).Name} in onApplyOperations.");
            return configuredValue;
        }

        /// <summary>从正式 TriggerSet 中读取指定信号和条件的唯一阈值，使触发次数始终根据资产当前配置推导。</summary>
        private static float ReadConfiguredConditionThreshold(EffectTriggerSet triggerSet, EffectSignalType signalType, EffectConditionType conditionType)
        {
            Assert.That(triggerSet, Is.Not.Null);
            SerializedObject serializedTriggerSet = new SerializedObject(triggerSet);
            SerializedProperty triggers = serializedTriggerSet.FindProperty("triggers");
            int matchingConditionCount = 0;
            float configuredThreshold = 0f;
            for (int triggerIndex = 0; triggerIndex < triggers.arraySize; triggerIndex++)
            {
                SerializedProperty trigger = triggers.GetArrayElementAtIndex(triggerIndex);
                if ((EffectSignalType)trigger.FindPropertyRelative("signalType").intValue != signalType) continue;
                SerializedProperty conditions = trigger.FindPropertyRelative("conditions");
                for (int conditionIndex = 0; conditionIndex < conditions.arraySize; conditionIndex++)
                {
                    SerializedProperty condition = conditions.GetArrayElementAtIndex(conditionIndex);
                    if ((EffectConditionType)condition.FindPropertyRelative("type").intValue != conditionType) continue;
                    matchingConditionCount++;
                    configuredThreshold = condition.FindPropertyRelative("threshold").floatValue;
                }
            }
            Assert.That(matchingConditionCount, Is.EqualTo(1), $"Persistent trigger set '{triggerSet.name}' must contain exactly one {conditionType} condition for {signalType}.");
            return configuredThreshold;
        }

        /// <summary>
        /// 从持久化 EffectDefinition 的属性修改操作累计指定属性的 Boost 与 Offset，并计算该资产当前配置对应的最终值。
        /// </summary>
        private static float CalculateConfiguredPropertyValue(EffectDefinition definition, PropertyType propertyType, float baseValue, EffectSignal signal)
        {
            SerializedObject serializedDefinition = new SerializedObject(definition);
            SerializedProperty operations = serializedDefinition.FindProperty("onApplyOperations");
            int propertyOperationCount = 0;
            float boost = 1f;
            float offset = 0f;
            for (int index = 0; index < operations.arraySize; index++)
            {
                SerializedProperty operation = operations.GetArrayElementAtIndex(index);
                if (!(operation.managedReferenceValue is PropertyModifierOperation)) continue;
                if ((PropertyType)operation.FindPropertyRelative("propertyType").enumValueIndex != propertyType) continue;
                propertyOperationCount++;
                float configuredValue = EvaluateConfiguredFormula(operation.FindPropertyRelative("valuePerStack"), signal);
                PropertyModifierMode modifierMode = (PropertyModifierMode)operation.FindPropertyRelative("modifierMode").enumValueIndex;
                if (modifierMode == PropertyModifierMode.Boost) boost += configuredValue;
                else offset += configuredValue;
            }
            Assert.That(propertyOperationCount, Is.GreaterThan(0), $"Persistent effect '{definition.EffectId}' must contain a PropertyModifierOperation for '{propertyType}' in onApplyOperations.");
            return baseValue * boost + offset;
        }

        /// <summary>
        /// 按 EffectValueFormula 的序列化 baseValueSource、multiplier 和 offset 计算期望值，使测试数值始终来源于当前资产配置。
        /// </summary>
        private static float EvaluateConfiguredFormula(SerializedProperty formula, EffectSignal signal)
        {
            Assert.That(formula, Is.Not.Null);
            EffectValueSource baseValueSource = (EffectValueSource)formula.FindPropertyRelative("baseValueSource").intValue;
            float baseValue;
            switch (baseValueSource)
            {
                case EffectValueSource.One: baseValue = 1f; break;
                case EffectValueSource.SignalValue: baseValue = signal.Value; break;
                case EffectValueSource.SignalRequestedValue: baseValue = signal.RequestedValue; break;
                case EffectValueSource.Property: baseValue = ReadConfiguredProperty(signal, formula); break;
                default: baseValue = 0f; break;
            }
            float multiplier = formula.FindPropertyRelative("multiplier").floatValue;
            float offset = formula.FindPropertyRelative("offset").floatValue;
            return baseValue * multiplier + offset;
        }

        /// <summary>
        /// 从公式引用的测试实体读取属性组件，保持期望值计算与 EffectValueFormula 的实体来源语义一致。
        /// </summary>
        private static float ReadConfiguredProperty(EffectSignal signal, SerializedProperty formula)
        {
            EffectValueEntity entitySource = (EffectValueEntity)formula.FindPropertyRelative("propertyEntity").enumValueIndex;
            EffectPropertyValue propertyValue = (EffectPropertyValue)formula.FindPropertyRelative("propertyValue").enumValueIndex;
            Entity entity = entitySource == EffectValueEntity.Caster ? signal.Caster : entitySource == EffectValueEntity.Target ? signal.Target : signal.Source;
            if (entity == null) return 0f;
            if (!entity.TryGetComp(out PropertyComponent property)) return 0f;
            switch (propertyValue)
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
                case EffectPropertyValue.Toughness: return property.Toughness;
                case EffectPropertyValue.DamageBoost: return property.DamageBonus;
                case EffectPropertyValue.DamageTakenBoost: return property.DamageTakenBonus;
                default: return 0f;
            }
        }

        /// <summary>
        /// 在当前编辑器加载的程序集里查找指定类型，避免测试程序集对 Editor 文件的具体程序集名称形成脆弱依赖。
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        /// 从当前 Editor 程序集中创建指定 PropertyDrawer，并为类型缺失或构造失败提供清晰的测试错误。
        /// </summary>
        private static PropertyDrawer CreatePropertyDrawer(string fullName)
        {
            Type drawerType = FindType(fullName);
            Assert.That(drawerType, Is.Not.Null, $"Editor drawer type '{fullName}' must be loaded.");
            object drawer = Activator.CreateInstance(drawerType);
            Assert.That(drawer, Is.InstanceOf<PropertyDrawer>(), $"Editor drawer type '{fullName}' must derive from PropertyDrawer.");
            return (PropertyDrawer)drawer;
        }

        /// <summary>
        /// TestEntity 只注册效果示例需要的属性和事件组件，避免测试依赖场景预制体。
        /// </summary>
        private sealed class TestEntity : Entity
        {
            /// <summary>
            /// 创建测试实体并连接指定 PropertyComponent。
            /// </summary>
            public TestEntity(GameObject gameObject, PropertyComponent property)
            {
                bindGo = gameObject;
                AddComp(property);
                AddComp<EventComponent>();
            }
        }

        /// <summary>记录即时效果被执行的次数，用于观察无法直接暴露到业务层的 Killed Signal。</summary>
        [Serializable]
        private sealed class CountingOperation : EffectOperation
        {
            /// <summary>获取当前测试事务累计执行次数。</summary>
            public int ExecutionCount { get; private set; }

            /// <inheritdoc />
            public override void Execute(EffectOperationContext context)
            {
                ExecutionCount++;
            }
        }

        /// <summary>保存最近一次操作接收到的 DamageApplied 信号，供伤害属性结算测试检查完整上下文。</summary>
        [Serializable]
        private sealed class DamageSignalCaptureOperation : EffectOperation
        {
            /// <summary>获取最近一次执行时收到的不可变战斗信号。</summary>
            public EffectSignal Signal { get; private set; }

            /// <inheritdoc />
            public override void Execute(EffectOperationContext context)
            {
                Signal = context.Signal;
            }
        }

        /// <summary>记录一次操作拿到的实体关系，用于验证 Caster 与 Source 的新语义不会在请求链中被反转。</summary>
        [Serializable]
        private sealed class RelationCaptureOperation : EffectOperation
        {
            /// <summary>获取直接释放当前行为的实体。</summary>
            public Entity Caster { get; private set; }

            /// <summary>获取当前行为的目标实体。</summary>
            public Entity Target { get; private set; }

            /// <summary>获取整条因果链的实际源头实体。</summary>
            public Entity Source { get; private set; }

            /// <inheritdoc />
            public override void Execute(EffectOperationContext context)
            {
                Caster = context.Caster;
                Target = context.Target;
                Source = context.Source;
            }
        }

        /// <summary>批量计算一组数值公式，用于验证实体来源与运行时属性的完整组合矩阵。</summary>
        [Serializable]
        private sealed class FormulaCaptureOperation : EffectOperation
        {
            /// <summary>保存本次测试需要依次计算的全部公式。</summary>
            private readonly EffectValueFormula[] formulas;

            /// <summary>获取最近一次执行产生的公式结果；执行前为空数组。</summary>
            public float[] Values { get; private set; } = Array.Empty<float>();

            /// <summary>创建持有指定公式快照的捕获操作，避免测试执行期间修改共享配置。</summary>
            public FormulaCaptureOperation(EffectValueFormula[] valueFormulas)
            {
                formulas = valueFormulas == null ? Array.Empty<EffectValueFormula>() : (EffectValueFormula[])valueFormulas.Clone();
            }

            /// <inheritdoc />
            public override void Execute(EffectOperationContext context)
            {
                Values = new float[formulas.Length];
                for (int index = 0; index < formulas.Length; index++) Values[index] = formulas[index] == null ? 0f : formulas[index].Evaluate(context);
            }
        }

        /// <summary>
        /// 记录测试 Logic 的启用、禁用和更新次数，供 Entity 控制能力门禁测试复用。
        /// </summary>
        private abstract class RecordingControlLogic : Xuan.Prometheus.Logic.Logic
        {
            /// <summary>获取 Logic 被启用的累计次数。</summary>
            public int EnableCount { get; private set; }

            /// <summary>获取 Logic 被禁用的累计次数。</summary>
            public int DisableCount { get; private set; }

            /// <summary>获取 Logic 实际执行更新的累计次数。</summary>
            public int UpdateCount { get; private set; }

            /// <inheritdoc />
            public override void AfterNew()
            {
            }

            /// <inheritdoc />
            public override bool CanEnable()
            {
                return true;
            }

            /// <inheritdoc />
            public override bool CanDisable()
            {
                return false;
            }

            /// <inheritdoc />
            public override void OnEnable()
            {
                EnableCount++;
            }

            /// <inheritdoc />
            public override void OnDisable()
            {
                DisableCount++;
            }

            /// <inheritdoc />
            public override void OnUpdate(float dt)
            {
                UpdateCount++;
            }

            /// <inheritdoc />
            public override void OnDispose()
            {
            }
        }

        /// <summary>使用 Logic 默认 Act 需求的测试逻辑。</summary>
        private sealed class DefaultActTestLogic : RecordingControlLogic
        {
        }

        /// <summary>只声明 Move 能力需求的测试逻辑。</summary>
        private sealed class MoveRequirementTestLogic : RecordingControlLogic
        {
            /// <summary>创建需要移动能力的测试逻辑。</summary>
            public MoveRequirementTestLogic()
            {
                ControlRequirement = LogicControlRequirement.Move;
            }
        }

        /// <summary>只声明 ActiveSkill 能力需求的测试逻辑。</summary>
        private sealed class ActiveSkillRequirementTestLogic : RecordingControlLogic
        {
            /// <summary>创建需要主动技能能力的测试逻辑。</summary>
            public ActiveSkillRequirementTestLogic()
            {
                ControlRequirement = LogicControlRequirement.ActiveSkill;
            }
        }
    }
}
