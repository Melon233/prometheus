using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
        private const string SlimeHitRecoveryReferencePath = "Assets/Art/火环spine合集1/Q版小人/敌人/Enemy/slime_dark_l/Models/ReferenceAssets/leg_hitted2idle.asset";
        private AssetKit assetKit;
        private GameplayKit gameplayKit;
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
            SerializedObject serializedLibrary = new SerializedObject(runtimeLibrary);
            serializedLibrary.FindProperty("attackedExecutor").FindPropertyRelative("attackedSfx").objectReferenceValue = null;
            serializedLibrary.ApplyModifiedPropertiesWithoutUndo();
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
            animationEntity = new AnimationTestEntity(slimeInstance, spineComponent, motionComponent, propertyComponent);
            gameplayKit.AddEntity(animationEntity);
            animationEntity.AfterNew();
            Assert.That(animationEntity.TryGetComp(out eventComponent), Is.True, "最小测试 Entity 必须包含 EventComponent。");
        }

        /// <summary>按真实生命周期释放 Entity、动画库克隆与资源 Kit，避免 Unity 对象跨测试泄漏。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
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

        /// <summary>验证 PlayerEntity 注册四个独立动作 Logic，而 TalentLogic 保持为单独的常驻天赋组合。</summary>
        [Test]
        public void PlayerEntity_ComposesIndependentCombatActionLogics()
        {
            GameObject yefaPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(yefaPrefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            GameObject yefaInstance = Object.Instantiate(yefaPrefab);
            try
            {
                PlayerEntity playerEntity = new PlayerEntity(yefaInstance);
                Assert.That(playerEntity.TryGetLogic(out TalentLogic talentLogic), Is.True);
                Assert.That(talentLogic, Is.Not.InstanceOf<ITriggerHandler>(), "TalentLogic 不得再次接管具体攻击碰撞回调。");
                Assert.That(playerEntity.TryGetLogic(out NormalAttackLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out SpecialAttackLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out SkillLogic _), Is.True);
                Assert.That(playerEntity.TryGetLogic(out UltimateLogic _), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(yefaInstance);
            }
        }

        /// <summary>验证史莱姆空中运动把 AI 水平速度与属性重力合成为一次 CharacterController 位移。</summary>
        [Test]
        public void EnemyAirMovement_AppliesConfiguredGravityAndPreservesHorizontalMotion()
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
            const float step = 0.05f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float deltaTime = Mathf.Min(step, duration - elapsed);
                spineComponent.spineAnimator.AnimationState.Update(deltaTime);
                spineComponent.spineAnimator.AnimationState.Apply(spineComponent.spineAnimator.Skeleton);
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
                AddLogic<EnemyAirMoveLogic>();
                AddLogic<MotionLogic>();
                AddLogic<AttackedLogic>();
            }
        }
    }
}
