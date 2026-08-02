using System;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Effects;

namespace Xuan.Prometheus.Actor.Editor
{
    /// <summary>
    /// 保存从 Yefa 旧 AnimationLibrary 私有 Executor 字段读取出的行为表现、音效和 VFX 槽位；所有引用均保持为原始资产，不复制客户端资源。
    /// </summary>
    internal sealed class LegacyYefaActionSource
    {
        internal LegacyYefaActionSource(AnimationReferenceAsset skillStartAnimation, AnimationReferenceAsset skillAnimation, AudioClip skillAudio, int skillVfxSlotIndex, AnimationReferenceAsset ultimateAnimation, AudioClip ultimateAudio, int ultimateVfxSlotIndex, AnimationReferenceAsset specialAttackAnimation, AudioClip specialAttackAudio, int specialAttackVfxSlotIndex, AnimationReferenceAsset dodgeFrontAnimation, AnimationReferenceAsset dodgeBackAnimation)
        {
            SkillStartAnimation = skillStartAnimation;
            SkillAnimation = skillAnimation;
            SkillAudio = skillAudio;
            SkillVfxSlotIndex = skillVfxSlotIndex;
            UltimateAnimation = ultimateAnimation;
            UltimateAudio = ultimateAudio;
            UltimateVfxSlotIndex = ultimateVfxSlotIndex;
            SpecialAttackAnimation = specialAttackAnimation;
            SpecialAttackAudio = specialAttackAudio;
            SpecialAttackVfxSlotIndex = specialAttackVfxSlotIndex;
            DodgeFrontAnimation = dodgeFrontAnimation;
            DodgeBackAnimation = dodgeBackAnimation;
        }

        internal AnimationReferenceAsset SkillStartAnimation { get; }

        internal AnimationReferenceAsset SkillAnimation { get; }

        internal AudioClip SkillAudio { get; }

        internal int SkillVfxSlotIndex { get; }

        internal AnimationReferenceAsset UltimateAnimation { get; }

        internal AudioClip UltimateAudio { get; }

        internal int UltimateVfxSlotIndex { get; }

        internal AnimationReferenceAsset SpecialAttackAnimation { get; }

        internal AudioClip SpecialAttackAudio { get; }

        internal int SpecialAttackVfxSlotIndex { get; }

        internal AnimationReferenceAsset DodgeFrontAnimation { get; }

        internal AnimationReferenceAsset DodgeBackAnimation { get; }
    }

    /// <summary>
    /// 通过 SerializedProperty 读取旧私有 Executor 字段，使迁移结果严格跟随当前 AnimationLibrary 资产而不是代码中的猜测引用。
    /// </summary>
    internal static class LegacyYefaActionSourceReader
    {
        /// <summary>
        /// 读取 Yefa 的 Skill、Ultimate、SpecialAttack 和前后 Dodge 数据，并在任何必需引用缺失时停止迁移。
        /// </summary>
        internal static LegacyYefaActionSource Load(LegacyActorSource actorSource)
        {
            if (actorSource == null || actorSource.Library == null) throw new ArgumentNullException(nameof(actorSource));
            global::Xuan.Prometheus.AnimationLibrary library = actorSource.Library;
            string assetPath = AssetDatabase.GetAssetPath(library);
            if (!library.hasTalent) throw new InvalidOperationException($"旧 AnimationLibrary '{assetPath}' 没有启用 Talent，无法迁移 Yefa 主动行为。");
            SerializedObject serializedLibrary = new SerializedObject(library);
            AnimationReferenceAsset skillStartAnimation = ReadObjectReference<AnimationReferenceAsset>(serializedLibrary, "skillExecutor", "skillStartAni", assetPath);
            AnimationReferenceAsset skillAnimation = ReadObjectReference<AnimationReferenceAsset>(serializedLibrary, "skillExecutor", "skillAni", assetPath);
            AudioClip skillAudio = ReadObjectReference<AudioClip>(serializedLibrary, "skillExecutor", "skillAudio", assetPath);
            int skillVfxSlotIndex = ReadEnumValue(serializedLibrary, "skillExecutor", "skillVfx", assetPath);
            AnimationReferenceAsset ultimateAnimation = ReadObjectReference<AnimationReferenceAsset>(serializedLibrary, "ultimateExecutor", "ultimateAni", assetPath);
            AudioClip ultimateAudio = ReadObjectReference<AudioClip>(serializedLibrary, "ultimateExecutor", "ultimateAudio", assetPath);
            int ultimateVfxSlotIndex = ReadEnumValue(serializedLibrary, "ultimateExecutor", "ultVfx", assetPath);
            AnimationReferenceAsset specialAttackAnimation = ReadObjectReference<AnimationReferenceAsset>(serializedLibrary, "specialAttackExecutor", "specialAttackAni", assetPath);
            AudioClip specialAttackAudio = ReadObjectReference<AudioClip>(serializedLibrary, "specialAttackExecutor", "specialAttackAudio", assetPath);
            int specialAttackVfxSlotIndex = ReadEnumValue(serializedLibrary, "specialAttackExecutor", "specialAttackVfx", assetPath);
            AnimationReferenceAsset dodgeFrontAnimation = ReadObjectReference<AnimationReferenceAsset>(serializedLibrary, "dodgeExecutor", "dodgeFrontAnimation", assetPath);
            AnimationReferenceAsset dodgeBackAnimation = ReadObjectReference<AnimationReferenceAsset>(serializedLibrary, "dodgeExecutor", "dodgeBackAnimation", assetPath);
            return new LegacyYefaActionSource(skillStartAnimation, skillAnimation, skillAudio, skillVfxSlotIndex, ultimateAnimation, ultimateAudio, ultimateVfxSlotIndex, specialAttackAnimation, specialAttackAudio, specialAttackVfxSlotIndex, dodgeFrontAnimation, dodgeBackAnimation);
        }

