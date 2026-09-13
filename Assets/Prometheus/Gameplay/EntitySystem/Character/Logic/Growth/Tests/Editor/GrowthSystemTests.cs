using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Logic.Talent;
using Xuan.Prometheus.Elements;
using Xuan.Prometheus.Shields;

namespace Xuan.Prometheus.Growth.Tests
{
    /// <summary>使用正式 Yefa Prefab 验证四类养成数据、公式、永久 Effect 投影和 PlayerEntity 组合。</summary>
    public sealed class GrowthSystemTests
    {
        /// <summary>正式玩家 Prefab 路径，保证测试覆盖实际序列化 Debug 配置与档位预设。</summary>
        private const string YefaPrefabPath = "Assets/BundleResources/Character/Yefa.prefab";
        /// <summary>正式 EffectLibrary 路径，保证 TalentLogic 的战斗心流触发注册具有完整配置。</summary>
        private const string EffectLibraryPath = "Assets/BundleResources/Config/Effect/EffectLibrary.asset";
        /// <summary>正式测试装备 Definition SO 路径，验证装备实例只引用只读资产配置。</summary>
        private const string EquipmentDefinitionPath = "Assets/BundleResources/Config/Growth/YefaTrainingEquipment.asset";
        /// <summary>保存测试独占的资源 Kit。</summary>
        private AssetKit assetKit;
        /// <summary>保存测试独占的配表 Kit；等级与突破全部由配表驱动，因此养成链路必须先有表。</summary>
        private ConfigKit configKit;
        /// <summary>保存测试独占的玩法世界。</summary>
        private GameplayKit gameplayKit;
        /// <summary>保存测试独占的 EntitySystem。</summary>
        private EntitySystem entitySystem;
        /// <summary>保存测试独占的效果系统替身，避免绕过正式 EffectSystem 的内部资源加载职责。</summary>
        private TestEffectSystem effectSystem;

        /// <summary>实体移除会广播 EntityRemovedEvent，因此本夹具必须提供全局事件入口。</summary>
        private EventKit eventKit;
        /// <summary>保存正式 Yefa Prefab 实例。</summary>
        private GameObject yefaInstance;
        /// <summary>保存只组合养成链路依赖的测试 Entity。</summary>
        private GrowthTestEntity entity;

        /// <summary>创建单局 EffectRuntime、实例化正式 Yefa，并初始化四种养成 Logic。</summary>
        /// <summary>按真实配表建立一个元素系统；养成用例不验证反应，但伤害结算链路需要它在场。</summary>
        private static ElementSystem CreateElementSystem()
        {
            ElementSystem created = new ElementSystem();
            created.AfterNew();
            return created;
        }

        [SetUp]
        public void SetUp()
        {
            assetKit = new AssetKit();
            Core.Asset = assetKit;
            configKit = new ConfigKit();
            Core.Config = configKit;
            // 编辑器测试没有资源包，直接从 AssetDatabase 读导出的 .bytes 喂表。
            configKit.LoadFrom(LoadTableFromAssetDatabase);
            eventKit = new EventKit();
            Core.Event = eventKit;
            gameplayKit = new GameplayKit();
            Core.Gameplay = gameplayKit;
            entitySystem = new EntitySystem();
            gameplayKit.AddSystem<IEntitySystem>(entitySystem);
            EffectLibrary effectLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(EffectLibraryPath);
            Assert.That(effectLibrary, Is.Not.Null, $"无法加载正式效果库：{EffectLibraryPath}");
            effectSystem = new TestEffectSystem(effectLibrary, CreateElementSystem());
            gameplayKit.AddSystem<IEffectSystem>(effectSystem);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            yefaInstance = Object.Instantiate(prefab);
            entity = new GrowthTestEntity(yefaInstance);
            entitySystem.AddEntity(entity);
            entity.AfterNew();
        }

        /// <summary>按运行时依赖逆序释放 Entity、System、资源和剩余测试对象。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
            Core.Gameplay = null;
            entitySystem = null;
            effectSystem = null;
            eventKit?.Dispose();
            eventKit = null;
            Core.Event = null;
            assetKit?.Dispose();
            assetKit = null;
            configKit?.Dispose();
            configKit = null;
            Core.Config = null;
            if (yefaInstance != null) Object.DestroyImmediate(yefaInstance);
            yefaInstance = null;
            entity = null;
        }

