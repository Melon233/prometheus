using NUnit.Framework;
using Spine;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>验证 MixDuration 矩阵的默认值、有向覆盖和 SpineComponent 全部过渡入口。</summary>
    public sealed class AnimationMixDurationMatrixTests
    {
        private const string SlimePrefabPath = "Assets/BundleResources/Enemy/Slime.prefab";

        private GameObject slimeInstance;
        private AnimationLibrary runtimeLibrary;
        private SpineComponent spineComponent;

        /// <summary>为集成测试实例化正式史莱姆，并克隆动画库以隔离矩阵修改。</summary>
        [SetUp]
        public void SetUp()
        {
            GameObject slimePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlimePrefabPath);
            Assert.That(slimePrefab, Is.Not.Null, $"无法加载正式史莱姆预制体：{SlimePrefabPath}");
            slimeInstance = Object.Instantiate(slimePrefab);
            spineComponent = slimeInstance.GetEntityComponent<SpineComponent>();
            Assert.That(spineComponent, Is.Not.Null, "史莱姆预制体必须包含 SpineComponent。");
            runtimeLibrary = Object.Instantiate(spineComponent.animationLib);
            runtimeLibrary.MixDurationMatrix.ClearOverrides();
            runtimeLibrary.MixDurationMatrix.DefaultDuration = AnimationMixDurationMatrix.FallbackDuration;
            spineComponent.animationLib = runtimeLibrary;
            spineComponent.spineAnimator = slimeInstance.GetComponent<SkeletonAnimation>();
            Assert.That(spineComponent.spineAnimator, Is.Not.Null, "史莱姆预制体必须包含 SkeletonAnimation。");
            spineComponent.spineAnimator.Initialize(true);
        }

        /// <summary>释放集成测试创建的动画库克隆和 GameObject，避免跨测试污染。</summary>
        [TearDown]
        public void TearDown()
        {
            if (runtimeLibrary != null) Object.DestroyImmediate(runtimeLibrary);
            runtimeLibrary = null;
            if (slimeInstance != null) Object.DestroyImmediate(slimeInstance);
            slimeInstance = null;
            spineComponent = null;
        }

        /// <summary>验证空矩阵对任意方向返回 0.2 秒，并确保两个方向可以配置不同值。</summary>
        [Test]
        public void Matrix_DefaultAndDirectionalOverrides_AreResolvedCorrectly()
        {
            AnimationMixDurationMatrix matrix = new AnimationMixDurationMatrix();
            Assert.That(matrix.GetMixDuration(AnimationSemantic.Idle, AnimationSemantic.Run), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(matrix.GetMixDuration(AnimationSemantic.Run, AnimationSemantic.Idle), Is.EqualTo(0.2f).Within(0.0001f));
            matrix.SetMixDuration(AnimationSemantic.Idle, AnimationSemantic.Run, 0.35f);
            matrix.SetMixDuration(AnimationSemantic.Run, AnimationSemantic.Idle, 0.08f);
            Assert.That(matrix.GetMixDuration(AnimationSemantic.Idle, AnimationSemantic.Run), Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(matrix.GetMixDuration(AnimationSemantic.Run, AnimationSemantic.Idle), Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(matrix.RemoveMixDuration(AnimationSemantic.Idle, AnimationSemantic.Run), Is.True);
            Assert.That(matrix.GetMixDuration(AnimationSemantic.Idle, AnimationSemantic.Run), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(matrix.GetMixDuration(AnimationSemantic.Run, AnimationSemantic.Idle), Is.EqualTo(0.08f).Within(0.0001f));
        }

        /// <summary>验证直接切换、序列内部切换、Spine StateData 和停止淡出全部读取矩阵对应单元格。</summary>
        [Test]
        public void SpineComponent_AllTransitionPaths_ReadMixDurationMatrix()
        {
            AnimationMixDurationMatrix matrix = runtimeLibrary.MixDurationMatrix;
            matrix.SetMixDuration(AnimationSemantic.Idle, AnimationSemantic.Run, 0.34f);
            matrix.SetMixDuration(AnimationSemantic.Run, AnimationSemantic.Hit, 0.09f);
            matrix.SetMixDuration(AnimationSemantic.Hit, AnimationSemantic.HitRecovery, 0.42f);
            matrix.SetMixDuration(AnimationSemantic.Hit, AnimationSemantic.None, 0.31f);
            runtimeLibrary.ApplyMixDurationMatrix(spineComponent.spineAnimator.AnimationState.Data);
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.Idle, out AnimationLine idleLine), Is.True);
            Assert.That(runtimeLibrary.TryGetLine(AnimationSemantic.Run, out AnimationLine runLine), Is.True);
            Assert.That(spineComponent.spineAnimator.AnimationState.Data.GetMix(idleLine.GetRuntimeAnimation(), runLine.GetRuntimeAnimation()), Is.EqualTo(0.34f).Within(0.0001f));
            AnimationPlayback idlePlayback = spineComponent.TryPlay(AnimationSemantic.Idle, AnimationOwner.Idle, AnimationPriority.Idle, true, 1f, true);
            Assert.That(idlePlayback, Is.Not.Null);
            AnimationPlayback runPlayback = spineComponent.TryPlay(AnimationSemantic.Run, AnimationOwner.GroundMove, AnimationPriority.Locomotion, true);
            Assert.That(runPlayback, Is.Not.Null);
            Assert.That(spineComponent.spineAnimator.AnimationState.GetCurrent(0).MixDuration, Is.EqualTo(0.34f).Within(0.0001f));
            AnimationPlayback hitPlayback = spineComponent.TryPlaySequence(AnimationSemantic.Hit, AnimationSemantic.HitRecovery, AnimationOwner.HitReaction, AnimationPriority.HitReaction);
            Assert.That(hitPlayback, Is.Not.Null);
            Assert.That(spineComponent.spineAnimator.AnimationState.GetCurrent(0).MixDuration, Is.EqualTo(0.09f).Within(0.0001f));
            Assert.That(hitPlayback.FinalEntry.MixDuration, Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(spineComponent.Stop(AnimationOwner.HitReaction), Is.True);
            TrackEntry emptyEntry = spineComponent.spineAnimator.AnimationState.GetCurrent(0);
            Assert.That(emptyEntry, Is.Not.Null);
            Assert.That(emptyEntry.MixDuration, Is.EqualTo(0.31f).Within(0.0001f));
        }
    }
}