        /// <summary>
        /// 读取一个嵌套 Executor 中的必需对象引用，并在旧序列化结构漂移时提供完整字段路径。
        /// </summary>
        private static TObject ReadObjectReference<TObject>(SerializedObject owner, string parentName, string childName, string assetPath) where TObject : UnityEngine.Object
        {
            SerializedProperty child = RequireRelativeProperty(owner, parentName, childName, assetPath);
            TObject result = child.objectReferenceValue as TObject;
            if (result == null) throw new InvalidOperationException($"资产 '{assetPath}' 的字段 '{parentName}.{childName}' 缺少必需的 '{typeof(TObject).Name}' 引用。");
            return result;
        }

        /// <summary>
        /// 读取一个嵌套 Executor 中的真实枚举值；该值直接对应旧 VfxComponent.vfxSlots 索引。
        /// </summary>
        private static int ReadEnumValue(SerializedObject owner, string parentName, string childName, string assetPath)
        {
            SerializedProperty child = RequireRelativeProperty(owner, parentName, childName, assetPath);
            if (child.propertyType != SerializedPropertyType.Enum) throw new InvalidOperationException($"资产 '{assetPath}' 的字段 '{parentName}.{childName}' 不是枚举，无法解析旧 VFX 槽位。");
            return child.enumValueIndex;
        }

        /// <summary>
        /// 获取必需的嵌套序列化字段，避免迁移器在字段重命名后静默写入错误资源。
        /// </summary>
        private static SerializedProperty RequireRelativeProperty(SerializedObject owner, string parentName, string childName, string assetPath)
        {
            SerializedProperty parent = owner.FindProperty(parentName);
            if (parent == null) throw new InvalidOperationException($"资产 '{assetPath}' 缺少序列化字段 '{parentName}'。");
            SerializedProperty child = parent.FindPropertyRelative(childName);
            if (child == null) throw new InvalidOperationException($"资产 '{assetPath}' 缺少序列化字段 '{parentName}.{childName}'。");
            return child;
        }
    }

    /// <summary>
    /// 描述一个表现变体中的单段 Spine 动画及其相对行为 Tick 区间；Skill 通过两段连续 Cue 保留旧 Add 动画语义。
    /// </summary>
    internal sealed class LegacyAnimationCuePlan
    {
        internal LegacyAnimationCuePlan(string cueId, AnimationReferenceAsset animation, int startTick, int endTick)
        {
            CueId = cueId;
            Animation = animation;
            StartTick = startTick;
            EndTick = endTick;
        }

        internal string CueId { get; }

        internal AnimationReferenceAsset Animation { get; }

        internal int StartTick { get; }

        internal int EndTick { get; }
    }

