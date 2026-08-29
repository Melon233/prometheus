using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Xuan.Prometheus.Ai;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>使用正式史莱姆预制体与动画库验证待机抢占恢复、语义解析以及由动画会话驱动的受击状态生命周期。</summary>
    public sealed class SlimeAnimationRegressionTests
    {
        private const string SlimePrefabPath = "Assets/BundleResources/Enemy/Slime.prefab";
        private const string YefaPrefabPath = "Assets/BundleResources/Character/Yefa.prefab";
        private const string YefaAnimationLibraryPath = "Assets/BundleResources/Config/Animation/YefaAnimationLibrary.asset";
        private const string HudPanelPrefabPath = "Assets/BundleResources/UI/Hud/Prefabs/HudPanel.prefab";
        private const string SlimeHitRecoveryReferencePath = "Assets/Art/火环spine合集1/Q版小人/敌人/Enemy/slime_dark_l/Models/ReferenceAssets/leg_hitted2idle.asset";
        private AssetKit assetKit;
        private GameplayKit gameplayKit;
        private EntitySystem entitySystem;
        private AnimationTestEntity animationEntity;
        private GameObject slimeInstance;
        private AnimationLibrary runtimeLibrary;
        private SpineComponent spineComponent;
        private MotionComponent motionComponent;
        private PropertyComponent propertyComponent;
        private EventComponent eventComponent;

        /// <summary>为每个测试实例化正式史莱姆资源，并构造包含真实 EnemyAiLogic 的最小 Entity 环境。</summary>
        [SetUp]
        public void SetUp()
        {
            GameObject slimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlimePrefabPath);
            Assert.That(slimePrefab, Is.Not.Null, $"无法加载正式史莱姆预制体：{SlimePrefabPath}");
            slimeInstance = Object.Instantiate(slimePrefab);
            slimeInstance.name = "SlimeAnimationRegressionInstance";
            spineComponent = slimeInstance.GetComponent<SpineComponent>();
            Assert.That(spineComponent, Is.Not.Null, "史莱姆预制体必须包含 SpineComponent。");
            Assert.That(spineComponent.animationLib, Is.Not.Null, "史莱姆 SpineComponent 必须配置 AnimationLibrary。");
            runtimeLibrary = Object.Instantiate(spineComponent.animationLib);
            runtimeLibrary.InvalidateSemanticIndex();
            spineComponent.animationLib = runtimeLibrary;
            spineComponent.spineAnimator = slimeInstance.GetComponent<Spine.Unity.SkeletonAnimation>();
            Assert.That(spineComponent.spineAnimator, Is.Not.Null, "史莱姆预制体必须包含 SkeletonAnimation。");
            spineComponent.spineAnimator.Initialize(true);
            spineComponent.spineAnimator.AnimationState.Data.DefaultMix = SpineComponent.TransitionDuration;
            motionComponent = slimeInstance.GetComponent<MotionComponent>();
            propertyComponent = slimeInstance.GetComponent<PropertyComponent>();
            Assert.That(motionComponent, Is.Not.Null, "史莱姆预制体必须包含 MotionComponent。");
            Assert.That(motionComponent.cc, Is.SameAs(slimeInstance.GetComponent<CharacterController>()), "史莱姆 MotionComponent 必须引用同对象的 CharacterController。");
            Assert.That(propertyComponent, Is.Not.Null, "史莱姆预制体必须包含 PropertyComponent。");
            propertyComponent.RefreshBaseValues();
            assetKit = new AssetKit();
            gameplayKit = new GameplayKit(assetKit);
            entitySystem = gameplayKit.GetSystem<EntitySystem>();
            animationEntity = new AnimationTestEntity(slimeInstance, spineComponent, motionComponent, propertyComponent);
            entitySystem.AddEntity(animationEntity);
            animationEntity.AfterNew();
            Assert.That(animationEntity.TryGetComp(out eventComponent), Is.True, "最小测试 Entity 必须包含 EventComponent。");
        }

        /// <summary>按真实生命周期释放 Entity、动画库克隆与资源 Kit，避免 Unity 对象跨测试泄漏。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
            entitySystem = null;
            assetKit?.Dispose();
            assetKit = null;
            if (runtimeLibrary != null) Object.DestroyImmediate(runtimeLibrary);
            runtimeLibrary = null;
            if (slimeInstance != null) Object.DestroyImmediate(slimeInstance);
            animationEntity = null;
            slimeInstance = null;
            spineComponent = null;
            motionComponent = null;
            propertyComponent = null;
            eventComponent = null;
        }

        /// <summary>验证史莱姆停止移动时释放 Locomotion 所有权，使低优先级 Idle 可以立即接管主轨。</summary>
        [Test]
        public void StopMovement_ReleasesLocomotionAndAllowsIdleSemantic()
        {
            AnimationPlayback movementPlayback = spineComponent.TryPlay(AnimationSemantic.Run, AnimationOwner.GroundMove, AnimationPriority.Locomotion, true);
            Assert.That(movementPlayback, Is.Not.Null, "史莱姆动画库必须能够解析 Run 语义。");
            Assert.That(spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true), Is.Null, "移动所有权存在时，低优先级 Idle 必须被拒绝。");
            EnemyAiLogic enemyAiLogic = new EnemyAiLogic();
            FieldInfo spineField = typeof(EnemyAiLogic).GetField("spineComponent", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo motionField = typeof(EnemyAiLogic).GetField("motionComponent", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(spineField, Is.Not.Null, "EnemyAiLogic 必须保留 SpineComponent 运行时依赖。");
            Assert.That(motionField, Is.Not.Null, "EnemyAiLogic 必须通过 MotionComponent 管理水平速度。");
            spineField.SetValue(enemyAiLogic, spineComponent);
            motionField.SetValue(enemyAiLogic, motionComponent);
            motionComponent.curVelo = new Vector3(3f, -4f, 2f);
            enemyAiLogic.StopMovement();
            Assert.That(spineComponent.CurrentPlayback, Is.Null, "EnemyAiLogic.StopMovement 必须停止 GroundMove 会话。");
            Assert.That(motionComponent.curVelo, Is.EqualTo(new Vector3(0f, -4f, 0f)), "停止 AI 移动只能清除水平速度，不能吞掉重力速度。");
            AnimationPlayback idlePlayback = spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true);
            Assert.That(idlePlayback, Is.Not.Null, "停止移动后，史莱姆 Idle 语义必须成功接管主轨。");
            Assert.That(idlePlayback.Semantic, Is.EqualTo(AnimationSemantic.Idle));
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.Idle, out AnimationLine idleLine), Is.True);
            Assert.That(spineComponent.GetCurrentAnimation(), Is.EqualTo(idleLine.AnimationReferenceAsset.Animation.Name));
        }

        /// <summary>验证角色和史莱姆能够通过同一组公共枚举解析各自资源，并确保全部正式 AnimationLine 已完成语义迁移。</summary>
        [Test]
        public void CharacterAndSlimeLibraries_ResolveSharedSemanticsAndAllLinesAreMigrated()
        {
            AnimationLibrary yefaLibrary = AssetDatabase.LoadAssetAtPath<AnimationLibrary>(YefaAnimationLibraryPath);
            Assert.That(yefaLibrary, Is.Not.Null, $"无法加载正式角色动画库：{YefaAnimationLibraryPath}");
            AnimationSemantic[] sharedSemantics = { AnimationSemantic.Idle, AnimationSemantic.Run, AnimationSemantic.Attack1, AnimationSemantic.Hit, AnimationSemantic.HitRecovery, AnimationSemantic.Death };
            for (int index = 0; index < sharedSemantics.Length; index++)
            {
                AnimationSemantic semantic = sharedSemantics[index];
                Assert.That(runtimeLibrary.TryGetLine(semantic, out AnimationLine slimeLine), Is.True, $"史莱姆动画库缺少语义：{semantic}");
                Assert.That(yefaLibrary.TryGetLine(semantic, out AnimationLine yefaLine), Is.True, $"角色动画库缺少语义：{semantic}");
                Assert.That(slimeLine, Is.Not.SameAs(yefaLine), $"角色和史莱姆的 {semantic} 应解析为各自 AnimationLine。");
            }
            string[] lineGuids = AssetDatabase.FindAssets("t:AnimationLine");
            Assert.That(lineGuids.Length, Is.GreaterThan(0), "项目必须包含正式 AnimationLine 资源。");
            for (int index = 0; index < lineGuids.Length; index++)
            {
                string linePath = AssetDatabase.GUIDToAssetPath(lineGuids[index]);
                AnimationLine line = AssetDatabase.LoadAssetAtPath<AnimationLine>(linePath);
                Assert.That(line, Is.Not.Null, $"无法加载 AnimationLine：{linePath}");
                Assert.That(line.Semantic, Is.Not.EqualTo(AnimationSemantic.None), $"AnimationLine 尚未配置语义：{linePath}");
            }
        }

        /// <summary>验证全部实际攻击 AnimationLine 独立配置一对强类型碰撞盒命令，且 AnimationLibrary 不再暴露共享事件名。</summary>
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk1.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk1_move.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk2.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk2_move.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk3.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk4.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk4_move.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk_branch.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/heavy.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/xskill.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/skill_start.asset")]
        public void CombatAnimationLine_ConfiguresIndependentTypedHitboxWindow(string assetPath)
        {
            Assert.That(typeof(AnimationLibrary).GetField("hitStart", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null, "AnimationLibrary 不得继续保存全角色共享的碰撞盒开启事件名。");
            Assert.That(typeof(AnimationLibrary).GetField("hitEnd", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), Is.Null, "AnimationLibrary 不得继续保存全角色共享的碰撞盒关闭事件名。");
            AnimationLine line = AssetDatabase.LoadAssetAtPath<AnimationLine>(assetPath);
            Assert.That(line, Is.Not.Null, $"无法加载正式战斗 AnimationLine：{assetPath}");
            AnimationLineEvent enableCommand = null;
            AnimationLineEvent disableCommand = null;
            int enableCommandCount = 0;
            int disableCommandCount = 0;
            for (int index = 0; index < line.Events.Count; index++)
            {
                AnimationLineEvent marker = line.Events[index];
                if (marker.Command == AnimationLineEventCommand.EnableHitbox)
                {
                    enableCommandCount++;
                    enableCommand = marker;
                }
                else if (marker.Command == AnimationLineEventCommand.DisableHitbox)
                {
                    disableCommandCount++;
                    disableCommand = marker;
                }
            }
            Assert.That(enableCommandCount, Is.EqualTo(1), $"{line.name} 必须且只能配置一个 EnableHitbox 命令。");
            Assert.That(disableCommandCount, Is.EqualTo(1), $"{line.name} 必须且只能配置一个 DisableHitbox 命令。");
            Assert.That(enableCommand.Time, Is.LessThan(disableCommand.Time), $"{line.name} 的碰撞盒开启时间必须早于关闭时间。");
            Assert.That(disableCommand.Time, Is.LessThanOrEqualTo(line.Duration), $"{line.name} 的碰撞盒关闭命令不得超出当前动画长度。");
        }

        /// <summary>验证 Yefa 每段普通攻击只在组件保存碰撞体与 ID，并从统一 TalentConfig 读取独立倍率和偏移。</summary>
        [Test]
        public void YefaNormalAttackStages_UseIndependentHitboxesAndConfigurableDamageFormula()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            AttackComponent attackComponent = yefaPrefab.GetComponent<AttackComponent>();
            SpineComponent yefaSpine = yefaPrefab.GetComponent<SpineComponent>();
            Assert.That(attackComponent, Is.Not.Null, "Yefa 预制体必须包含 AttackComponent。");
            Assert.That(yefaSpine, Is.Not.Null);
            Assert.That(attackComponent.TalentConfig, Is.Not.Null, "Yefa AttackComponent 必须引用 TalentConfig。");
            Assert.That(yefaPrefab.GetComponent<SpecialAttackComponent>().TalentConfig, Is.SameAs(attackComponent.TalentConfig));
            Assert.That(yefaPrefab.GetComponent<SkillComponent>().TalentConfig, Is.SameAs(attackComponent.TalentConfig));
            Assert.That(yefaPrefab.GetComponent<UltimateComponent>().TalentConfig, Is.SameAs(attackComponent.TalentConfig));
            Assert.That(attackComponent.ConfiguredHitCount, Is.EqualTo(yefaSpine.animationLib.atkExecutor.Count), "普通攻击动画段和命中配置必须一一对应。");
            Assert.That(attackComponent.TalentConfig.NormalAttack.StageCount, Is.EqualTo(attackComponent.ConfiguredHitCount), "TalentConfig 普攻数值段必须与碰撞绑定段一一对应。");
            SerializedObject prefabAttack = new SerializedObject(attackComponent);
            SerializedProperty firstBinding = prefabAttack.FindProperty("attackHits").GetArrayElementAtIndex(0);
            Assert.That(firstBinding.FindPropertyRelative("damageMultiplier"), Is.Null, "AttackComponent 不得继续暴露伤害倍率。");
            Assert.That(firstBinding.FindPropertyRelative("damageOffset"), Is.Null, "AttackComponent 不得继续暴露伤害偏移。");
            Assert.That(firstBinding.FindPropertyRelative("additionalTags"), Is.Null, "AttackComponent 不得继续暴露数值关联标签。");
            HashSet<ColliderProxy> uniqueHitboxes = new HashSet<ColliderProxy>();
            for (int stageIndex = 0; stageIndex < attackComponent.ConfiguredHitCount; stageIndex++)
            {
                Assert.That(attackComponent.TryGetHitSelection(stageIndex, out NormalAttackHitSelection selection), Is.True, $"第 {stageIndex + 1} 段缺少有效命中配置。");
                Assert.That(attackComponent.TalentConfig.NormalAttack.TryGetStage(stageIndex, out NormalAttackTalentStage configuredStage), Is.True);
                Assert.That(uniqueHitboxes.Add(selection.ColliderProxy), Is.True, $"第 {stageIndex + 1} 段必须使用独立 ColliderProxy。");
                Assert.That(selection.ColliderProxy.GetComponent<Collider>(), Is.Not.Null);
                Assert.That(selection.DamageMultiplier, Is.GreaterThan(0f), $"第 {stageIndex + 1} 段普通攻击必须配置正数伤害倍率，否则碰撞成功也会表现为无法命中。");
                Assert.That(selection.DamageMultiplier, Is.EqualTo(configuredStage.DamageMultiplier).Within(0.0001f), "每段命中倍率必须读取 TalentConfig 当前资产值。");
                Assert.That(selection.DamageOffset, Is.EqualTo(configuredStage.DamageOffset).Within(0.0001f), "每段命中偏移必须读取 TalentConfig 当前资产值。");
                Assert.That(selection.AbilityId, Is.EqualTo($"Player.NormalAttack.{stageIndex + 1}"));
            }
            Assert.That(attackComponent.TalentConfig.NormalAttack.TryGetStage(0, out NormalAttackTalentStage configuredFirstStage), Is.True);
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            TalentConfig runtimeTalentConfig = Object.Instantiate(attackComponent.TalentConfig);
            try
            {
                AttackComponent runtimeAttack = yefaInstance.GetComponent<AttackComponent>();
                SerializedObject serializedAttack = new SerializedObject(runtimeAttack);
                serializedAttack.FindProperty("talentConfig").objectReferenceValue = runtimeTalentConfig;
                serializedAttack.ApplyModifiedPropertiesWithoutUndo();
                SerializedObject serializedTalent = new SerializedObject(runtimeTalentConfig);
                SerializedProperty secondStage = serializedTalent.FindProperty("normalAttack").FindPropertyRelative("stages").GetArrayElementAtIndex(1);
                secondStage.FindPropertyRelative("damageMultiplier").floatValue = 1.75f;
                secondStage.FindPropertyRelative("damageOffset").floatValue = 2f;
                serializedTalent.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(runtimeAttack.TryGetHitSelection(1, out NormalAttackHitSelection modifiedSelection), Is.True);
                Assert.That(modifiedSelection.DamageMultiplier, Is.EqualTo(1.75f).Within(0.0001f), "普通攻击 Logic 必须读取当前段独立配置的伤害倍率。");
                Assert.That(modifiedSelection.DamageOffset, Is.EqualTo(2f).Within(0.0001f), "普通攻击 Logic 必须读取当前段独立配置的伤害偏移。");
                PlayerCombatHitContext hitContext = new PlayerCombatHitContext(modifiedSelection.ColliderProxy, modifiedSelection.DamageMultiplier, modifiedSelection.DamageOffset, EffectTag.Attack | EffectTag.NormalAttack, modifiedSelection.AbilityId, DamageActionType.NormalAttack);
                Assert.That(hitContext.CalculateRequestedDamage(20f), Is.EqualTo(37f).Within(0.0001f), "第二段配置必须按照二十乘 1.75 再加二计算为三十七点请求伤害。");
                Assert.That(runtimeAttack.TryGetHitSelection(0, out NormalAttackHitSelection unchangedSelection), Is.True);
                Assert.That(unchangedSelection.DamageMultiplier, Is.EqualTo(configuredFirstStage.DamageMultiplier).Within(0.0001f), "修改单段倍率不得影响其他连段。");
                Assert.That(unchangedSelection.DamageOffset, Is.EqualTo(configuredFirstStage.DamageOffset).Within(0.0001f), "修改单段偏移不得影响其他连段。");
            }
            finally
            {
                Object.DestroyImmediate(runtimeTalentConfig);
                Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证 AnimationLine 的 DisableHitbox 命令会关闭全部普攻碰撞盒，切入第二段时也会先清除上一段泄漏再只开启当前段。</summary>
        [Test]
        public void NormalAttackHitWindow_LineCommandsAndStageSwitchCloseEveryBoundHitbox()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            int entityId = 0;
            try
            {
                SpineComponent yefaSpine = yefaInstance.GetComponent<SpineComponent>();
                AttackComponent yefaAttack = yefaInstance.GetComponent<AttackComponent>();
                PropertyComponent yefaProperty = yefaInstance.GetComponent<PropertyComponent>();
                Assert.That(yefaSpine, Is.Not.Null);
                Assert.That(yefaAttack, Is.Not.Null);
                Assert.That(yefaProperty, Is.Not.Null);
                yefaSpine.spineAnimator = yefaInstance.GetComponent<Spine.Unity.SkeletonAnimation>();
                yefaSpine.spineAnimator.Initialize(true);
                yefaSpine.spineAnimator.AnimationState.Data.DefaultMix = SpineComponent.TransitionDuration;
                yefaProperty.RefreshBaseValues();
                Assert.That(yefaAttack.TryGetHitSelection(0, out NormalAttackHitSelection firstHit), Is.True);
                Assert.That(yefaAttack.TryGetHitSelection(1, out NormalAttackHitSelection secondHit), Is.True);
                CombatHitboxTestLogic combatLogic = new CombatHitboxTestLogic(firstHit.ColliderProxy, secondHit.ColliderProxy);
                CombatHitboxTestEntity combatEntity = new CombatHitboxTestEntity(yefaInstance, combatLogic);
                entityId = entitySystem.AddEntity(combatEntity);
                combatEntity.AfterNew();
                AnimationPlayback firstPlayback = yefaSpine.TryPlay(AnimationSemantic.Attack1, AnimationOwner.NormalAttack, AnimationPriority.Attack, false, 1f, true);
                Assert.That(combatLogic.BeginForTests(firstPlayback, firstHit, true, YefaVfx.Atk1), Is.True);
                AdvanceAnimation(yefaSpine, 0.1f);
                Assert.That(firstHit.ColliderProxy.cod.enabled, Is.True, "第一段 EnableHitbox 命令后必须开启第一段碰撞盒。");
                Assert.That(secondHit.ColliderProxy.cod.enabled, Is.False);
                VfxComponent yefaVfx = yefaInstance.GetComponent<VfxComponent>();
                Assert.That(yefaVfx.vfxSlots[(int)YefaVfx.Atk1].activeSelf, Is.True, "第一段 EnableHitbox 命令后必须启动第一段动作特效。");
                AdvanceAnimation(yefaSpine, 0.2f);
                Assert.That(firstHit.ColliderProxy.cod.enabled, Is.False, "第一段 DisableHitbox 命令后必须关闭第一段碰撞盒。");
                Assert.That(secondHit.ColliderProxy.cod.enabled, Is.False);
                firstHit.ColliderProxy.cod.enabled = true;
                AnimationPlayback secondPlayback = yefaSpine.TryPlay(AnimationSemantic.Attack2, AnimationOwner.NormalAttack, AnimationPriority.Attack, false, 1f, true);
                Assert.That(yefaVfx.vfxSlots[(int)YefaVfx.Atk1].activeSelf, Is.False, "切段打断旧动作时必须立即停止旧段动作特效。");
                Assert.That(combatLogic.BeginForTests(secondPlayback, secondHit), Is.True);
                Assert.That(firstHit.ColliderProxy.cod.enabled, Is.False, "建立第二段动作上下文时必须立即清除上一段遗留碰撞盒。");
                AdvanceAnimation(yefaSpine, 0.2f);
                Assert.That(firstHit.ColliderProxy.cod.enabled, Is.False);
                Assert.That(secondHit.ColliderProxy.cod.enabled, Is.True, "第二段 EnableHitbox 命令后必须只开启第二段碰撞盒。");
                AdvanceAnimation(yefaSpine, 0.2f);
                Assert.That(firstHit.ColliderProxy.cod.enabled, Is.False);
                Assert.That(secondHit.ColliderProxy.cod.enabled, Is.False, "第二段 DisableHitbox 命令后必须关闭第二段碰撞盒。");
                Assert.That(combatLogic.HitWindowClosedCount, Is.EqualTo(2));
            }
            finally
            {
                if (entityId > 0) entitySystem.RemoveEntity(entityId);
                else if (yefaInstance != null) Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证所有 TalentConfig 伤害倍率都启用百分比 Inspector，并保持百分比与运行时倍率的双向换算。</summary>
        [Test]
        public void TalentDamageMultipliers_UsePercentageInspector()
        {
            FieldInfo abilityMultiplier = typeof(TalentAbilityValues).GetField("damageMultiplier", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo normalAttackMultiplier = typeof(NormalAttackTalentStage).GetField("damageMultiplier", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(abilityMultiplier, Is.Not.Null);
            Assert.That(normalAttackMultiplier, Is.Not.Null);
            Assert.That(abilityMultiplier.GetCustomAttribute<PercentageAttribute>(), Is.Not.Null, "特殊攻击、技能和大招的倍率必须按百分比编辑。");
            Assert.That(normalAttackMultiplier.GetCustomAttribute<PercentageAttribute>(), Is.Not.Null, "普通攻击逐段倍率必须按百分比编辑。");
            System.Type drawerType = null;
            foreach (Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                drawerType = assembly.GetType("Xuan.Prometheus.Editor.PercentageAttributeDrawer");
                if (drawerType != null) break;
            }
            Assert.That(drawerType, Is.Not.Null, "必须存在 PercentageAttribute 对应的 Editor 绘制器。");
            MethodInfo toDisplayPercentage = drawerType.GetMethod("ToDisplayPercentage", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo toStoredMultiplier = drawerType.GetMethod("ToStoredMultiplier", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo calculateSuffixX = drawerType.GetMethod("CalculateSuffixX", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(toDisplayPercentage, Is.Not.Null);
            Assert.That(toStoredMultiplier, Is.Not.Null);
            Assert.That(calculateSuffixX, Is.Not.Null);
            Assert.That((float)toDisplayPercentage.Invoke(null, new object[] { 1.25f }), Is.EqualTo(125f).Within(0.0001f));
            Assert.That((float)toStoredMultiplier.Invoke(null, new object[] { 175f, 0f }), Is.EqualTo(1.75f).Within(0.0001f));
            Assert.That((float)toStoredMultiplier.Invoke(null, new object[] { -20f, 0f }), Is.Zero, "百分比输入不得绕过非负倍率约束。");
            Rect valueRect = new Rect(10f, 0f, 100f, EditorGUIUtility.singleLineHeight);
            float suffixX = (float)calculateSuffixX.Invoke(null, new object[] { valueRect, 125f, EditorStyles.numberField });
            Assert.That(suffixX, Is.GreaterThan(valueRect.x), "百分号必须位于数字起点右侧。");
            Assert.That(suffixX + 12f, Is.LessThanOrEqualTo(valueRect.xMax), "百分号必须完整保留在输入框内部。");
        }

        /// <summary>验证 PlayerEntity 注册小队成员组件与四个独立动作 Logic，而 TalentLogic 保持为单独的常驻天赋组合。</summary>
        [Test]
        public void PlayerEntity_ComposesIndependentCombatActionLogics()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            try
            {
                PlayerEntity playerEntity = new PlayerEntity(yefaInstance);
                Assert.That(playerEntity.TryGetComp(out TeamMemberComponent _), Is.True, "每个玩家 Entity 必须持有独立的小队槽位运行态。");
                Assert.That(playerEntity.TryGetLogic(out TalentLogic talentLogic), Is.True);
                Assert.That(talentLogic, Is.Not.InstanceOf<ITriggerHandler>(), "TalentLogic 不得再次接管具体攻击碰撞回调。");
                Assert.That(playerEntity.TryGetLogic(out NormalAttackLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out SpecialAttackLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out SkillLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out SkillCooldownLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out UltimateLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out UltimateCooldownLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out GravityLogic _), Is.True, "玩家必须使用闪避期间持续运行的统一重力逻辑。");
            }
            finally
            {
                Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证 EntitySystem 会按 Entity、组件和字段立即同步，并在真实变化时回调且支持句柄精确退订。</summary>
        [Test]
        public void EntitySystem_TracksPlayerHudFieldsAndHandleStopsCallbacks()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            int hudEntityId = 0;
            List<ListenHandle> handles = new List<ListenHandle>();
            try
            {
                PropertyComponent playerProperty = yefaInstance.GetComponent<PropertyComponent>();
                SkillComponent playerSkill = yefaInstance.GetComponent<SkillComponent>();
                UltimateComponent playerUltimate = yefaInstance.GetComponent<UltimateComponent>();
                Assert.That(playerProperty, Is.Not.Null);
                Assert.That(playerSkill, Is.Not.Null);
                Assert.That(playerUltimate, Is.Not.Null);
                playerProperty.RefreshBaseValues();
                playerProperty.OnRecoverHp(playerProperty.MaxHp * 0.75f);
                playerProperty.OnGainCoreEnergy(20f);
                playerProperty.OnGainUltEnergy(25f);
                playerSkill.InitializeRuntimeState();
                playerSkill.BeginCooldown();
                playerUltimate.InitializeRuntimeState();
                playerUltimate.BeginCooldown();
                HudSyncTestEntity hudEntity = new HudSyncTestEntity(yefaInstance, playerProperty, playerSkill, playerUltimate);
                hudEntityId = entitySystem.AddEntity(hudEntity);
                hudEntity.AfterNew();
                int hpCalls = 0;
                int coreEnergyCalls = 0;
                int ultEnergyCalls = 0;
                int skillCooldownCalls = 0;
                int cooldownCalls = 0;
                ListenHandle hpHandle = entitySystem.Listen<PropertyComponent>(hudEntity.EntityId, component => component.HpProperty, _ => hpCalls++);
                handles.Add(hpHandle);
                handles.Add(entitySystem.Listen<PropertyComponent>(hudEntity.EntityId, component => component.CoreEnergyProperty, _ => coreEnergyCalls++));
                handles.Add(entitySystem.Listen<PropertyComponent>(hudEntity.EntityId, component => component.UltEnergyProperty, _ => ultEnergyCalls++));
                handles.Add(entitySystem.Listen<SkillComponent>(hudEntity.EntityId, component => component.CooldownRemainingProperty, _ => skillCooldownCalls++));
                handles.Add(entitySystem.Listen<UltimateComponent>(hudEntity.EntityId, component => component.CooldownRemainingProperty, _ => cooldownCalls++));
                Assert.That(hpCalls, Is.EqualTo(1), "注册生命监听时必须立即同步当前值。");
                Assert.That(coreEnergyCalls, Is.EqualTo(1), "注册核心能量监听时必须立即同步当前值。");
                Assert.That(ultEnergyCalls, Is.EqualTo(1), "注册大招能量监听时必须立即同步当前值。");
                Assert.That(skillCooldownCalls, Is.EqualTo(1), "注册技能冷却监听时必须立即同步当前值。");
                Assert.That(cooldownCalls, Is.EqualTo(1), "注册大招冷却监听时必须立即同步当前值。");
                playerProperty.OnTakeDamage(1f);
                playerProperty.OnGainCoreEnergy(1f);
                playerProperty.OnGainUltEnergy(1f);
                playerSkill.AdvanceCooldown(0.1f);
                playerUltimate.AdvanceCooldown(0.1f);
                Assert.That(hpCalls, Is.EqualTo(2));
                Assert.That(coreEnergyCalls, Is.EqualTo(2));
                Assert.That(ultEnergyCalls, Is.EqualTo(2));
                Assert.That(skillCooldownCalls, Is.EqualTo(2));
                Assert.That(cooldownCalls, Is.EqualTo(2));
                hpHandle.Dispose();
                playerProperty.OnRecoverHp(1f);
                Assert.That(hpCalls, Is.EqualTo(2), "释放 Handle 后属性不得继续持有 UI 回调。");
            }
            finally
            {
                foreach (ListenHandle handle in handles) handle.Dispose();
                if (hudEntityId > 0) entitySystem.RemoveEntity(hudEntityId);
                else if (yefaInstance != null) Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证技能只有冷却完成时可再次释放，并且冷却按角色 TalentConfig 的独立配置推进到零。</summary>
        [Test]
        public void SkillState_BeginsConfiguredCooldownAndAdvancesToReady()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            try
            {
                SkillComponent skillComponent = yefaInstance.GetComponent<SkillComponent>();
                Assert.That(skillComponent, Is.Not.Null);
                skillComponent.InitializeRuntimeState();
                Assert.That(skillComponent.CooldownDuration, Is.EqualTo(5f).Within(0.0001f));
                Assert.That(skillComponent.IsCooldownReady, Is.True);
                skillComponent.BeginCooldown();
                Assert.That(skillComponent.IsCooldownReady, Is.False);
                Assert.That(skillComponent.CooldownRemaining, Is.EqualTo(5f).Within(0.0001f));
                Assert.That(skillComponent.AdvanceCooldown(2f), Is.True);
                Assert.That(skillComponent.CooldownRemaining, Is.EqualTo(3f).Within(0.0001f));
                Assert.That(skillComponent.AdvanceCooldown(3f), Is.True);
                Assert.That(skillComponent.IsCooldownReady, Is.True);
                Assert.That(skillComponent.AdvanceCooldown(1f), Is.False, "冷却已经归零时不得继续产生脏回调。");
            }
            finally
            {
                Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证大招只有满能量且冷却完成时可释放，成功提交后能量归零并按配置独立推进冷却。</summary>
        [Test]
        public void UltimateState_RequiresFullEnergyConsumesAllAndAdvancesCooldown()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            try
            {
                PropertyComponent playerProperty = yefaInstance.GetComponent<PropertyComponent>();
                UltimateComponent ultimateComponent = yefaInstance.GetComponent<UltimateComponent>();
                Assert.That(playerProperty, Is.Not.Null);
                Assert.That(ultimateComponent, Is.Not.Null);
                playerProperty.RefreshBaseValues();
                ultimateComponent.InitializeRuntimeState();
                Assert.That(ultimateComponent.CooldownDuration, Is.EqualTo(10f).Within(0.0001f));
                playerProperty.OnGainUltEnergy(playerProperty.UltEnergyLimit - 1f);
                Assert.That(ultimateComponent.CanRelease(playerProperty), Is.False);
                playerProperty.OnGainUltEnergy(1f);
                Assert.That(ultimateComponent.CanRelease(playerProperty), Is.True);
                float consumedEnergy = playerProperty.ConsumeAllUltEnergy();
                ultimateComponent.BeginCooldown();
                Assert.That(consumedEnergy, Is.EqualTo(playerProperty.UltEnergyLimit).Within(0.0001f));
                Assert.That(playerProperty.UltEnergy, Is.Zero);
                Assert.That(ultimateComponent.CanRelease(playerProperty), Is.False);
                Assert.That(ultimateComponent.AdvanceCooldown(4f), Is.True);
                Assert.That(ultimateComponent.CooldownRemaining, Is.EqualTo(6f).Within(0.0001f));
                Assert.That(ultimateComponent.AdvanceCooldown(6f), Is.True);
                Assert.That(ultimateComponent.IsCooldownReady, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证 UltMono 使用两种进度分别驱动 Fill Image，并把剩余冷却格式化为一位小数。</summary>
        [Test]
        public void UltMono_DisplaysEnergyCooldownAndOneDecimalTime()
        {
            GameObject root = new GameObject("UltMonoTest.Root");
            GameObject cooldownObject = new GameObject("UltMonoTest.Cooldown");
            GameObject energyObject = new GameObject("UltMonoTest.Energy");
            GameObject textObject = new GameObject("UltMonoTest.Text");
            try
            {
                UltMono ultMono = root.AddComponent<UltMono>();
                ultMono.cooldownImg = cooldownObject.AddComponent<Image>();
                ultMono.energyImg = energyObject.AddComponent<Image>();
                ultMono.cooldownTxt = textObject.AddComponent<TextMeshProUGUI>();
                ultMono.ApplyState(25f, 100f, 6.25f, 10f);
                Assert.That(ultMono.energyImg.fillAmount, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(ultMono.cooldownImg.fillAmount, Is.EqualTo(0.625f).Within(0.0001f));
                Assert.That(ultMono.cooldownTxt.text, Is.EqualTo("6.3"));
                ultMono.ApplyState(25f, 100f, 0f, 10f);
                Assert.That(ultMono.cooldownImg.fillAmount, Is.Zero);
                Assert.That(ultMono.cooldownTxt.text, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(textObject);
                Object.DestroyImmediate(energyObject);
                Object.DestroyImmediate(cooldownObject);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>验证通用 CdMono 按完整冷却计算遮罩比例，并把技能剩余冷却格式化为一位小数。</summary>
        [Test]
        public void CdMono_DisplaysSkillCooldownAndOneDecimalTime()
        {
            GameObject root = new GameObject("CdMonoTest.Root");
            GameObject cooldownObject = new GameObject("CdMonoTest.Cooldown");
            GameObject textObject = new GameObject("CdMonoTest.Text");
            try
            {
                CdMono cdMono = root.AddComponent<CdMono>();
                cdMono.cooldownImg = cooldownObject.AddComponent<Image>();
                cdMono.cooldownTxt = textObject.AddComponent<TextMeshProUGUI>();
                cdMono.ApplyState(3.25f, 5f);
                Assert.That(cdMono.cooldownImg.fillAmount, Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(cdMono.cooldownTxt.text, Is.EqualTo("3.3"));
                cdMono.ApplyState(0f, 5f);
                Assert.That(cdMono.cooldownImg.fillAmount, Is.Zero);
                Assert.That(cdMono.cooldownTxt.text, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(textObject);
                Object.DestroyImmediate(cooldownObject);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>验证 HUD Binder 公开 Skill 的 CdMono，并且冷却遮罩和文本引用完整可用。</summary>
        [Test]
        public void HudSkill_HasCompleteCooldownBinding()
        {
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPanelPrefabPath);
            Assert.That(hudPrefab, Is.Not.Null, $"无法加载 HUD 预制体：{HudPanelPrefabPath}");
            UIComponentBinder binder = hudPrefab.GetComponent<UIComponentBinder>();
            Assert.That(binder, Is.Not.Null);
            UIComponentBinding skillBinding = null;
            foreach (UIComponentBinding binding in binder.Bindings)
            {
                if (binding.Name == "Skill") skillBinding = binding;
            }
            Assert.That(skillBinding, Is.Not.Null, "HUD Binder 必须公开 Skill 字段。");
            CdMono skillMono = skillBinding.Component as CdMono;
            Assert.That(skillMono, Is.Not.Null, "HUD Skill 字段必须绑定 CdMono。");
            Assert.That(skillMono.cooldownImg, Is.Not.Null);
            Assert.That(skillMono.cooldownTxt, Is.Not.Null);
        }

        /// <summary>验证 HUD BuffList 已完成 Binder、ScrollRect、内容节点和可复用 Buff 项模板配置。</summary>
        [Test]
        public void HudBuffList_HasCompleteLoopListViewRuntimeConfiguration()
        {
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPanelPrefabPath);
            Assert.That(hudPrefab, Is.Not.Null, $"无法加载 HUD 预制体：{HudPanelPrefabPath}");
            UIComponentBinder binder = hudPrefab.GetComponent<UIComponentBinder>();
            Assert.That(binder, Is.Not.Null);
            UIComponentBinding buffListBinding = null;
            foreach (UIComponentBinding binding in binder.Bindings)
            {
                if (binding.Name == "BuffList") buffListBinding = binding;
            }
            Assert.That(buffListBinding, Is.Not.Null, "HUD Binder 必须公开 BuffList 字段。");
            UnityEngine.Component buffList = buffListBinding.Component;
            Assert.That(buffList.GetType().FullName, Is.EqualTo("SuperScrollView.LoopListView2"));
            ScrollRect scrollRect = buffList.GetComponent<ScrollRect>();
            Assert.That(scrollRect, Is.Not.Null);
            Assert.That(scrollRect.content, Is.Not.Null);
            Assert.That(scrollRect.viewport, Is.Not.Null);
            Transform itemTemplate = scrollRect.content.Find("Buff");
            Assert.That(itemTemplate, Is.Not.Null);
            Assert.That(itemTemplate.GetComponent("LoopListViewItem2"), Is.Not.Null);
            BuffMono buffMono = itemTemplate.GetComponent<BuffMono>();
            Assert.That(buffMono, Is.Not.Null);
            Assert.That(buffMono.icon, Is.Not.Null);
            Assert.That(buffMono.stackCnt, Is.Not.Null);
            Assert.That(buffMono.durationImg, Is.Not.Null);
        }

        /// <summary>验证史莱姆空中运动把 AI 水平速度与属性重力合成为一次 CharacterController 位移。</summary>
        [Test]
        public void GravityLogic_AppliesConfiguredGravityAndPreservesEnemyHorizontalMotion()
        {
            motionComponent.cc.enabled = false;
            slimeInstance.transform.position = new Vector3(0f, 20f, 0f);
            motionComponent.cc.enabled = true;
            motionComponent.curVelo = new Vector3(2f, 0f, 0f);
            Vector3 startPosition = slimeInstance.transform.position;
            animationEntity.OnUpdate(0.5f);
            Assert.That(propertyComponent.Gravity, Is.EqualTo(9.8f).Within(0.0001f), "史莱姆属性必须提供非零重力。");
            Assert.That(motionComponent.curVelo.y, Is.EqualTo(-4.9f).Within(0.0001f), "空中逻辑必须按照 Gravity × deltaTime 累计竖直速度。");
            Assert.That(slimeInstance.transform.position.x, Is.GreaterThan(startPosition.x), "空中受重力时仍必须保留 AI 水平移动能力。");
            Assert.That(slimeInstance.transform.position.y, Is.LessThan(startPosition.y), "MotionLogic 必须把重力速度提交给 CharacterController。");
        }

        /// <summary>验证连续 StaggeredEvent 会重启受击表现，同时保持唯一受击状态直到最终动画自然完成。</summary>
        [Test]
        public void RepeatedStaggeredEvents_KeepAttackedStateUntilFinalAnimationCompletes()
        {
            int stateChangeCount = 0;
            eventComponent.AddListener<ControlStateChangedEvent>(_ => stateChangeCount++);
            eventComponent.Invoke(new StaggeredEvent(10f, 2f, 1f));
            AnimationPlayback firstPlayback = spineComponent.CurrentPlayback;
            Assert.That(firstPlayback, Is.Not.Null, "首次达标伤害必须创建受击播放会话。");
            Assert.That(firstPlayback.Semantic, Is.EqualTo(AnimationSemantic.HitRecovery), "双段受击会话应以最终恢复段作为会话语义。");
            Assert.That(propertyComponent.IsAttacked, Is.True, "受击动画播放期间必须持有 Attacked 状态。");
            Assert.That(propertyComponent.CanAct, Is.False);
            Assert.That(stateChangeCount, Is.EqualTo(1));
            eventComponent.Invoke(new StaggeredEvent(10f, 2f, 1f));
            AnimationPlayback secondPlayback = spineComponent.CurrentPlayback;
            Assert.That(secondPlayback, Is.Not.Null, "连续达标伤害必须创建新的受击播放会话。");
            Assert.That(secondPlayback, Is.Not.SameAs(firstPlayback), "聚合状态未变化时，新的 StaggeredEvent 仍必须重启受击动画。");
            Assert.That(propertyComponent.IsAttacked, Is.True, "替换受击会话时不得瞬时退出 Attacked 状态。");
            Assert.That(stateChangeCount, Is.EqualTo(1), "连续受击替换动画时不得重复添加或移除 Attacked 状态。");
            Assert.That(runtimeLibrary.attackedExecutor.HasRecoveryAnimation, Is.True, "史莱姆 AttackedExecutor 必须自动识别恢复动画。");
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.Hit, out AnimationLine hitLine), Is.True);
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.HitRecovery, out AnimationLine recoveryLine), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(recoveryLine.AnimationReferenceAsset), Is.EqualTo(SlimeHitRecoveryReferencePath), "恢复语义必须使用史莱姆专属 leg_hitted2idle 资源。");
            AdvanceAnimation(hitLine.Duration + recoveryLine.Duration + SpineComponent.TransitionDuration + 1f);
            Assert.That(spineComponent.CurrentPlayback, Is.Null, "最终受击动画自然完成后必须释放主轨会话。");
            Assert.That(propertyComponent.IsAttacked, Is.False, "最终受击动画完成后必须立即退出 Attacked 状态。");
            Assert.That(propertyComponent.CanAct, Is.True);
            Assert.That(stateChangeCount, Is.EqualTo(2));
        }

        /// <summary>验证受击动画期间 Attacked 状态会停用 EnemyAiLogic，同时保留重力和 Motion 基础设施。</summary>
        [Test]
        public void AttackedState_BlocksEnemyAiButKeepsGravityUntilAnimationCompletes()
        {
            animationEntity.OnUpdate(0f);
            Assert.That(animationEntity.TryGetLogic(out EnemyAiLogic enemyAiLogic), Is.True);
            Assert.That(enemyAiLogic.Enable, Is.True, "测试开始前 EnemyAiLogic 必须已经启用。");
            motionComponent.cc.enabled = false;
            slimeInstance.transform.position = new Vector3(0f, 20f, 0f);
            motionComponent.cc.enabled = true;
            motionComponent.curVelo = new Vector3(3f, -2f, 4f);
            eventComponent.Invoke(new StaggeredEvent(10f, 2f, 1f));
            animationEntity.OnUpdate(0.02f);
            Assert.That(spineComponent.CurrentPlayback, Is.Not.Null, "Attacked 状态期间必须播放受击动画。");
            Assert.That(spineComponent.CurrentPlayback.Semantic, Is.EqualTo(AnimationSemantic.HitRecovery), "低优先级 Idle 不能打断双段受击会话。");
            Assert.That(propertyComponent.IsAttacked, Is.True);
            Assert.That(enemyAiLogic.Enable, Is.False, "Attacked 状态必须通过行动能力门禁停用 EnemyAiLogic。");
            Assert.That(enemyAiLogic.Brain.IsRunning, Is.False, "EnemyAiLogic 停用时必须挂起 Brain。");
            Assert.That(motionComponent.curVelo.x, Is.Zero, "受击时必须清除水平 X 速度。");
            Assert.That(motionComponent.curVelo.z, Is.Zero, "受击时必须清除水平 Z 速度。");
            Assert.That(motionComponent.curVelo.y, Is.LessThan(-2f), "受击状态不能暂停竖直重力速度。");
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.Hit, out AnimationLine hitLine), Is.True);
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.HitRecovery, out AnimationLine recoveryLine), Is.True);
            AdvanceAnimation(hitLine.Duration + recoveryLine.Duration + SpineComponent.TransitionDuration + 1f);
            Assert.That(spineComponent.CurrentPlayback, Is.Null, "受击动画完成时必须释放 HitReaction 会话。");
            Assert.That(propertyComponent.IsAttacked, Is.False);
            animationEntity.OnUpdate(0.02f);
            Assert.That(enemyAiLogic.Enable, Is.True, "受击动画完成后 EnemyAiLogic 必须由行动能力门禁重新启用。");
            Assert.That(enemyAiLogic.Brain.IsRunning, Is.True);
        }

        /// <summary>验证受击动画被更高优先级会话打断时会立即释放 Attacked 状态，避免 Logic 门禁泄漏。</summary>
        [Test]
        public void HigherPriorityAnimation_InterruptsHitReactionAndReleasesAttackedState()
        {
            eventComponent.Invoke(new StaggeredEvent(10f, 2f, 1f));
            Assert.That(propertyComponent.IsAttacked, Is.True);
            AnimationPlayback deathPlayback = spineComponent.TryPlay(AnimationSemantic.Death, AnimationOwner.Death, AnimationPriority.Death, false, 1f, true);
            Assert.That(deathPlayback, Is.Not.Null);
            Assert.That(propertyComponent.IsAttacked, Is.False, "更高优先级动画打断受击会话时必须同步退出 Attacked 状态。");
            Assert.That(propertyComponent.CanAct, Is.True);
        }

        /// <summary>以接近运行时帧更新的固定步长推进 Spine 状态，使排队动画能够依次切换并发布完成事件。</summary>
        private void AdvanceAnimation(float duration)
        {
            AdvanceAnimation(spineComponent, duration);
        }

        /// <summary>推进指定 SpineComponent 的正式 AnimationState，供角色和史莱姆事件窗口测试复用。</summary>
        private static void AdvanceAnimation(SpineComponent targetSpineComponent, float duration)
        {
            const float step = 0.05f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float deltaTime = Mathf.Min(step, duration - elapsed);
                targetSpineComponent.spineAnimator.AnimationState.Update(deltaTime);
                targetSpineComponent.spineAnimator.AnimationState.Apply(targetSpineComponent.spineAnimator.Skeleton);
                elapsed += deltaTime;
            }
        }

        /// <summary>提供受击表现与空中运动所需的真实 EnemyAiLogic 和通用 AttackedLogic 组合，避免测试专用替代链路。</summary>
        private sealed class AnimationTestEntity : Entity
        {
            /// <summary>注册正式史莱姆 AI 所需组件以及重力、位移和通用受击 Logic。</summary>
            public AnimationTestEntity(GameObject bindGameObject, SpineComponent animationComponent, MotionComponent motion, PropertyComponent property)
            {
                bindGo = bindGameObject;
                AddComp(animationComponent);
                AddComp(motion);
                AddComp(property);
                AddComp(bindGameObject.GetComponent<AttackComponent>());
                AddComp(bindGameObject.GetComponent<VfxComponent>());
                AddComp(bindGameObject.GetComponent<EnemyAiComponent>());
                AddComp<EventComponent>();
                AddComp(bindGameObject.GetComponent<EffectComponent>());
                AddLogic<EnemyAiLogic>();
                AddLogic<GravityLogic>();
                AddLogic<MotionLogic>();
                AddLogic<AttackedLogic>();
            }
        }

        /// <summary>为 EntitySystem 监听测试提供只包含玩家属性、技能冷却与大招状态组件的最小 Entity。</summary>
        private sealed class HudSyncTestEntity : Entity
        {
            /// <summary>注册 HUD 监听寻址所需的场景对象、属性组件、技能组件和大招组件。</summary>
            public HudSyncTestEntity(GameObject bindGameObject, PropertyComponent property, SkillComponent skill, UltimateComponent ultimate)
            {
                bindGo = bindGameObject;
                AddComp(property);
                AddComp(skill);
                AddComp(ultimate);
            }
        }

        /// <summary>提供可直接建立攻击会话的测试 Logic，以验证 PlayerCombatActionLogic 的统一命中窗口生命周期。</summary>
        private sealed class CombatHitboxTestLogic : PlayerCombatActionLogic
        {
            private readonly ColliderProxy firstHitbox;
            private readonly ColliderProxy secondHitbox;

            /// <summary>创建只绑定前两段正式普通攻击碰撞盒的测试 Logic。</summary>
            public CombatHitboxTestLogic(ColliderProxy firstHitbox, ColliderProxy secondHitbox)
            {
                this.firstHitbox = firstHitbox;
                this.secondHitbox = secondHitbox;
            }

            /// <summary>测试 Logic 复用普通攻击动画所有权。</summary>
            protected override AnimationOwner ActionOwner => AnimationOwner.NormalAttack;

            /// <summary>记录正式 DisableHitbox 命令进入派生扩展入口的次数。</summary>
            public int HitWindowClosedCount { get; private set; }

            /// <summary>通过基类正式入口建立一次测试动作上下文。</summary>
            public bool BeginForTests(AnimationPlayback playback, NormalAttackHitSelection selection, bool hasVfx = false, YefaVfx vfx = default)
            {
                PlayerCombatHitContext hitContext = new PlayerCombatHitContext(selection.ColliderProxy, selection.DamageMultiplier, selection.DamageOffset, EffectTag.Attack | EffectTag.NormalAttack, selection.AbilityId, DamageActionType.NormalAttack);
                return BeginAction(playback, hitContext, hasVfx, vfx);
            }

            /// <summary>绑定两段碰撞盒，使基类负责其启停和回收。</summary>
            protected override void OnActionInitialized()
            {
                BindHitbox(firstHitbox);
                BindHitbox(secondHitbox);
            }

            /// <summary>累计 DisableHitbox 命令回调次数。</summary>
            protected override void OnHitWindowClosed()
            {
                HitWindowClosedCount++;
            }

            /// <summary>测试 Logic 不消费帧输入，动画事件由正式 Spine 状态推进。</summary>
            public override void OnUpdate(float dt)
            {
            }
        }

        /// <summary>为命中窗口测试注册 PlayerCombatActionLogic 所需的最小正式组件集合。</summary>
        private sealed class CombatHitboxTestEntity : Entity
        {
            /// <summary>绑定 Yefa 正式组件并注册测试动作 Logic。</summary>
            public CombatHitboxTestEntity(GameObject bindGameObject, CombatHitboxTestLogic combatLogic)
            {
                bindGo = bindGameObject;
                AddComp<InputComponent>();
                AddComp(bindGameObject.GetComponent<SpineComponent>());
                AddComp(bindGameObject.GetComponent<PropertyComponent>());
                AddComp(bindGameObject.GetComponent<EffectComponent>());
                AddComp(bindGameObject.GetComponent<VfxComponent>());
                AddLogic(combatLogic);
            }
        }
    }
}
