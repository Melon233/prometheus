using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Asset;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>使用正式 Yefa 动画和运动组件验证地面闪避结束不会伪造落地边沿。</summary>
    public sealed class DodgeLandingRegressionTests
    {
        /// <summary>正式角色预制体路径，保证测试覆盖实际前后闪避与落地动画配置。</summary>
        private const string YefaPrefabPath = "Assets/BundleResources/Character/Yefa.prefab";

        /// <summary>保存测试独占的资源系统。</summary>
        private AssetKit assetKit;

        /// <summary>保存测试独占的玩法世界。</summary>
        private GameplayKit gameplayKit;

        /// <summary>保存当前测试的正式角色实例。</summary>
        private GameObject actor;

        /// <summary>保存用于建立稳定接地状态的临时地面。</summary>
        private GameObject floor;

        /// <summary>保存可逐帧写入闪避命令的输入组件。</summary>
        private InputComponent inputComponent;

        /// <summary>保存受测运动状态。</summary>
        private MotionComponent motionComponent;

        /// <summary>保存受测动画播放器。</summary>
        private SpineComponent spineComponent;

        /// <summary>保存把 Spine Root Motion 汇入 MotionComponent 的正式桥接组件。</summary>
        private CharacterRootMotionComponent rootMotionComponent;

        /// <summary>保存只组合闪避、接地和必要依赖逻辑的最小实体。</summary>
        private DodgeLandingTestEntity entity;

        /// <summary>实例化正式角色和物理地面，并建立进入测试前已经连续接地的稳定状态。</summary>
        [SetUp]
        public void SetUp()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(YefaPrefabPath);
            Assert.That(prefab, Is.Not.Null, $"无法加载正式角色预制体：{YefaPrefabPath}");
            actor = UnityEngine.Object.Instantiate(prefab);
            actor.name = "DodgeLandingRegressionTests.Yefa";
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "DodgeLandingRegressionTests.Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            actor.transform.position = new Vector3(0f, 0.2f, 0f);
            spineComponent = actor.GetEntityComponent<SpineComponent>();
            motionComponent = actor.GetEntityComponent<MotionComponent>();
            PropertyComponent propertyComponent = actor.GetEntityComponent<PropertyComponent>();
            Assert.That(spineComponent, Is.Not.Null);
            Assert.That(motionComponent, Is.Not.Null);
            Assert.That(propertyComponent, Is.Not.Null);
            propertyComponent.RefreshBaseValues();
            spineComponent.spineAnimator = actor.GetComponent<Spine.Unity.SkeletonAnimation>();
            Assert.That(spineComponent.spineAnimator, Is.Not.Null);
            spineComponent.spineAnimator.Initialize(true);
            rootMotionComponent = actor.GetComponent<CharacterRootMotionComponent>();
            Assert.That(rootMotionComponent, Is.Not.Null, "正式角色必须通过 CharacterRootMotionComponent 把动画位移交给统一运动出口。");
            typeof(CharacterRootMotionComponent).GetMethod("Start", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(rootMotionComponent, null);
            spineComponent.animationLib.ApplyMixDurationMatrix(spineComponent.spineAnimator.AnimationState.Data);
            motionComponent.cc.Move(Vector3.down);
            Assert.That(motionComponent.cc.isGrounded, Is.True, "测试角色必须先在临时地面上建立接地状态。");
            motionComponent.curVelo = Vector3.down * 2f;
            motionComponent.wasGroundedLastFrame = true;
            motionComponent.landThisFrame = false;
            assetKit = new AssetKit();
            Core.Asset = assetKit;
            gameplayKit = new GameplayKit();
            entity = new DodgeLandingTestEntity(actor, spineComponent, motionComponent, propertyComponent);
            gameplayKit.GetSystem<IEntitySystem>().AddEntity(entity);
            entity.AfterNew();
            Assert.That(entity.TryGetComp(out inputComponent), Is.True);
            entity.OnUpdate(0.016f);
            Assert.That(motionComponent.wasGroundedLastFrame, Is.True);
        }

        /// <summary>按玩法生命周期逆序释放实体、玩法世界、资源系统和临时物理对象。</summary>
        [TearDown]
        public void TearDown()
        {
            gameplayKit?.Dispose();
            gameplayKit = null;
            assetKit?.Dispose();
            assetKit = null;
            if (actor != null) UnityEngine.Object.DestroyImmediate(actor);
            if (floor != null) UnityEngine.Object.DestroyImmediate(floor);
            actor = null;
            floor = null;
            inputComponent = null;
            motionComponent = null;
            spineComponent = null;
            rootMotionComponent = null;
            entity = null;
        }

        /// <summary>验证前后闪避的完整根位移会经 MotionComponent 和 CharacterController 提交，不会被同帧重力 Move 覆盖。</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DodgeRootMotion_CharacterControllerPreservesFullAnimationDistance(bool isForwardDodge)
        {
            const float deltaTime = 1f / 60f;
            inputComponent.moveDir = isForwardDodge ? Vector2.right : Vector2.zero;
            inputComponent.wasDodgePressedThisFrame = true;
            Vector3 startPosition = actor.transform.position;
            entity.OnUpdate(deltaTime);
            AnimationPlayback dodgePlayback = spineComponent.CurrentPlayback;
            Assert.That(dodgePlayback, Is.Not.Null, "闪避输入必须创建受优先级管理的动画会话。");
            Assert.That(dodgePlayback.FinalEntry.MixDuration, Is.Zero.Within(0.0001f), "当前动画混合矩阵要求进入闪避时不衰减 Root Motion。");
            Vector2 expectedRootMotion = rootMotionComponent.GetAnimationRootMotion(dodgePlayback.FinalEntry.Animation);
            int frameCount = Mathf.CeilToInt(dodgePlayback.Duration / deltaTime) + 1;
            inputComponent.wasDodgePressedThisFrame = false;
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                entity.OnUpdate(deltaTime);
                spineComponent.spineAnimator.Update(deltaTime);
            }
            entity.OnUpdate(deltaTime);
            float actualDistance = actor.transform.position.x - startPosition.x;
            Assert.That(actualDistance, Is.EqualTo(expectedRootMotion.x).Within(0.01f), $"CharacterController 必须完整提交动画根位移；期望={expectedRootMotion.x}，实际={actualDistance}。");
        }

        /// <summary>验证离场时关闭 Root Motion 会清空待提交位移并拒绝后台动画继续累计，重新上场后才恢复接收。</summary>
        [Test]
        public void DisabledRootMotion_DropsPendingAndBackgroundAnimationMovement()
        {
            motionComponent.AddRootMotionDelta(Vector3.right);
            motionComponent.SetRootMotionEnabled(false);
            Assert.That(motionComponent.ConsumeRootMotionDelta(), Is.EqualTo(Vector3.zero), "关闭 Root Motion 时必须清除切人前尚未提交的动画位移。");
            motionComponent.AddRootMotionDelta(Vector3.right * 2f);
            Assert.That(motionComponent.ConsumeRootMotionDelta(), Is.EqualTo(Vector3.zero), "离场角色的后台动画不得累计 Root Motion。");
            motionComponent.SetRootMotionEnabled(true);
            motionComponent.AddRootMotionDelta(Vector3.right * 3f);
            Assert.That(motionComponent.ConsumeRootMotionDelta(), Is.EqualTo(Vector3.right * 3f), "重新上场后必须恢复接收新的 Root Motion。");
        }

        /// <summary>验证前闪避和后闪避都只中断运动积分，不会把持续接地错误转换为落地事件。</summary>
        [TestCase(true)]
        [TestCase(false)]
        public void GroundedDodgeCompletion_DoesNotCreateSyntheticLanding(bool isForwardDodge)
        {
            inputComponent.moveDir = isForwardDodge ? Vector2.right : Vector2.zero;
            inputComponent.wasDodgePressedThisFrame = true;
            entity.OnUpdate(0.016f);
            AnimationSemantic expectedSemantic = spineComponent.animationLib.dodgeExecutor.GetSemantic(isForwardDodge);
            Assert.That(spineComponent.CurrentPlayback, Is.Not.Null, "闪避输入必须启动正式动画会话。");
            Assert.That(spineComponent.CurrentPlayback.Semantic, Is.EqualTo(expectedSemantic));
            Assert.That(motionComponent.cc.isGrounded, Is.True, "闪避期间角色没有真实离地。");
            Assert.That(entity.TryGetLogic(out GravityLogic gravityLogic), Is.True);
            Assert.That(gravityLogic.Enable, Is.True, "闪避期间重力逻辑必须保持启用。");
            Assert.That(gravityLogic.BlockCnt, Is.Zero, "闪避不得阻塞重力逻辑。");
            motionComponent.curVelo.y = 0f;
            entity.OnUpdate(0.016f);
            Assert.That(motionComponent.curVelo.y, Is.LessThan(0f), "闪避动画运行期间重力逻辑仍须维持地面吸附速度。");
            inputComponent.moveDir = Vector2.zero;
            inputComponent.wasDodgePressedThisFrame = false;
            Assert.That(spineComponent.Stop(AnimationOwner.Dodge), Is.True, "测试必须通过正式动画所有者结束闪避会话。");
            entity.OnUpdate(0.016f);
            Assert.That(motionComponent.cc.isGrounded, Is.True);
            Assert.That(motionComponent.wasGroundedLastFrame, Is.True);
            Assert.That(motionComponent.landThisFrame, Is.False, "持续接地的闪避结束不得产生落地边沿。");
            entity.OnUpdate(0.016f);
            Assert.That(spineComponent.CurrentPlayback == null || spineComponent.CurrentPlayback.Owner != AnimationOwner.Landing, Is.True, "前后闪避结束后都不得意外播放落地动画。");
        }

        /// <summary>验证 GravityLogic 会在空中逐帧增加下落速度，并仅在真实重新接地后产生落地动画。</summary>
        [Test]
        public void AirborneGravity_AcceleratesDownwardAndProducesRealLanding()
        {
            entity.BlockLogic<AirMoveLogic>();
            motionComponent.cc.enabled = false;
            actor.transform.position = new Vector3(0f, 3f, 0f);
            motionComponent.cc.enabled = true;
            motionComponent.cc.Move(Vector3.zero);
            Assert.That(motionComponent.cc.isGrounded, Is.False, "抬高角色后必须先刷新 CharacterController 的旧接地缓存。");
            motionComponent.curVelo = Vector3.zero;
            motionComponent.wasGroundedLastFrame = false;
            motionComponent.landThisFrame = false;
            float startHeight = actor.transform.position.y;
            entity.OnUpdate(0.25f);
            Assert.That(motionComponent.curVelo.y, Is.LessThan(0f), "空中第一帧必须开始累计向下重力速度。");
            Assert.That(actor.transform.position.y, Is.LessThan(startHeight), "MotionLogic 必须提交 GravityLogic 产生的下落位移。");
            bool playedLanding = false;
            for (int frameIndex = 0; frameIndex < 40; frameIndex++)
            {
                entity.OnUpdate(0.1f);
                if (spineComponent.CurrentPlayback != null && spineComponent.CurrentPlayback.Owner == AnimationOwner.Landing)
                {
                    playedLanding = true;
                    break;
                }
            }
            Assert.That(motionComponent.cc.isGrounded, Is.True, $"角色必须在重力推进后重新接触临时地面；位置={actor.transform.position}，速度={motionComponent.curVelo}。");
            Assert.That(playedLanding, Is.True, $"真实的空中下落重新接地必须播放落地动画；当前位置={actor.transform.position}，当前动画={spineComponent.CurrentPlayback?.Owner}。");
        }

        /// <summary>组合闪避测试所需的真实逻辑类型，使阻塞、恢复和执行顺序与 PlayerEntity 一致。</summary>
        private sealed class DodgeLandingTestEntity : Entity
        {
            /// <summary>注册正式场景组件以及闪避会阻塞的全部运动逻辑。</summary>
            public DodgeLandingTestEntity(GameObject bindGameObject, SpineComponent spine, MotionComponent motion, PropertyComponent property)
            {
                bindGo = bindGameObject != null ? bindGameObject : throw new ArgumentNullException(nameof(bindGameObject));
                AddComp<InputComponent>();
                AddComp<DodgeComponent>();
                AddComp(spine);
                AddComp(motion);
                AddComp(property);
                AddLogic<GroundMoveLogic>();
                AddLogic<GravityLogic>();
                AddLogic<AirMoveLogic>();
                AddLogic<RotateLogic>();
                AddLogic<LandLogic>();
                AddLogic<DodgeLogic>();
                AddLogic<MotionLogic>();
            }
        }
    }
}