    /// <summary>
    /// 保存同一权威行为的一个客户端表现变体；Dodge 使用 Default 后退和 Moving 前进两个变体。
    /// </summary>
    internal sealed class LegacyActionVariantPlan
    {
        internal LegacyActionVariantPlan(string variantId, LegacyAnimationCuePlan[] animationCues)
        {
            VariantId = variantId;
            AnimationCues = animationCues ?? throw new ArgumentNullException(nameof(animationCues));
        }

        internal string VariantId { get; }

        internal LegacyAnimationCuePlan[] AnimationCues { get; }
    }

    /// <summary>
    /// 保存一个行为 MotionClip 对应的变体约束和完整逐 Tick 根运动样本；同一 Dodge 行为可以同时声明 Default 与 Moving 两条互斥轨道。
    /// </summary>
    internal sealed class LegacyActionMotionPlan
    {
        internal LegacyActionMotionPlan(string motionId, string requiredVariantId, Vector3[] bakedMotionSamples)
        {
            MotionId = motionId;
            RequiredVariantId = requiredVariantId;
            BakedMotionSamples = bakedMotionSamples ?? throw new ArgumentNullException(nameof(bakedMotionSamples));
        }

        internal string MotionId { get; }

        internal string RequiredVariantId { get; }

        internal Vector3[] BakedMotionSamples { get; }
    }

    /// <summary>
    /// 保存一个 Yefa 非普通攻击行为的完整确定性迁移计划，包括命令、战斗标签、命中窗口和客户端表现资源。
    /// </summary>
    internal sealed class LegacyActionPlan
    {
        internal LegacyActionPlan(string assetPath, string behaviorId, ActorBehaviorCommand command, int durationTicks, int chainFromTick, bool hasHitWindow, int hitStartTick, int hitEndTick, string hitboxBindingId, EffectTag effectTags, ActorFactionMask targetFactions, AudioClip audioClip, int audioTick, string vfxBindingId, int vfxSlotIndex, int vfxTick, LegacyActionVariantPlan[] variants, LegacyActionMotionPlan[] motionPlans)
        {
            AssetPath = assetPath;
            BehaviorId = behaviorId;
            Command = command;
            DurationTicks = durationTicks;
            ChainFromTick = chainFromTick;
            HasHitWindow = hasHitWindow;
            HitStartTick = hitStartTick;
            HitEndTick = hitEndTick;
            HitboxBindingId = hitboxBindingId;
            EffectTags = effectTags;
            TargetFactions = targetFactions;
            AudioClip = audioClip;
            AudioTick = audioTick;
            VfxBindingId = vfxBindingId;
            VfxSlotIndex = vfxSlotIndex;
            VfxTick = vfxTick;
            Variants = variants ?? throw new ArgumentNullException(nameof(variants));
            MotionPlans = motionPlans ?? throw new ArgumentNullException(nameof(motionPlans));
        }

        internal string AssetPath { get; }

        internal string BehaviorId { get; }

        internal ActorBehaviorCommand Command { get; }

        internal int DurationTicks { get; }

        internal int ChainFromTick { get; }

        internal bool HasHitWindow { get; }

        internal int HitStartTick { get; }

        internal int HitEndTick { get; }

        internal string HitboxBindingId { get; }

        internal EffectTag EffectTags { get; }

        internal ActorFactionMask TargetFactions { get; }

        internal AudioClip AudioClip { get; }

        internal int AudioTick { get; }

        internal string VfxBindingId { get; }

        internal int VfxSlotIndex { get; }

        internal int VfxTick { get; }

        internal LegacyActionVariantPlan[] Variants { get; }

        internal LegacyActionMotionPlan[] MotionPlans { get; }

        internal bool HasAudioCue => AudioClip != null;

        internal bool HasVfxCue => !string.IsNullOrWhiteSpace(VfxBindingId);
    }