        /// <summary>按表名从 AssetDatabase 读取导出的二进制表，替代运行时的资源包加载。</summary>
        private static Luban.ByteBuf LoadTableFromAssetDatabase(string tableName)
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>($"Assets/BundleResources/Table/{tableName}.bytes");
            Assert.That(asset, Is.Not.Null, $"无法加载导出的配表：{tableName}.bytes；请先执行 Prometheus/Luban/导出配表。");
            return new Luban.ByteBuf(asset.bytes);
        }

        /// <summary>验证 PlayerEntity 已注册三个新增 Component 和对应 Logic，避免 Prefab 与组合根遗漏链路。</summary>
        [Test]
        public void PlayerEntity_ComposesAllGrowthComponentsAndLogics()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                PlayerEntity player = new PlayerEntity(instance);
                entitySystem.AddEntity(player);
                player.AfterNew();
                Assert.That(player.TryGetComp(out CharaLevelComponent level), Is.True);
                Assert.That(player.TryGetComp(out EquipmentComponent equipment), Is.True);
                Assert.That(player.TryGetComp(out WeaponComponent weapon), Is.True);
                Assert.That(AssetDatabase.Contains(level.Config), Is.True, "角色等级配置必须是被 Prefab 引用的持久化 SO 资产。");
                Assert.That(AssetDatabase.Contains(equipment.Config), Is.True, "装备配置必须是被 Prefab 引用的持久化 SO 资产。");
                Assert.That(AssetDatabase.Contains(weapon.Config), Is.True, "武器配置必须是被 Prefab 引用的持久化 SO 资产。");
                Assert.That(player.TryGetLogic(out CharaLevelLogic _), Is.True);
                Assert.That(player.TryGetLogic(out EquipmentLogic _), Is.True);
                Assert.That(player.TryGetLogic(out WeaponLogic _), Is.True);
                entitySystem.RemoveEntity(player.EntityId);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>验证 Debug 初始值、升级公式、天赋成长、装备汇总、武器经验与隐藏永久 Effect 的完整运行链路。</summary>
        [Test]
        public void GrowthLogics_ApplyConfiguredDataThroughPermanentEffects()
        {
            Assert.That(entity.TryGetComp(out PropertyComponent property), Is.True);
            Assert.That(entity.TryGetComp(out CharaLevelComponent level), Is.True);
            Assert.That(entity.TryGetComp(out EquipmentComponent equipment), Is.True);
            Assert.That(entity.TryGetComp(out WeaponComponent weapon), Is.True);
            Assert.That(entity.TryGetComp(out SkillComponent skill), Is.True);
            float initialAttack = property.Atk;

            Assert.That(level.CurrentLevel, Is.EqualTo(1));
            Assert.That(level.CurrentLevelExperience, Is.Zero);
            Assert.That(level.AscensionPhase, Is.Zero);
            Assert.That(level.LevelCap, Is.EqualTo(20), "零突破阶段的等级上限来自第一次突破的 requiredLevel。");
            Assert.That(property.Atk, Is.EqualTo(initialAttack).Within(0.0001f), "一级零突破时三项基础属性增量均为零。");

            // 逐级表：投入恰好一级所需的经验应当升一级，且当前等级内经验清零。
            Assert.That(level.AddExperience(120), Is.EqualTo(120));
            entity.OnUpdate(0f);
            Assert.That(level.CurrentLevel, Is.EqualTo(2));
            Assert.That(level.CurrentLevelExperience, Is.Zero);
            Assert.That(property.Atk, Is.GreaterThan(initialAttack), "等级提升后攻击力增量应当生效。");
            // 等级增量走 Base 通道，因此它与配置基础值相加后**一起**被装备与武器的 Boost 放大。
            float attackBaseAtLevelTwo = property.Atk;

            Assert.That(skill.TalentLevel, Is.EqualTo(1));
            Assert.That(skill.GainCoefficient, Is.Zero.Within(0.0001f));
            Assert.That(skill.TrySetTalentLevel(5), Is.True);
            entity.OnUpdate(0f);
            Assert.That(skill.GainCoefficient, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(skill.TalentScale, Is.EqualTo(1.4f).Within(0.0001f));

            EquipmentDefinition equipmentDefinition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(EquipmentDefinitionPath);
            Assert.That(equipmentDefinition, Is.Not.Null, $"无法加载正式装备定义：{EquipmentDefinitionPath}");
            Assert.That(AssetDatabase.Contains(equipmentDefinition), Is.True, "装备 Definition 必须是可复用的持久化 SO 资产。");
            Assert.That(equipment.TryEquip(0, equipmentDefinition), Is.True);
            Assert.That(equipment.GetEquipment(0).CurrentLevel, Is.Zero, "装备初始等级必须是零级。");
            Assert.That(equipment.AddExperience(0, equipment.MaximumExperience), Is.EqualTo(equipment.MaximumExperience).Within(0.0001f));
            entity.OnUpdate(0f);
            Assert.That(equipment.GetEquipment(0).CurrentLevel, Is.EqualTo(equipment.MaximumLevel));
            Assert.That(equipment.GetEquipment(0).Tiers[0].CurrentOffset, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(equipment.GetEquipment(0).Tiers[0].CurrentCoefficient, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(property.Atk, Is.EqualTo(attackBaseAtLevelTwo * 1.05f + 10f).Within(0.0001f), "等级的 Base 增量应当被装备的 Boost 一并放大，装备的 Offset 则在缩放之后加算。");

            Assert.That(weapon.CurrentLevel, Is.EqualTo(1));
            Assert.That(weapon.CurrentTotalExperience, Is.Zero);
            Assert.That(weapon.AddExperience(weapon.MaximumExperience), Is.EqualTo(weapon.MaximumExperience).Within(0.0001f));
            entity.OnUpdate(0f);
            Assert.That(weapon.CurrentLevel, Is.EqualTo(weapon.MaximumLevel));
            Assert.That(weapon.CurrentTotalExperience, Is.EqualTo(weapon.MaximumExperience).Within(0.0001f));
            Assert.That(weapon.Tiers.Count, Is.EqualTo(1));
            Assert.That(weapon.Tiers[0].CurrentOffset, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(weapon.Tiers[0].CurrentCoefficient, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(property.Atk, Is.EqualTo(attackBaseAtLevelTwo * 1.1f + 20f).Within(0.0001f), "角色、装备和武器的永久 Effect 应同时汇总到 (Base + ΣBase) × Boost + Offset 公式。");

            AssertPermanentGrowthEffect("CharaLevel");
            AssertPermanentGrowthEffect("Equipment.Tiers");
            AssertPermanentGrowthEffect("Weapon.Tiers");
            AssertPermanentGrowthEffect("Talent");
            System.Collections.Generic.List<EffectInstance> visibleBuffs = new System.Collections.Generic.List<EffectInstance>();
            Assert.That(entity.TryGetComp(out EffectComponent effectComponent), Is.True);
            effectComponent.CopyActiveBuffs(visibleBuffs);
            Assert.That(visibleBuffs, Is.Empty, "内部成长永久 Effect 不应污染 HUD Buff 列表。");
        }

        /// <summary>验证三个通道的语义差异：Base 参与缩放，Offset 不参与，Boost 是缩放本身。</summary>
        [Test]
        public void PropertyChannels_BaseIsScaledByBoostButOffsetIsNot()
        {
            ModifiableProperty property = new ModifiableProperty();
            property.SetBaseValue(100f);

            PropertyModifier baseModifier = new PropertyModifier(PropertyType.Atk, PropertyModifierMode.Base, 50f);
            PropertyModifier boostModifier = new PropertyModifier(PropertyType.Atk, PropertyModifierMode.Boost, 0.5f);
            PropertyModifier offsetModifier = new PropertyModifier(PropertyType.Atk, PropertyModifierMode.Offset, 20f);

            property.AddModifier(baseModifier);
            Assert.That(property.Value, Is.EqualTo(150f).Within(0.0001f), "Base 与配置基础值直接相加。");

            property.AddModifier(boostModifier);
            Assert.That(property.Value, Is.EqualTo(225f).Within(0.0001f), "Boost 缩放的是基础值与 Base 之和。");

            property.AddModifier(offsetModifier);
            Assert.That(property.Value, Is.EqualTo(245f).Within(0.0001f), "Offset 在缩放之后加算，不被 Boost 放大。");

            property.RemoveModifier(baseModifier);
            Assert.That(property.Value, Is.EqualTo(170f).Within(0.0001f), "移除 Base 后缩放基数回落到配置基础值。");
        }

        /// <summary>验证突破是等级的闸门：它不提升等级，只抬高上限，并让积压经验继续参与升级。</summary>
        [Test]
        public void Ascension_GatesLevelCapAndConsumesBankedExperience()
        {
            Assert.That(entity.TryGetComp(out CharaLevelComponent level), Is.True);
            Assert.That(level.MaximumAscensionPhase, Is.EqualTo(6), "Yefa 配表应提供六次突破。");

            // 投入远超一阶段所需的经验：等级应停在 20，多余经验积压而不是丢弃。
            level.AddExperience(1000000);
            entity.OnUpdate(0f);
            Assert.That(level.CurrentLevel, Is.EqualTo(20), "等级必须停在当前突破阶段的上限。");
            Assert.That(level.IsAwaitingAscension, Is.True);
            Assert.That(level.CurrentLevelExperience, Is.GreaterThan(0), "等待突破期间投入的经验应当保留。");

            int bankedBeforeAscension = level.CurrentLevelExperience;
            Assert.That(level.CanAscendByLevel(), Is.True);
            Assert.That(level.TryAscend(), Is.True);
            entity.OnUpdate(0f);

            Assert.That(level.AscensionPhase, Is.EqualTo(1));
            Assert.That(level.LevelCap, Is.EqualTo(40), "突破后等级上限抬高到下一阶段。");
            Assert.That(level.CurrentLevel, Is.GreaterThan(20), "积压经验应在突破后立即继续升级，不被浪费。");
            Assert.That(level.CurrentLevelExperience, Is.LessThan(bankedBeforeAscension));
        }

        /// <summary>验证突破的等级条件不满足时不产生任何副作用。</summary>
        [Test]
        public void Ascension_WithInsufficientLevel_IsRejected()
        {
            Assert.That(entity.TryGetComp(out CharaLevelComponent level), Is.True);

            Assert.That(level.CurrentLevel, Is.EqualTo(1));
            Assert.That(level.CanAscendByLevel(), Is.False);
            Assert.That(level.TryAscend(), Is.False);
            Assert.That(level.AscensionPhase, Is.Zero, "被拒绝的突破不得改变阶段。");
        }

        /// <summary>验证突破消耗以 MaterialCost 表达，且摩拉与材料一并给出，供背包统一校验。</summary>
        [Test]
        public void Ascension_ExposesCostIncludingMora()
        {
            Assert.That(entity.TryGetComp(out CharaLevelComponent level), Is.True);

            System.Collections.Generic.IReadOnlyList<MaterialCost> costs = level.GetNextAscensionCost();

            Assert.That(costs.Count, Is.EqualTo(4), "首次突破消耗为摩拉加三种材料。");
            Assert.That(costs[0].MaterialId, Is.EqualTo("MAT_MORA"));
            Assert.That(costs[0].Count, Is.EqualTo(20000));
            // 校验能力由背包提供；此处只确认消耗结构可以直接交给 IBagSystem.CheckCost。
            MaterialCostCheck check = MaterialCostEvaluator.Evaluate(costs, _ => 0);
            Assert.That(check.IsSatisfied, Is.False);
            Assert.That(check.Shortages.Count, Is.EqualTo(4));
        }

        /// <summary>验证第二属性从突破 2 才开始生效，突破 1 的档位为零。</summary>
        [Test]
        public void SecondaryAttribute_StartsAtSecondAscension()
        {
            Assert.That(entity.TryGetComp(out PropertyComponent property), Is.True);
            Assert.That(entity.TryGetComp(out CharaLevelComponent level), Is.True);
            float initialCritRate = property.CritRate;

            level.AddExperience(1000000);
            Assert.That(level.TryAscend(), Is.True);
            entity.OnUpdate(0f);
            Assert.That(property.CritRate, Is.EqualTo(initialCritRate).Within(0.0001f), "突破 1 的第二属性档位为零，不应改变暴击率。");

            Assert.That(level.TryAscend(), Is.True);
            entity.OnUpdate(0f);
            Assert.That(level.AscensionPhase, Is.EqualTo(2));
            Assert.That(property.CritRate, Is.GreaterThan(initialCritRate), "突破 2 起第二属性开始生效。");
        }

        /// <summary>验证非线性经验曲线决定装备和武器的离散等级阈值；角色等级已改为逐级表驱动，不再走曲线。</summary>
        [Test]
        public void ExperienceCurves_MapCumulativeExperienceToDiscreteLevels()
        {
            GameObject curveObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath));
            GameplayKit curveGameplayKit = null;
            AssetKit curveAssetKit = null;
            EffectLibrary library = AssetDatabase.LoadAssetAtPath<EffectLibrary>(EffectLibraryPath);
            EquipmentConfig equipmentConfig = null;
            WeaponConfig weaponConfig = null;
            try
            {
                PlayerBinder binder = curveObject.GetComponent<PlayerBinder>();
                Assert.That(binder, Is.Not.Null, "Yefa 必须在根节点持有 PlayerBinder。");
                equipmentConfig = Object.Instantiate(binder.EquipmentConfig);
                weaponConfig = Object.Instantiate(binder.WeaponConfig);
                SetSerializedGrowthCurve(equipmentConfig, 4, 100f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetSerializedGrowthCurve(weaponConfig, 5, 100f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetBinderConfig(binder, "equipmentConfig", equipmentConfig);
                SetBinderConfig(binder, "weaponConfig", weaponConfig);
                curveAssetKit = new AssetKit();
                Core.Asset = curveAssetKit;
                curveGameplayKit = new GameplayKit();
                Core.Gameplay = curveGameplayKit;
                curveGameplayKit.AddSystem<IEntitySystem>(new EntitySystem());
                TestEffectSystem curveEffectSystem = new TestEffectSystem(library, CreateElementSystem());
                curveGameplayKit.AddSystem<IEffectSystem>(curveEffectSystem);
                GrowthTestEntity curveEntity = new GrowthTestEntity(curveObject);
                curveGameplayKit.GetSystem<IEntitySystem>().AddEntity(curveEntity);
                curveEntity.AfterNew();
                Assert.That(curveEntity.TryGetComp(out EquipmentComponent equipment), Is.True);
                Assert.That(curveEntity.TryGetComp(out WeaponComponent weapon), Is.True);
                EquipmentDefinition definition = AssetDatabase.LoadAssetAtPath<EquipmentDefinition>(EquipmentDefinitionPath);
                Assert.That(definition, Is.Not.Null, $"无法加载正式装备定义：{EquipmentDefinitionPath}");
                Assert.That(equipment.TryEquip(0, definition), Is.True);
                Assert.That(equipment.AddExperience(0, 50f), Is.EqualTo(50f).Within(0.0001f));
                Assert.That(equipment.GetEquipment(0).CurrentLevel, Is.EqualTo(2), "装备从零级开始映射曲线等级进度。");
                Assert.That(weapon.AddExperience(50f), Is.EqualTo(50f).Within(0.0001f));
                Assert.That(weapon.CurrentLevel, Is.EqualTo(3), "武器参考角色从一级开始映射曲线等级进度。");
            }
            finally
            {
                curveGameplayKit?.Dispose();
                curveAssetKit?.Dispose();
                Core.Asset = assetKit;
                Core.Gameplay = gameplayKit;
                if (curveObject != null) Object.DestroyImmediate(curveObject);
                if (equipmentConfig != null) Object.DestroyImmediate(equipmentConfig);
                if (weaponConfig != null) Object.DestroyImmediate(weaponConfig);
            }
        }

        /// <summary>验证指定成长通道存在活动永久 Effect，并带有 Growth 标签。</summary>
        private void AssertPermanentGrowthEffect(string channel)
        {
            EffectInstance instance = effectSystem.Runtime.GetActiveEffect(entity, $"Growth.{entity.EntityId}.{channel}");
            Assert.That(instance, Is.Not.Null, $"缺少成长永久 Effect：{channel}");
            Assert.That(instance.Definition.DurationType, Is.EqualTo(EffectDurationType.Permanent));
            Assert.That((instance.Definition.Tags & EffectTag.Growth) != 0, Is.True);
        }

        /// <summary>用 SerializedObject 为三个不同 Component 写入相同的等级上限、满级经验和测试曲线。</summary>
        private static void SetSerializedGrowthCurve(ScriptableObject config, int maximumLevel, float maximumExperience, AnimationCurve curve)
        {
            SerializedObject serializedObject = new SerializedObject(config);
            serializedObject.FindProperty("maximumLevel").intValue = maximumLevel;
            serializedObject.FindProperty("maximumExperience").floatValue = maximumExperience;
            serializedObject.FindProperty("experienceCurve").animationCurveValue = curve;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>把测试专用配置 SO 副本写入实例 Binder 的指定字段，不修改正式 Prefab 资产。</summary>
        private static void SetBinderConfig(PlayerBinder binder, string fieldName, ScriptableObject config)
        {
            SerializedObject serializedObject = new SerializedObject(binder);
            serializedObject.FindProperty(fieldName).objectReferenceValue = config;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>为养成规则测试提供内存 EffectRuntime 与指定只读配置，不改变正式 EffectSystem 的资源边界。</summary>
        private sealed class TestEffectSystem : XSystem, IEffectSystem
        {
            /// <summary>创建测试独占的效果运行时，并保存测试要验证的正式效果配置。</summary>
            /// <param name="defaultLibrary">测试实体注册触发规则时使用的只读效果配置库。</param>
            /// <param name="elements">元素系统；伤害结算经过它判定附着与反应。</param>
            public TestEffectSystem(EffectLibrary defaultLibrary, IElementSystem elements)
            {
                DefaultLibrary = defaultLibrary;
                Runtime = new EffectRuntime(1977, elements, new ShieldSystem());
            }

            /// <summary>获取测试效果系统是否已经释放。</summary>
            public bool IsDisposed { get; private set; }

            /// <summary>获取测试独占的效果运行时。</summary>
            public EffectRuntime Runtime { get; }

            /// <summary>获取测试指定的只读效果配置库。</summary>
            public EffectLibrary DefaultLibrary { get; }

            /// <summary>释放测试运行时持有的效果实例和注册句柄。</summary>
            public override void Dispose()
            {
                if (IsDisposed) return;
                Runtime.Dispose();
                IsDisposed = true;
            }
        }

        /// <summary>只组合养成系统需要的纯 C# Component、根 Binder 表现和 Logic。</summary>
        private sealed class GrowthTestEntity : Entity
        {
            /// <summary>从正式 Yefa 实例注册养成链路全部依赖。</summary>
            public GrowthTestEntity(GameObject gameObject)
            {
                AddComp(new GameObjectComponent(GameObjectSpawnSpec.SceneBound<PlayerBinder>(gameObject)));
                AddComp<EffectComponent>();
                AddComp<PropertyComponent>();
                AddComp<CharaLevelComponent>();
                AddComp<EquipmentComponent>();
                AddComp<WeaponComponent>();
                AddComp<AttackComponent>();
                AddComp<SpecialAttackComponent>();
                AddComp<SkillComponent>();
                AddComp<UltimateComponent>();
                AddComp<CoreTalentComponent>();
                AddLogic<GameObjectLogic>();
                AddLogic<EffectLogic>();
                AddLogic<CharaLevelLogic>();
                AddLogic<EquipmentLogic>();
                AddLogic<WeaponLogic>();
                AddLogic<TalentLogic>();
            }
        }
    }
}
