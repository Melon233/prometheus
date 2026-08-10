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

        /// <summary>验证首次致死伤害只发布一次 DieEvent 和 Killed Signal，后续命中与治疗都不能让尸体再次结算死亡。</summary>
        [Test]
        public void FatalDamage_EmitsDeathAndKilledExactlyOnce()
        {
            registrations = runtime.RegisterTriggerSet(sourceEntity, examples.AttackTriggers);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int deathCount = 0;
            int hpChangedCount = 0;
            int staggeredCount = 0;
            targetEvents.AddListener<DieEvent>(_ => deathCount++);
            targetEvents.AddListener<HpChangedEvent>(_ => hpChangedCount++);
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            CountingOperation killedCounter = new CountingOperation();
            EffectDefinition killedCounterEffect = ScriptableObject.CreateInstance<EffectDefinition>();
            killedCounterEffect.name = "Tests.KilledCounterEffect";
            killedCounterEffect.ConfigureForTests("Tests.KilledCounterEffect", EffectTag.None, EffectDurationType.Instant, 0f, 0f, EffectStackPolicy.Reject, EffectStackKeyPolicy.Definition, 1, EffectExecutionPhase.AfterApply, 0, new EffectOperation[] { killedCounter }, null, null, null);
            EffectTriggerDefinition killedTrigger = new EffectTriggerDefinition();
            killedTrigger.ConfigureForTests("Tests.OnKilled.Count", EffectSignalType.Killed, EffectListenScope.Source, EffectTargetSelector.Source, 1f, 0f, true, 0, Array.Empty<EffectConditionDefinition>(), new[] { killedCounterEffect });
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
            targetProperty.SetBaseValue(PropertyType.Toughness, 1f);
            targetEntity.TryGetComp(out EventComponent targetEvents);
            int staggeredCount = 0;
            targetEvents.AddListener<StaggeredEvent>(_ => staggeredCount++);
            float sourceAttackBeforeEffect = sourceProperty.Atk;
            float sourceAttackSpeedBeforeEffect = sourceProperty.AtkSpeed;
            float targetHpBeforeEffect = targetProperty.Hp;
            EffectSignal fireAttackSignal = new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetEntity, sourceEntity, sourceAttackBeforeEffect, sourceAttackBeforeEffect, EffectTag.Attack | EffectTag.NormalAttack | EffectTag.Fire, "Example.FireAttack");
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
                addOperation.Invoke(definitionEditor, new object[] { "onApplyOperations", new PropertyModifierOperation("Copied.Property", PropertyType.Def, PropertyModifierMode.Offset, EffectValueFormula.SourceAttack(2f, 3f)) });
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
                Assert.That(pastedValuePerStack.FindPropertyRelative("source").enumValueIndex, Is.EqualTo((int)EffectValueSource.SourceAttack));
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
            }
            finally
            {
                gameplayKit.Dispose();
                assetKit.Dispose();
                UnityEngine.Object.DestroyImmediate(runtimeRootObject);
            }
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
            SerializedObject serializedDefinition = new SerializedObject(definition);
            SerializedProperty operations = serializedDefinition.FindProperty("onApplyOperations");
            int damageOperationCount = 0;
            float configuredDamage = 0f;
            for (int index = 0; index < operations.arraySize; index++)
            {
                SerializedProperty operation = operations.GetArrayElementAtIndex(index);
                if (!(operation.managedReferenceValue is DamageOperation)) continue;
                damageOperationCount++;
                configuredDamage += Mathf.Max(0f, EvaluateConfiguredFormula(operation.FindPropertyRelative("amount"), signal));
            }
            Assert.That(damageOperationCount, Is.EqualTo(1), $"Persistent effect '{definition.EffectId}' must contain exactly one DamageOperation in onApplyOperations.");
            return configuredDamage;
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
        /// 按 EffectValueFormula 的序列化 source、multiplier 和 offset 计算期望值，使测试数值始终来源于当前资产配置。
        /// </summary>
        private static float EvaluateConfiguredFormula(SerializedProperty formula, EffectSignal signal)
        {
            Assert.That(formula, Is.Not.Null);
            EffectValueSource source = (EffectValueSource)formula.FindPropertyRelative("source").enumValueIndex;
            float baseValue;
            switch (source)
            {
                case EffectValueSource.Constant: baseValue = 1f; break;
                case EffectValueSource.SignalValue: baseValue = signal.Value; break;
                case EffectValueSource.SignalRequestedValue: baseValue = signal.RequestedValue; break;
                case EffectValueSource.SourceAttack: baseValue = ReadConfiguredProperty(signal.Source, property => property.Atk); break;
                case EffectValueSource.TargetAttack: baseValue = ReadConfiguredProperty(signal.Target, property => property.Atk); break;
                case EffectValueSource.SourceMaxHp: baseValue = ReadConfiguredProperty(signal.Source, property => property.MaxHp); break;
                case EffectValueSource.TargetMaxHp: baseValue = ReadConfiguredProperty(signal.Target, property => property.MaxHp); break;
                case EffectValueSource.SourceCoreEnergy: baseValue = ReadConfiguredProperty(signal.Source, property => property.CoreEnergy); break;
                default: baseValue = 0f; break;
            }
            float multiplier = formula.FindPropertyRelative("multiplier").floatValue;
            float offset = formula.FindPropertyRelative("offset").floatValue;
            return baseValue * multiplier + offset;
        }

        /// <summary>
        /// 从公式引用的测试实体读取属性组件，保持期望值计算与 EffectValueFormula 的实体来源语义一致。
        /// </summary>
        private static float ReadConfiguredProperty(Entity entity, Func<PropertyComponent, float> reader)
        {
            if (entity == null) return 0f;
            if (!entity.TryGetComp(out PropertyComponent property)) return 0f;
            return reader(property);
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