    /// <summary>
    /// 将 Yefa 旧 Executor 内容转换为四个可重复生成且可被校验器重建的行为计划。
    /// </summary>
    internal static class LegacyYefaActionPlanBuilder
    {
        /// <summary>
        /// 按稳定顺序创建 Skill、Ultimate、SpecialAttack 和 Dodge；所有时间均从 Spine 当前资源量化而来。
        /// </summary>
        internal static LegacyActionPlan[] Build(LegacyActorSource actorSource, Action<string> reportWarning)
        {
            if (actorSource == null) throw new ArgumentNullException(nameof(actorSource));
            LegacyYefaActionSource source = LegacyYefaActionSourceReader.Load(actorSource);
            LegacyAttackTiming skillTiming = LegacyAttackTiming.Read(source.SkillAnimation, actorSource.Library, reportWarning);
            int skillStartDuration = LegacyAttackTiming.ReadDurationTicks(source.SkillStartAnimation);
            int skillDuration = skillStartDuration + skillTiming.DurationTicks;
            int skillHitStart = skillStartDuration + skillTiming.HitStartTick;
            int skillHitEnd = skillStartDuration + skillTiming.HitEndTick;
            LegacyActionMotionPlan[] skillMotionPlans = BuildSequentialMotionPlans("Yefa.Skill.Motion", "Default", source.SkillStartAnimation, skillStartDuration, source.SkillAnimation, skillTiming.DurationTicks, reportWarning);
            LegacyActionPlan skill = new LegacyActionPlan(LegacyActorMigrationPaths.YefaSkillBehaviorPath, "Yefa.Skill", ActorBehaviorCommand.Skill, skillDuration, skillDuration, true, skillHitStart, skillHitEnd, "Yefa.Skill.Hitbox", EffectTag.Attack | EffectTag.Skill, ActorFactionMask.Enemy, source.SkillAudio, skillHitStart, "Yefa.Skill.Vfx", source.SkillVfxSlotIndex, skillHitStart, new[] { new LegacyActionVariantPlan("Default", new[] { new LegacyAnimationCuePlan("Animation.Start", source.SkillStartAnimation, 0, skillStartDuration), new LegacyAnimationCuePlan("Animation.Main", source.SkillAnimation, skillStartDuration, skillDuration) }) }, skillMotionPlans);
            LegacyAttackTiming ultimateTiming = LegacyAttackTiming.Read(source.UltimateAnimation, actorSource.Library, reportWarning);
            LegacyActionPlan ultimate = new LegacyActionPlan(LegacyActorMigrationPaths.YefaUltimateBehaviorPath, "Yefa.Ultimate", ActorBehaviorCommand.Ultimate, ultimateTiming.DurationTicks, ultimateTiming.DurationTicks, true, ultimateTiming.HitStartTick, ultimateTiming.HitEndTick, "Yefa.Ultimate.Hitbox", EffectTag.Attack | EffectTag.Skill, ActorFactionMask.Enemy, source.UltimateAudio, ultimateTiming.HitStartTick, "Yefa.Ultimate.Vfx", source.UltimateVfxSlotIndex, ultimateTiming.HitStartTick, new[] { CreateSingleAnimationVariant("Default", source.UltimateAnimation, ultimateTiming.DurationTicks) }, BuildSingleAnimationMotionPlans("Yefa.Ultimate.Motion", "Default", source.UltimateAnimation, ultimateTiming.DurationTicks, reportWarning));
            LegacyAttackTiming specialTiming = LegacyAttackTiming.Read(source.SpecialAttackAnimation, actorSource.Library, reportWarning);
            LegacyActionPlan specialAttack = new LegacyActionPlan(LegacyActorMigrationPaths.YefaSpecialAttackBehaviorPath, "Yefa.SpecialAttack", ActorBehaviorCommand.SpecialAttack, specialTiming.DurationTicks, specialTiming.DurationTicks, true, specialTiming.HitStartTick, specialTiming.HitEndTick, "Yefa.SpecialAttack.Hitbox", EffectTag.Attack, ActorFactionMask.Enemy, source.SpecialAttackAudio, specialTiming.HitStartTick, "Yefa.SpecialAttack.Vfx", source.SpecialAttackVfxSlotIndex, specialTiming.HitStartTick, new[] { CreateSingleAnimationVariant("Default", source.SpecialAttackAnimation, specialTiming.DurationTicks) }, BuildSingleAnimationMotionPlans("Yefa.SpecialAttack.Motion", "Default", source.SpecialAttackAnimation, specialTiming.DurationTicks, reportWarning));
            int dodgeFrontDuration = LegacyAttackTiming.ReadDurationTicks(source.DodgeFrontAnimation);
            int dodgeBackDuration = LegacyAttackTiming.ReadDurationTicks(source.DodgeBackAnimation);
            int dodgeDuration = Mathf.Max(dodgeFrontDuration, dodgeBackDuration);
            LegacyActionMotionPlan[] dodgeMotionPlans = CombineMotionPlans(BuildSingleAnimationMotionPlans("Yefa.Dodge.Default.Motion", "Default", source.DodgeBackAnimation, dodgeDuration, reportWarning), BuildSingleAnimationMotionPlans("Yefa.Dodge.Moving.Motion", "Moving", source.DodgeFrontAnimation, dodgeDuration, reportWarning));
            LegacyActionPlan dodge = new LegacyActionPlan(LegacyActorMigrationPaths.YefaDodgeBehaviorPath, "Yefa.Dodge", ActorBehaviorCommand.Dodge, dodgeDuration, dodgeDuration, false, 0, 0, null, EffectTag.None, ActorFactionMask.None, null, 0, null, -1, 0, new[] { CreateSingleAnimationVariant("Default", source.DodgeBackAnimation, dodgeBackDuration), CreateSingleAnimationVariant("Moving", source.DodgeFrontAnimation, dodgeFrontDuration) }, dodgeMotionPlans);
            return new[] { skill, ultimate, specialAttack, dodge };
        }

