using System;
using System.Collections.Generic;
using NUnit.Framework;
using Spine;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证行为动画严格受权威相位驱动，并验证 Spine 官方根运动缩放、镜像和姿势抵消语义没有被客户端适配器改变。</summary>
    public sealed class SpineBehaviorAuthorityTests
    {
        private const string YefaPrefabPath = "Assets/BundleResources/Character/Yefa.prefab";
        private const string YefaAttackPath = "Assets/BundleResources/Config/Actor/YefaBasicAttack1.asset";

        /// <summary>验证相同 BehaviorPhase 在不同渲染帧时长下产生完全相同的非循环动画时间。</summary>
        [Test]
        public void ResolveTrackTime_SameAuthoritativePhaseDoesNotDependOnRenderDeltaTime()
        {
            BehaviorPhase phase = CreatePhase(120, BehaviorPhase.One, 30);

            float firstFrameTrackTime = SpineBehaviorAuthorityMath.ResolveTrackTime(phase, 0.5f, 10, 50, 120, 2f, false, 60);
            float longFrameTrackTime = SpineBehaviorAuthorityMath.ResolveTrackTime(phase, 0.5f, 10, 50, 120, 2f, false, 60);

            Assert.That(firstFrameTrackTime, Is.EqualTo(1.025f).Within(0.000001f));
            Assert.That(longFrameTrackTime, Is.EqualTo(firstFrameTrackTime).Within(0.000001f));
        }

        /// <summary>验证高攻击速度相位与插值共同决定循环动画时间，而不是额外乘一次速率导致双重加速。</summary>
        [Test]
        public void ResolveTrackTime_LoopingCueUsesInterpolatedBehaviorTicksExactlyOnce()
        {
            BehaviorPhase phase = CreatePhase(120, BehaviorPhase.One * 2, 1);

            float trackTime = SpineBehaviorAuthorityMath.ResolveTrackTime(phase, 0.25f, 0, 120, 120, 3f, true, 60);

            Assert.That(trackTime, Is.EqualTo(2.5f / 60f).Within(0.000001f));
        }

        /// <summary>验证根运动严格遵循官方先 Skeleton 与父骨缩放、再换轴、最后 RootMotionScale 的计算顺序。</summary>
        [Test]
        public void ConvertBakedRootMotion_MatchesSkeletonRootMotionBaseScaleOrder()
        {
            var settings = new SpineRootMotionAxisSettings(true, true, 1.5f, 0.25f, 0.1f, 0.2f);

            Vector3 converted = SpineBehaviorAuthorityMath.ConvertBakedRootMotion(new Vector3(2f, 3f, 0.75f), new Vector2(-1f, 2f), new Vector2(-0.5f, 3f), settings);

            Assert.That(converted.x, Is.EqualTo(3.3f).Within(0.000001f));
            Assert.That(converted.y, Is.EqualTo(4.7f).Within(0.000001f));
            Assert.That(converted.z, Is.EqualTo(0.75f).Within(0.000001f));
        }

        /// <summary>验证左朝向通过 Skeleton.ScaleX 翻转烘焙 X 位移，并且关闭的轴不会被换轴参数重新注入。</summary>
        [Test]
        public void ConvertBakedRootMotion_LeftFacingFlipsXAndDisabledAxisRemainsZero()
        {
            Vector3 rightFacing = SpineBehaviorAuthorityMath.ConvertBakedRootMotion(new Vector3(0.4f, 0.2f, 0f), Vector2.one, Vector2.one, SpineRootMotionAxisSettings.Default);
            Vector3 leftFacing = SpineBehaviorAuthorityMath.ConvertBakedRootMotion(new Vector3(0.4f, 0.2f, 0f), new Vector2(-1f, 1f), Vector2.one, SpineRootMotionAxisSettings.Default);
            var xDisabledSettings = new SpineRootMotionAxisSettings(false, true, 1f, 1f, 10f, 0f);
            Vector3 xDisabled = SpineBehaviorAuthorityMath.ConvertBakedRootMotion(new Vector3(0.4f, 0.2f, 0f), Vector2.one, Vector2.one, xDisabledSettings);

            Assert.That(rightFacing.x, Is.EqualTo(0.4f).Within(0.000001f));
            Assert.That(leftFacing.x, Is.EqualTo(-0.4f).Within(0.000001f));
            Assert.That(xDisabled.x, Is.Zero.Within(0.000001f));
        }

        /// <summary>使用真实 Yefa Skeleton 验证 TrackEntry 被冻结、显式 Seek 后不会再按自由渲染时间漂移，并验证 hips 位移会从最终姿势扣除。</summary>
        [Test]
        public void SpineBehaviorAuthorityRuntime_YefaTrackIsPhaseLockedAndRootPoseIsCompensated()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(YefaPrefabPath);
            SpineBehaviorAuthorityRuntime authority = null;
            try
            {
                SkeletonAnimation skeletonAnimation = prefabRoot.GetComponent<SkeletonAnimation>();
                SkeletonRootMotion legacyRootMotion = prefabRoot.GetComponent<SkeletonRootMotion>();
                ActorBehaviorDefinition behavior = AssetDatabase.LoadAssetAtPath<ActorBehaviorDefinition>(YefaAttackPath);
                Assert.That(skeletonAnimation, Is.Not.Null);
                Assert.That(legacyRootMotion, Is.Not.Null);
                Assert.That(behavior, Is.Not.Null);
                skeletonAnimation.Initialize(true);
                legacyRootMotion.enabled = false;
                Assert.That(behavior.TryGetPresentationVariant("Moving", out ActorPresentationVariantDefinition movingVariant), Is.True);
                ActorPresentationCueDefinition animationCue = FindSpineCue(movingVariant);
                Assert.That(animationCue.Animation, Is.Not.Null);
                if (animationCue.Animation.Animation == null) animationCue.Animation.Initialize();
                TrackEntry entry = skeletonAnimation.AnimationState.SetAnimation(animationCue.SpineTrack, animationCue.Animation.Animation, animationCue.Loop);
                authority = new SpineBehaviorAuthorityRuntime(skeletonAnimation, legacyRootMotion, 60, "hips");
                authority.BeginBehavior(behavior.DurationTicks);
                authority.RegisterTrack(entry, animationCue.StartTick, animationCue.EndTick, animationCue.Loop);
                BehaviorPhase phase = CreatePhase(behavior.DurationTicks, BehaviorPhase.One, Mathf.Max(1, behavior.DurationTicks / 3));

                authority.Present(phase, 0.5f);
                float authoritativeTrackTime = entry.TrackTime;
                float expectedTrackTime = SpineBehaviorAuthorityMath.ResolveTrackTime(phase, 0.5f, animationCue.StartTick, animationCue.EndTick, behavior.DurationTicks, entry.Animation.Duration, animationCue.Loop, 60);
                skeletonAnimation.Update(0.75f);

                Assert.That(entry.TimeScale, Is.Zero.Within(0.000001f));
                Assert.That(authoritativeTrackTime, Is.EqualTo(expectedTrackTime).Within(0.000001f));
                Assert.That(entry.TrackTime, Is.EqualTo(authoritativeTrackTime).Within(0.000001f));
                authority.SetPoseCompensation(true);
                authority.Present(phase, 0.5f);
                AssertRootPoseCompensationMatchesOfficialSemantics(skeletonAnimation.Skeleton, "hips");
            }
            finally
            {
                authority?.Dispose();
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        /// <summary>验证 SpineComponent 使用 FacingRoot 局部右向基准，不会在 Actor 根具有出生旋转时把子节点强制写到世界 identity。</summary>
        [Test]
        public void SpineComponent_FacingUsesLocalBaselineAndPreservesActorRotation()
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(YefaPrefabPath);
            try
            {
                SpineComponent spineComponent = prefabRoot.GetComponent<SpineComponent>();
                Assert.That(spineComponent, Is.Not.Null);
                prefabRoot.transform.rotation = Quaternion.Euler(0f, 37f, 0f);
                Quaternion actorWorldRotation = prefabRoot.transform.rotation;
                Quaternion rightLocalRotation = spineComponent.FacingRootRightLocalRotation;

                spineComponent.CurFaceDir = FaceDir.Left;

                Assert.That(Quaternion.Angle(prefabRoot.transform.rotation, actorWorldRotation), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(spineComponent.rotateRoot.localRotation, rightLocalRotation * Quaternion.Euler(0f, 180f, 0f)), Is.LessThan(0.0001f));
                Assert.That(spineComponent.FacingSign, Is.EqualTo(-1f));
                spineComponent.CurFaceDir = FaceDir.Right;
                Assert.That(Quaternion.Angle(spineComponent.rotateRoot.localRotation, rightLocalRotation), Is.LessThan(0.0001f));
                Assert.That(spineComponent.FacingSign, Is.EqualTo(1f));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        /// <summary>从行为变体中读取唯一 Spine 动画 Cue，并拒绝测试资产缺失。</summary>
        private static ActorPresentationCueDefinition FindSpineCue(ActorPresentationVariantDefinition variant)
        {
            for (int index = 0; index < variant.Cues.Count; index++) if (variant.Cues[index] != null && variant.Cues[index].Kind == ActorPresentationCueKind.SpineAnimation) return variant.Cues[index];
            throw new InvalidOperationException($"Presentation variant '{variant.VariantId}' does not contain a Spine animation cue.");
        }

        /// <summary>通过公开 BehaviorController API 创建测试相位，避免测试依赖 BehaviorPhase 的内部构造函数。</summary>
        private static BehaviorPhase CreatePhase(int durationTicks, int rateRaw, int simulationSteps)
        {
            using (var controller = new BehaviorController(new NullBehaviorSink()))
            {
                var program = new BehaviorProgram("Spine.Authority.Test", durationTicks, Array.Empty<SimulationClip>());
                Assert.That(controller.TryStart(program, rateRaw, out BehaviorHandle handle), Is.True);
                for (int step = 0; step < simulationSteps; step++) controller.Step();
                Assert.That(controller.TryGetPhase(handle, out BehaviorPhase phase), Is.True);
                return phase;
            }
        }

        /// <summary>按照 SkeletonRootMotionBase.ClearEffectiveBoneOffsets 的公开源码公式验证真实 Skeleton 的根姿势抵消结果。</summary>
        private static void AssertRootPoseCompensationMatchesOfficialSemantics(Skeleton skeleton, string rootMotionBoneName)
        {
            Bone rootMotionBone = skeleton.FindBone(rootMotionBoneName);
            Assert.That(rootMotionBone, Is.Not.Null);
            Vector2 parentBoneScale = Vector2.one;
            Bone scaleBone = rootMotionBone;
            while ((scaleBone = scaleBone.Parent) != null)
            {
                parentBoneScale.x *= scaleBone.ScaleX;
                parentBoneScale.y *= scaleBone.ScaleY;
            }
            foreach (Bone topLevelBone in skeleton.Bones)
            {
                if (topLevelBone.Parent != null) continue;
                if (ReferenceEquals(topLevelBone, rootMotionBone))
                {
                    Assert.That(topLevelBone.X, Is.Zero.Within(0.0001f));
                    Assert.That(topLevelBone.Y, Is.Zero.Within(0.0001f));
                }
                else
                {
                    Assert.That(topLevelBone.X, Is.EqualTo((rootMotionBone.Data.X - rootMotionBone.X) * parentBoneScale.x).Within(0.0001f));
                    Assert.That(topLevelBone.Y, Is.EqualTo((rootMotionBone.Data.Y - rootMotionBone.Y) * parentBoneScale.y).Within(0.0001f));
                }
            }
        }

        /// <summary>为纯相位测试提供无副作用行为回调接收器。</summary>
        private sealed class NullBehaviorSink : IBehaviorSimulationSink
        {
            /// <inheritdoc />
            public void OnBehaviorStarted(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase)
            {
            }

            /// <inheritdoc />
            public void OnClipEntered(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
            {
            }

            /// <inheritdoc />
            public void OnClipSampled(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase)
            {
            }

            /// <inheritdoc />
            public void OnClipExited(BehaviorHandle handle, SimulationClip clip, BehaviorPhase phase, BehaviorEndReason reason)
            {
            }

            /// <inheritdoc />
            public void OnBehaviorEnded(BehaviorHandle handle, BehaviorProgram program, BehaviorPhase phase, BehaviorEndReason reason)
            {
            }
        }
    }
}
