using NUnit.Framework;
using Spine;
using UnityEditor;
using UnityEngine;

namespace Xuan.Prometheus.Animation.Tests
{
    /// <summary>验证 AnimationLine FMOD 绑定能够进入 Spine 原生时间轴并被统一运行时消费。</summary>
    public sealed class AnimationLineFmodAudioTests
    {
        private const string IdleAnimationLinePath = "Assets/BundleResources/Config/Animation/Lines/idle.asset";

        /// <summary>验证音效绑定会保留时间与枚举值，并生成不会泄漏给玩法事件订阅者的保留 Spine Event。</summary>
        [Test]
        public void AudioBinding_IsInjectedIntoRuntimeEventTimelineAndConsumedByFmodRuntime()
        {
            AnimationLine sourceLine = AssetDatabase.LoadAssetAtPath<AnimationLine>(IdleAnimationLinePath);
            Assert.That(sourceLine, Is.Not.Null, $"无法加载正式 AnimationLine：{IdleAnimationLinePath}");
            AnimationLine runtimeLine = Object.Instantiate(sourceLine);
            try
            {
                FmodAudioEvent testAudioEvent = (FmodAudioEvent)123456789;
                float bindingTime = Mathf.Min(0.1f, runtimeLine.Duration);
                runtimeLine.InsertAudioBinding(bindingTime, testAudioEvent);
                Assert.That(runtimeLine.AudioBindings.Count, Is.EqualTo(1));
                Spine.Event audioMarker = FindAudioMarker(runtimeLine.GetRuntimeAnimation());
                Assert.That(audioMarker, Is.Not.Null, "运行时 Spine EventTimeline 必须包含 FMOD 音效标记。");
                Assert.That(audioMarker.Time, Is.EqualTo(bindingTime).Within(0.0001f));
                Assert.That(audioMarker.Int, Is.EqualTo((int)testAudioEvent));
                Assert.That(FmodAudioRuntime.TryConsumeAnimationMarker(audioMarker, Vector3.zero), Is.True, "FMOD 保留标记必须被统一音频运行时消费，即使测试枚举没有实际 Bank 映射。");
            }
            finally
            {
                Object.DestroyImmediate(runtimeLine);
            }
        }

        /// <summary>验证仍由动作时间轴负责的正式战斗音效已绑定到对应 FMOD 事件，并且触发时间与迁移前的既有动画事件一致。</summary>
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk1.asset", FmodAudioEvent.CombatPlayerNormal_Attack_01, 0f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk1_move.asset", FmodAudioEvent.CombatPlayerNormal_Attack_01, 0f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk2.asset", FmodAudioEvent.CombatPlayerNormal_Attack_02, 0.15f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk2_move.asset", FmodAudioEvent.CombatPlayerNormal_Attack_02, 0.15f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk3.asset", FmodAudioEvent.CombatPlayerNormal_Attack_03, 0.1f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk4.asset", FmodAudioEvent.CombatPlayerNormal_Attack_04, 0.45f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk4_move.asset", FmodAudioEvent.CombatPlayerNormal_Attack_04, 0.45f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/skill_start.asset", FmodAudioEvent.CombatEnemySlime_Attack, 1.5f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/atk_branch.asset", FmodAudioEvent.CombatPlayerSkill, 0.25f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/xskill.asset", FmodAudioEvent.CombatPlayerUltimate, 2.0166667f)]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/heavy.asset", FmodAudioEvent.CombatPlayerSpecial_Attack, 0.4f)]
        public void CombatAudioBinding_UsesExpectedFmodEventAtAuthoredTime(string assetPath, FmodAudioEvent expectedEvent, float expectedTime)
        {
            AnimationLine animationLine = AssetDatabase.LoadAssetAtPath<AnimationLine>(assetPath);
            Assert.That(animationLine, Is.Not.Null, $"无法加载正式战斗 AnimationLine：{assetPath}");
            Assert.That(animationLine.AudioBindings.Count, Is.EqualTo(1), $"{animationLine.name} 必须且只能包含一个战斗 FMOD 绑定。");
            AnimationLineAudioBinding binding = animationLine.AudioBindings[0];
            Assert.That(binding.AudioEvent, Is.EqualTo(expectedEvent), $"{animationLine.name} 的 FMOD 事件用途不正确。");
            Assert.That(binding.Time, Is.EqualTo(expectedTime).Within(0.0001f), $"{animationLine.name} 的 FMOD 触发时间必须复用既有动画事件时间。");
            Spine.Event audioMarker = FindAudioMarker(animationLine.GetRuntimeAnimation());
            Assert.That(audioMarker, Is.Not.Null, $"{animationLine.name} 的运行时时间轴必须包含 FMOD 保留标记。");
            Assert.That(audioMarker.Int, Is.EqualTo((int)expectedEvent), $"{animationLine.name} 的运行时 FMOD 枚举载荷不正确。");
        }

        /// <summary>验证受击动画不再持有实际伤害音效，避免 DamageApplied 表现入口与非致命受击动画各播放一次。</summary>
        [TestCase("Assets/BundleResources/Config/Animation/Lines/leg_hitted.asset")]
        [TestCase("Assets/BundleResources/Config/Animation/Lines/leg_hitted 1.asset")]
        public void HitReactionAnimation_DoesNotOwnDamageImpactAudio(string assetPath)
        {
            AnimationLine animationLine = AssetDatabase.LoadAssetAtPath<AnimationLine>(assetPath);
            Assert.That(animationLine, Is.Not.Null, $"无法加载正式受击 AnimationLine：{assetPath}");
            Assert.That(animationLine.AudioBindings, Is.Empty, $"{animationLine.name} 的命中音效必须由 DamageApplied 表现入口统一播放，不能继续绑定在可被死亡动画抢占的受击时间轴上。");
        }

        /// <summary>从运行时动画的全部 EventTimeline 中查找 AnimationLine 注入的 FMOD 保留标记。</summary>
        private static Spine.Event FindAudioMarker(Spine.Animation runtimeAnimation)
        {
            Assert.That(runtimeAnimation, Is.Not.Null);
            ExposedList<Timeline> timelines = runtimeAnimation.Timelines;
            for (int timelineIndex = 0; timelineIndex < timelines.Count; timelineIndex++)
            {
                EventTimeline eventTimeline = timelines.Items[timelineIndex] as EventTimeline;
                if (eventTimeline == null) continue;
                for (int eventIndex = 0; eventIndex < eventTimeline.Events.Length; eventIndex++)
                {
                    Spine.Event candidate = eventTimeline.Events[eventIndex];
                    if (candidate != null && candidate.Data != null && candidate.Data.Name == FmodAudioRuntime.AnimationMarkerEventName) return candidate;
                }
            }
            return null;
        }
    }
}