        /// <summary>
        /// 创建从行为 Tick 零开始的单动画表现变体。
        /// </summary>
        private static LegacyActionVariantPlan CreateSingleAnimationVariant(string variantId, AnimationReferenceAsset animation, int animationDurationTicks)
        {
            return new LegacyActionVariantPlan(variantId, new[] { new LegacyAnimationCuePlan("Animation", animation, 0, animationDurationTicks) });
        }

        /// <summary>
        /// 为单动画行为创建可选根运动计划；明确无 hips 平移轨道时不生成 MotionClip。
        /// </summary>
        private static LegacyActionMotionPlan[] BuildSingleAnimationMotionPlans(string motionId, string requiredVariantId, AnimationReferenceAsset animation, int behaviorDurationTicks, Action<string> reportWarning)
        {
            if (LegacySpineRootMotionBaker.TryBake(animation, behaviorDurationTicks, "hips", out Vector3[] samples)) return new[] { new LegacyActionMotionPlan(motionId, requiredVariantId, samples) };
            reportWarning?.Invoke($"Spine 动画 '{animation.name}' 没有 hips TranslateTimeline；行为 '{motionId}' 不生成 MotionClip。");
            return Array.Empty<LegacyActionMotionPlan>();
        }

        /// <summary>
        /// 将 Skill 两段连续动画的根运动按各自 Tick 偏移合并到同一行为绑定；两段都无轨道时不生成 MotionClip。
        /// </summary>
        private static LegacyActionMotionPlan[] BuildSequentialMotionPlans(string motionId, string requiredVariantId, AnimationReferenceAsset firstAnimation, int firstDurationTicks, AnimationReferenceAsset secondAnimation, int secondDurationTicks, Action<string> reportWarning)
        {
            bool hasFirst = LegacySpineRootMotionBaker.TryBake(firstAnimation, firstDurationTicks, "hips", out Vector3[] firstSamples);
            bool hasSecond = LegacySpineRootMotionBaker.TryBake(secondAnimation, secondDurationTicks, "hips", out Vector3[] secondSamples);
            if (!hasFirst && !hasSecond)
            {
                reportWarning?.Invoke($"Spine 动画 '{firstAnimation.name}' 与 '{secondAnimation.name}' 都没有 hips TranslateTimeline；行为 '{motionId}' 不生成 MotionClip。");
                return Array.Empty<LegacyActionMotionPlan>();
            }
            Vector3[] combined = new Vector3[firstDurationTicks + secondDurationTicks];
            for (int index = 0; index < firstSamples.Length; index++) combined[index] = firstSamples[index];
            for (int index = 0; index < secondSamples.Length; index++) combined[firstDurationTicks + index] = secondSamples[index];
            return new[] { new LegacyActionMotionPlan(motionId, requiredVariantId, combined) };
        }

        /// <summary>
        /// 合并两个互斥表现变体的根运动计划，并保持 Default 后、Moving 前的稳定顺序。
        /// </summary>
        private static LegacyActionMotionPlan[] CombineMotionPlans(LegacyActionMotionPlan[] first, LegacyActionMotionPlan[] second)
        {
            LegacyActionMotionPlan[] result = new LegacyActionMotionPlan[first.Length + second.Length];
            for (int index = 0; index < first.Length; index++) result[index] = first[index];
            for (int index = 0; index < second.Length; index++) result[first.Length + index] = second[index];
            return result;
        }
    }
}
