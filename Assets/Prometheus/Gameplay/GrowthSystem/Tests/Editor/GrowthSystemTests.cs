using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Logic.Talent;

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
        /// <summary>保存测试独占的玩法世界。</summary>
        private GameplayKit gameplayKit;
        /// <summary>保存测试独占的 EntitySystem。</summary>
        private EntitySystem entitySystem;
        /// <summary>保存测试读取的正式 Effect 配置库。</summary>
        private EffectLibrary effectLibrary;
        /// <summary>保存测试独占的 EffectSystem。</summary>
        private EffectSystem effectSystem;
        /// <summary>保存正式 Yefa Prefab 实例。</summary>
        private GameObject yefaInstance;
        /// <summary>保存只组合养成链路依赖的测试 Entity。</summary>
        private GrowthTestEntity entity;

        /// <summary>创建单局 EffectRuntime、实例化正式 Yefa，并初始化四种养成 Logic。</summary>
        [SetUp]
        public void SetUp()
        {
            assetKit = new AssetKit();
            Core.Asset = assetKit;
            gameplayKit = new GameplayKit();
            entitySystem = gameplayKit.GetSystem<EntitySystem>();
            effectLibrary = AssetDatabase.LoadAssetAtPath<EffectLibrary>(EffectLibraryPath);
            Assert.That(effectLibrary, Is.Not.Null, $"无法加载正式效果库：{EffectLibraryPath}");
            effectSystem = new EffectSystem(effectLibrary);
            gameplayKit.AddSystem(effectSystem);
            effectSystem.AfterNew();
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
            entitySystem = null;
            effectSystem = null;
            assetKit?.Dispose();
            assetKit = null;
            effectLibrary = null;
            if (yefaInstance != null) Object.DestroyImmediate(yefaInstance);
            yefaInstance = null;
            entity = null;
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
            Assert.That(level.CurrentTotalExperience, Is.Zero);
            Assert.That(level.MaximumExperience, Is.EqualTo(8900f).Within(0.0001f));
            Assert.That(level.AddExperience(100f), Is.EqualTo(100f).Within(0.0001f));
            entity.OnUpdate(0f);
            Assert.That(level.CurrentLevel, Is.EqualTo(2));
            Assert.That(level.CurrentTotalExperience, Is.EqualTo(100f).Within(0.0001f));
            Assert.That(property.Atk, Is.EqualTo(initialAttack + 10f).Within(0.0001f), "Yefa 等级配置应让每级永久增加 10 点攻击力。");

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
            Assert.That(property.Atk, Is.EqualTo(initialAttack * 1.05f + 20f).Within(0.0001f), "角色等级 Offset 与装备满级的 Offset、Boost 应由两个永久 Effect 独立叠加。");

            Assert.That(weapon.CurrentLevel, Is.EqualTo(1));
            Assert.That(weapon.CurrentTotalExperience, Is.Zero);
            Assert.That(weapon.AddExperience(weapon.MaximumExperience), Is.EqualTo(weapon.MaximumExperience).Within(0.0001f));
            entity.OnUpdate(0f);
            Assert.That(weapon.CurrentLevel, Is.EqualTo(weapon.MaximumLevel));
            Assert.That(weapon.CurrentTotalExperience, Is.EqualTo(weapon.MaximumExperience).Within(0.0001f));
            Assert.That(weapon.Tiers.Count, Is.EqualTo(1));
            Assert.That(weapon.Tiers[0].CurrentOffset, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(weapon.Tiers[0].CurrentCoefficient, Is.EqualTo(0.05f).Within(0.0001f));
            Assert.That(property.Atk, Is.EqualTo(initialAttack * 1.1f + 30f).Within(0.0001f), "角色、装备和武器的永久 Effect 应同时汇总到现有属性公式。");

            AssertPermanentGrowthEffect("CharaLevel");
            AssertPermanentGrowthEffect("Equipment.Tiers");
            AssertPermanentGrowthEffect("Weapon.Tiers");
            AssertPermanentGrowthEffect("Talent");
            System.Collections.Generic.List<EffectInstance> visibleBuffs = new System.Collections.Generic.List<EffectInstance>();
            Assert.That(entity.TryGetComp(out EffectComponent effectComponent), Is.True);
            effectComponent.CopyActiveBuffs(visibleBuffs);
            Assert.That(visibleBuffs, Is.Empty, "内部成长永久 Effect 不应污染 HUD Buff 列表。");
        }

        /// <summary>验证非线性经验曲线决定角色、装备和武器的离散等级阈值，而不是继续使用旧经验系数公式。</summary>
        [Test]
        public void ExperienceCurves_MapCumulativeExperienceToDiscreteLevels()
        {
            GameObject curveObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath));
            GameplayKit curveGameplayKit = null;
            AssetKit curveAssetKit = null;
            EffectLibrary library = AssetDatabase.LoadAssetAtPath<EffectLibrary>(EffectLibraryPath);
            CharaLevelConfig levelConfig = null;
            EquipmentConfig equipmentConfig = null;
            WeaponConfig weaponConfig = null;
            try
            {
                PlayerBinder binder = curveObject.GetComponent<PlayerBinder>();
                Assert.That(binder, Is.Not.Null, "Yefa 必须在根节点持有 PlayerBinder。");
                levelConfig = Object.Instantiate(binder.CharaLevelConfig);
                equipmentConfig = Object.Instantiate(binder.EquipmentConfig);
                weaponConfig = Object.Instantiate(binder.WeaponConfig);
                SetSerializedGrowthCurve(levelConfig, 5, 100f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetSerializedGrowthCurve(equipmentConfig, 4, 100f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetSerializedGrowthCurve(weaponConfig, 5, 100f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
                SetBinderConfig(binder, "charaLevelConfig", levelConfig);
                SetBinderConfig(binder, "equipmentConfig", equipmentConfig);
                SetBinderConfig(binder, "weaponConfig", weaponConfig);
                curveAssetKit = new AssetKit();
                Core.Asset = curveAssetKit;
                curveGameplayKit = new GameplayKit();
                EffectSystem curveEffectSystem = new EffectSystem(library);
                curveGameplayKit.AddSystem(curveEffectSystem);
                curveEffectSystem.AfterNew();
                GrowthTestEntity curveEntity = new GrowthTestEntity(curveObject);
                curveGameplayKit.GetSystem<EntitySystem>().AddEntity(curveEntity);
                curveEntity.AfterNew();
                Assert.That(curveEntity.TryGetComp(out CharaLevelComponent level), Is.True);
                Assert.That(curveEntity.TryGetComp(out EquipmentComponent equipment), Is.True);
                Assert.That(curveEntity.TryGetComp(out WeaponComponent weapon), Is.True);
                Assert.That(level.AddExperience(50f), Is.EqualTo(50f).Within(0.0001f));
                Assert.That(level.CurrentLevel, Is.EqualTo(3), "EaseInOut 曲线在一半经验处应映射到一半等级进度。");
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
                if (levelConfig != null) Object.DestroyImmediate(levelConfig);
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
