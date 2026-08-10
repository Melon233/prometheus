using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Ai;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>使用正式史莱姆预制体与动画库验证待机抢占恢复、语义解析以及由动画会话驱动的受击状态生命周期。</summary>
    public sealed class SlimeAnimationRegressionTests
    {
        private const string SlimePrefabPath = "Assets/BundleResources/Enemy/Slime.prefab";
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
