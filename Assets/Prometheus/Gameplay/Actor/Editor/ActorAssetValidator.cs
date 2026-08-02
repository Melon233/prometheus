using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Actor.Editor
{
    /// <summary>
    /// 为旧角色迁移工具提供只读全量校验入口；校验不会创建、保存或修改任何资产。
    /// </summary>
    public static partial class LegacyActorMigrationTool
    {
        /// <summary>
        /// 校验生成资产的稳定 ID、引用、行为时间轴、Prefab Hitbox/VFX 绑定与 Yefa 镜头目标。
        /// </summary>
        [MenuItem("Prometheus/Actor/Validate All")]
        public static void ValidateAll()
        {
            ActorMigrationValidationReport report = new ActorMigrationValidationReport();
            LegacyAttackPlan[] yefaPlans = null;
            LegacyAttackPlan[] slimePlans = null;
            LegacyActionPlan[] yefaActionPlans = null;
            report.Capture("读取 Yefa 旧内容", () => yefaPlans = LegacyAttackPlanBuilder.BuildYefa(LegacyActorSourceReader.LoadYefa(), report.Warning));
            report.Capture("读取 Slime 旧内容", () => slimePlans = LegacyAttackPlanBuilder.BuildSlime(LegacyActorSourceReader.LoadSlime(), report.Warning));
            report.Capture("读取 Yefa 旧主动行为", () => yefaActionPlans = LegacyYefaActionPlanBuilder.Build(LegacyActorSourceReader.LoadYefa(), report.Warning));
            CharacterControllerMotionModelDefinition motion = RequireAsset<CharacterControllerMotionModelDefinition>(LegacyActorMigrationPaths.DefaultMotionPath, report);
            CameraFollowProfile camera = RequireAsset<CameraFollowProfile>(LegacyActorMigrationPaths.DefaultCameraPath, report);
            ActorDefinition yefaDefinition = RequireAsset<ActorDefinition>(LegacyActorMigrationPaths.YefaDefinitionPath, report);
            ActorDefinition slimeDefinition = RequireAsset<ActorDefinition>(LegacyActorMigrationPaths.SlimeDefinitionPath, report);
            if (yefaPlans != null && yefaActionPlans != null && yefaDefinition != null) ValidateActorDefinition(yefaDefinition, "Yefa", GameplayObjectCategory.Character, ActorFaction.Player, motion, camera, yefaPlans, yefaActionPlans, LegacyActorMigrationPaths.GetYefaBehaviorPath, report);
            if (slimePlans != null && slimeDefinition != null) ValidateActorDefinition(slimeDefinition, "Slime", GameplayObjectCategory.Monster, ActorFaction.Enemy, motion, null, slimePlans, Array.Empty<LegacyActionPlan>(), LegacyActorMigrationPaths.GetSlimeBehaviorPath, report);
            if (yefaDefinition != null && yefaPlans != null && yefaActionPlans != null) ValidatePrefab(LegacyActorMigrationPaths.YefaPrefabPath, yefaDefinition, true, yefaPlans, yefaActionPlans, report);
            if (slimeDefinition != null && slimePlans != null) ValidatePrefab(LegacyActorMigrationPaths.SlimePrefabPath, slimeDefinition, false, slimePlans, Array.Empty<LegacyActionPlan>(), report);
            report.FlushAndThrowIfInvalid();
            Debug.Log("[Actor Validation] ValidateAll 通过：默认 Motion/Camera、Yefa/Slime Definition、Yefa 八个与 Slime 一个 Behavior、全部逐 Tick 根运动以及两个 Prefab 绑定均有效。");
        }

        /// <summary>
        /// 加载一个必需生成资产，并把缺失或类型错误记录为校验失败。
        /// </summary>
        private static TAsset RequireAsset<TAsset>(string assetPath, ActorMigrationValidationReport report) where TAsset : UnityEngine.Object
        {
            TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
            if (asset == null) report.Error($"缺少必需资产或资产类型不匹配：'{assetPath}'，期望类型 '{typeof(TAsset).Name}'。");
            return asset;
        }

        /// <summary>
        /// 校验角色定义、行为引用顺序及每个行为资产的权威与表现数据。
        /// </summary>
        private static void ValidateActorDefinition(ActorDefinition definition, string expectedActorId, GameplayObjectCategory expectedCategory, ActorFaction expectedFaction, CharacterControllerMotionModelDefinition expectedMotion, CameraFollowProfile expectedCamera, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans, Func<int, string> resolveBehaviorPath, ActorMigrationValidationReport report)
        {
            if (!string.Equals(definition.ActorId, expectedActorId, StringComparison.Ordinal)) report.Error($"ActorDefinition '{AssetDatabase.GetAssetPath(definition)}' 的 ActorId 应为 '{expectedActorId}'，实际为 '{definition.ActorId}'。");
            if (definition.Category != expectedCategory) report.Error($"Actor '{expectedActorId}' 的 Category 应为 '{expectedCategory}'，实际为 '{definition.Category}'。");
            if (definition.Faction != expectedFaction) report.Error($"Actor '{expectedActorId}' 的 Faction 应为 '{expectedFaction}'，实际为 '{definition.Faction}'。");
            if (definition.MotionModel != expectedMotion) report.Error($"Actor '{expectedActorId}' 未引用默认 CharacterController Motion 资产。");
            if (definition.CameraProfile != expectedCamera) report.Error($"Actor '{expectedActorId}' 的 CameraProfile 引用不符合迁移约定。");
            if (definition.LocomotionPresentation == null || definition.LocomotionPresentation.Idle == null || definition.LocomotionPresentation.Move == null) report.Error($"Actor '{expectedActorId}' 缺少必需 Idle 或 Move Spine 动画引用。");
            if (string.Equals(expectedActorId, "Yefa", StringComparison.Ordinal) && definition.HeldAttackSpecialTriggerTicks != 30) report.Error($"Actor 'Yefa' 的长按普攻特殊攻击阈值应为 30 Tick，实际为 {definition.HeldAttackSpecialTriggerTicks}。");
            report.Capture($"验证 ActorDefinition '{expectedActorId}'", definition.ValidateOrThrow);
            int expectedBehaviorCount = attackPlans.Count + actionPlans.Count;
            if (definition.Behaviors.Count != expectedBehaviorCount) report.Error($"Actor '{expectedActorId}' 应绑定 {expectedBehaviorCount} 个行为，实际为 {definition.Behaviors.Count} 个。");
            for (int index = 0; index < attackPlans.Count; index++)
            {
                ActorBehaviorDefinition expectedBehavior = RequireAsset<ActorBehaviorDefinition>(resolveBehaviorPath(index), report);
                if (index < definition.Behaviors.Count && definition.Behaviors[index] != expectedBehavior) report.Error($"Actor '{expectedActorId}' 的第 {index + 1} 个行为引用未指向 '{resolveBehaviorPath(index)}'。");
                if (expectedBehavior != null) ValidateBehavior(expectedBehavior, attackPlans[index], report);
            }
            for (int index = 0; index < actionPlans.Count; index++)
            {
                LegacyActionPlan plan = actionPlans[index];
                ActorBehaviorDefinition expectedBehavior = RequireAsset<ActorBehaviorDefinition>(plan.AssetPath, report);
                int behaviorIndex = attackPlans.Count + index;
                if (behaviorIndex < definition.Behaviors.Count && definition.Behaviors[behaviorIndex] != expectedBehavior) report.Error($"Actor '{expectedActorId}' 的第 {behaviorIndex + 1} 个行为引用未指向 '{plan.AssetPath}'。");
                if (expectedBehavior != null) ValidateActionBehavior(expectedBehavior, plan, report);
            }
            ValidateMotionBindings(definition, attackPlans, actionPlans, report);
        }

        /// <summary>
        /// 校验一个普通攻击行为的稳定 ID、60 Hz 半开窗口、命中信号和表现变体引用。
        /// </summary>
        private static void ValidateBehavior(ActorBehaviorDefinition behavior, LegacyAttackPlan plan, ActorMigrationValidationReport report)
        {
            string assetPath = AssetDatabase.GetAssetPath(behavior);
            if (!string.Equals(behavior.BehaviorId, plan.BehaviorId, StringComparison.Ordinal)) report.Error($"行为 '{assetPath}' 的 BehaviorId 应为 '{plan.BehaviorId}'，实际为 '{behavior.BehaviorId}'。");
            if (behavior.Command != ActorBehaviorCommand.BasicAttack || behavior.CommandIndex != plan.CommandIndex) report.Error($"行为 '{plan.BehaviorId}' 的命令或连段序号无效。");
            if (behavior.DurationTicks != plan.DurationTicks || behavior.ChainFromTick != plan.ChainFromTick) report.Error($"行为 '{plan.BehaviorId}' 的 Duration/Chain 应为 {plan.DurationTicks}/{plan.ChainFromTick}，实际为 {behavior.DurationTicks}/{behavior.ChainFromTick}。");
            if (behavior.HitSignal == null || !string.Equals(behavior.HitSignal.SignalId, plan.BehaviorId, StringComparison.Ordinal) || behavior.HitSignal.Tags != (EffectTag.Attack | EffectTag.NormalAttack) || behavior.HitSignal.TargetFactions != plan.TargetFactions) report.Error($"行为 '{plan.BehaviorId}' 的命中 SignalId、普通攻击标签或目标阵营无效。");
            int expectedClipCount = 1 + (plan.HasMotion ? 1 : 0) + (plan.HasMovingVariant ? 1 : 0);
            if (behavior.SimulationClips.Count != expectedClipCount) report.Error($"行为 '{plan.BehaviorId}' 应包含 {expectedClipCount} 个迁移生成的模拟片段，实际为 {behavior.SimulationClips.Count} 个。");
            if (behavior.SimulationClips.Count > 0)
            {
                ActorSimulationClipDefinition clip = behavior.SimulationClips[0];
                if (clip == null || clip.Kind != SimulationClipKind.HitWindow || clip.StartTick != plan.HitStartTick || clip.EndTick != plan.HitEndTick || !string.Equals(clip.BindingId, LegacyActorMigrationPaths.HitboxBindingId, StringComparison.Ordinal)) report.Error($"行为 '{plan.BehaviorId}' 的 HitWindow 应为 [{plan.HitStartTick},{plan.HitEndTick}) 并绑定 '{LegacyActorMigrationPaths.HitboxBindingId}'。");
            }
            if (plan.HasMotion && behavior.SimulationClips.Count > 1)
            {
                ActorSimulationClipDefinition motionClip = behavior.SimulationClips[1];
                if (motionClip == null || motionClip.Kind != SimulationClipKind.Motion || motionClip.StartTick != 0 || motionClip.EndTick != plan.DurationTicks || !string.Equals(motionClip.BindingId, plan.MotionBindingId, StringComparison.Ordinal)) report.Error($"行为 '{plan.BehaviorId}' 的 MotionClip 应为 [0,{plan.DurationTicks}) 并绑定 '{plan.MotionBindingId}'。");
            }
            if (plan.HasMovingVariant && behavior.SimulationClips.Count == expectedClipCount)
            {
                ActorSimulationClipDefinition blockClip = behavior.SimulationClips[expectedClipCount - 1];
                if (blockClip == null || blockClip.Kind != SimulationClipKind.CapabilityBlock || blockClip.StartTick != 0 || blockClip.EndTick != plan.DurationTicks || blockClip.BlockedCapabilities != GetExpectedCharacterControlBlock()) report.Error($"行为 '{plan.BehaviorId}' 必须在 [0,{plan.DurationTicks}) 阻塞 Move、Rotate、Jump 与 Dodge，并保留 Input 以接受连击输入。");
            }
            report.Capture($"编译行为 '{plan.BehaviorId}'", () => behavior.BuildProgram());
            ValidateVariant(behavior, "Default", plan.DefaultAnimation, plan.DefaultAnimationEndTick, plan, report);
            if (plan.HasMovingVariant) ValidateVariant(behavior, "Moving", plan.MovingAnimation, plan.MovingAnimationEndTick, plan, report);
            else if (behavior.TryGetPresentationVariant("Moving", out _)) report.Error($"行为 '{plan.BehaviorId}' 不应包含 Moving 表现变体。");
            int expectedVariantCount = plan.HasMovingVariant ? 2 : 1;
            if (behavior.PresentationVariants.Count != expectedVariantCount) report.Error($"行为 '{plan.BehaviorId}' 应包含 {expectedVariantCount} 个表现变体，实际为 {behavior.PresentationVariants.Count} 个。");
        }

        /// <summary>
        /// 校验 Yefa 主动行为的命令、非普通攻击 EffectTag、目标阵营、可选 HitWindow 和全部表现 Cue。
        /// </summary>
        private static void ValidateActionBehavior(ActorBehaviorDefinition behavior, LegacyActionPlan plan, ActorMigrationValidationReport report)
        {
            string assetPath = AssetDatabase.GetAssetPath(behavior);
            if (!string.Equals(behavior.BehaviorId, plan.BehaviorId, StringComparison.Ordinal)) report.Error($"行为 '{assetPath}' 的 BehaviorId 应为 '{plan.BehaviorId}'，实际为 '{behavior.BehaviorId}'。");
            if (behavior.Command != plan.Command || behavior.CommandIndex != 0) report.Error($"行为 '{plan.BehaviorId}' 的命令应为 '{plan.Command}' 且 CommandIndex 应为零。");
            if (behavior.DurationTicks != plan.DurationTicks || behavior.ChainFromTick != plan.ChainFromTick) report.Error($"行为 '{plan.BehaviorId}' 的 Duration/Chain 应为 {plan.DurationTicks}/{plan.ChainFromTick}，实际为 {behavior.DurationTicks}/{behavior.ChainFromTick}。");
            if (behavior.HitSignal == null || !string.Equals(behavior.HitSignal.SignalId, plan.BehaviorId, StringComparison.Ordinal) || behavior.HitSignal.Tags != plan.EffectTags || behavior.HitSignal.TargetFactions != plan.TargetFactions) report.Error($"行为 '{plan.BehaviorId}' 的 SignalId、EffectTag 或目标阵营与迁移计划不一致。");
            if ((plan.EffectTags & EffectTag.NormalAttack) != EffectTag.None) report.Error($"行为 '{plan.BehaviorId}' 的迁移计划错误包含 NormalAttack 标签。");
            int expectedClipCount = (plan.HasHitWindow ? 1 : 0) + plan.MotionPlans.Length + 1;
            if (behavior.SimulationClips.Count != expectedClipCount) report.Error($"行为 '{plan.BehaviorId}' 应包含 {expectedClipCount} 个模拟片段，实际为 {behavior.SimulationClips.Count} 个。");
            if (plan.HasHitWindow && behavior.SimulationClips.Count > 0)
            {
                ActorSimulationClipDefinition clip = behavior.SimulationClips[0];
                if (clip == null || clip.Kind != SimulationClipKind.HitWindow || clip.StartTick != plan.HitStartTick || clip.EndTick != plan.HitEndTick || !string.Equals(clip.BindingId, plan.HitboxBindingId, StringComparison.Ordinal)) report.Error($"行为 '{plan.BehaviorId}' 的 HitWindow 应为 [{plan.HitStartTick},{plan.HitEndTick}) 并绑定 '{plan.HitboxBindingId}'。");
            }
            int firstMotionIndex = plan.HasHitWindow ? 1 : 0;
            for (int motionIndex = 0; motionIndex < plan.MotionPlans.Length; motionIndex++)
            {
                if (firstMotionIndex + motionIndex >= behavior.SimulationClips.Count) break;
                LegacyActionMotionPlan motionPlan = plan.MotionPlans[motionIndex];
                ActorSimulationClipDefinition motionClip = behavior.SimulationClips[firstMotionIndex + motionIndex];
                if (motionClip == null || motionClip.Kind != SimulationClipKind.Motion || motionClip.StartTick != 0 || motionClip.EndTick != plan.DurationTicks || !string.Equals(motionClip.BindingId, motionPlan.MotionId, StringComparison.Ordinal)) report.Error($"行为 '{plan.BehaviorId}' 的 MotionClip '{motionPlan.MotionId}' 应覆盖 [0,{plan.DurationTicks})。");
            }
            if (behavior.SimulationClips.Count == expectedClipCount)
            {
                ActorSimulationClipDefinition blockClip = behavior.SimulationClips[expectedClipCount - 1];
                if (blockClip == null || blockClip.Kind != SimulationClipKind.CapabilityBlock || blockClip.StartTick != 0 || blockClip.EndTick != plan.DurationTicks || blockClip.BlockedCapabilities != GetExpectedCharacterControlBlock()) report.Error($"行为 '{plan.BehaviorId}' 必须在完整时长内阻塞 Move、Rotate、Jump 与 Dodge，同时保留 Input。");
            }
            report.Capture($"编译行为 '{plan.BehaviorId}'", () => behavior.BuildProgram());
            if (behavior.PresentationVariants.Count != plan.Variants.Length) report.Error($"行为 '{plan.BehaviorId}' 应包含 {plan.Variants.Length} 个表现变体，实际为 {behavior.PresentationVariants.Count} 个。");
            for (int variantIndex = 0; variantIndex < plan.Variants.Length; variantIndex++) ValidateActionVariant(behavior, plan, plan.Variants[variantIndex], report);
        }

        /// <summary>
        /// 校验主动行为单个变体的 Spine 序列、音效和 VFX 引用及其真实触发 Tick。
        /// </summary>
        private static void ValidateActionVariant(ActorBehaviorDefinition behavior, LegacyActionPlan plan, LegacyActionVariantPlan expectedVariant, ActorMigrationValidationReport report)
        {
            if (!behavior.TryGetPresentationVariant(expectedVariant.VariantId, out ActorPresentationVariantDefinition variant))
            {
                report.Error($"行为 '{plan.BehaviorId}' 缺少表现变体 '{expectedVariant.VariantId}'。");
                return;
            }
            int expectedCueCount = expectedVariant.AnimationCues.Length + (plan.HasAudioCue ? 1 : 0) + (plan.HasVfxCue ? 1 : 0);
            if (variant.Cues.Count != expectedCueCount) report.Error($"行为 '{plan.BehaviorId}' 变体 '{expectedVariant.VariantId}' 应包含 {expectedCueCount} 个 Cue，实际为 {variant.Cues.Count} 个。");
            for (int cueIndex = 0; cueIndex < expectedVariant.AnimationCues.Length; cueIndex++)
            {
                LegacyAnimationCuePlan expectedCue = expectedVariant.AnimationCues[cueIndex];
                ActorPresentationCueDefinition cue = FindCue(variant, expectedCue.CueId);
                if (cue == null || cue.Kind != ActorPresentationCueKind.SpineAnimation || cue.Animation != expectedCue.Animation || cue.StartTick != expectedCue.StartTick || cue.EndTick != expectedCue.EndTick) report.Error($"行为 '{plan.BehaviorId}' 变体 '{expectedVariant.VariantId}' 的 Spine Cue '{expectedCue.CueId}' 与迁移计划不一致。");
            }
            ActorPresentationCueDefinition audioCue = FindCue(variant, "Audio");
            if (plan.HasAudioCue && (audioCue == null || audioCue.Kind != ActorPresentationCueKind.Audio || audioCue.AudioClip != plan.AudioClip || audioCue.StartTick != plan.AudioTick)) report.Error($"行为 '{plan.BehaviorId}' 变体 '{expectedVariant.VariantId}' 缺少在 Tick {plan.AudioTick} 触发的旧音效 Cue。");
            if (!plan.HasAudioCue && audioCue != null) report.Error($"行为 '{plan.BehaviorId}' 变体 '{expectedVariant.VariantId}' 不应包含 Audio Cue。");
            ActorPresentationCueDefinition vfxCue = FindCue(variant, "Vfx");
            if (plan.HasVfxCue && (vfxCue == null || vfxCue.Kind != ActorPresentationCueKind.Vfx || !string.Equals(vfxCue.BindingId, plan.VfxBindingId, StringComparison.Ordinal) || vfxCue.StartTick != plan.VfxTick)) report.Error($"行为 '{plan.BehaviorId}' 变体 '{expectedVariant.VariantId}' 缺少在 Tick {plan.VfxTick} 触发的 VFX Cue '{plan.VfxBindingId}'。");
            if (!plan.HasVfxCue && vfxCue != null) report.Error($"行为 '{plan.BehaviorId}' 变体 '{expectedVariant.VariantId}' 不应包含 VFX Cue。");
        }

        /// <summary>
        /// 按稳定 CueId 查找表现 Cue，找不到时返回空引用。
        /// </summary>
        private static ActorPresentationCueDefinition FindCue(ActorPresentationVariantDefinition variant, string cueId)
        {
            for (int index = 0; index < variant.Cues.Count; index++)
            {
                ActorPresentationCueDefinition cue = variant.Cues[index];
                if (cue != null && string.Equals(cue.CueId, cueId, StringComparison.Ordinal)) return cue;
            }
            return null;
        }

        /// <summary>
        /// 校验 ActorDefinition 中所有 Moving 根运动绑定的变体约束、样本数量和每 Tick 位移值。
        /// </summary>
        private static void ValidateMotionBindings(ActorDefinition definition, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans, ActorMigrationValidationReport report)
        {
            int expectedCount = 0;
            for (int index = 0; index < attackPlans.Count; index++) if (attackPlans[index].HasMotion) expectedCount++;
            for (int index = 0; index < actionPlans.Count; index++) expectedCount += actionPlans[index].MotionPlans.Length;
            if (definition.MotionBindings.Count != expectedCount) report.Error($"Actor '{definition.ActorId}' 应包含 {expectedCount} 个根运动绑定，实际为 {definition.MotionBindings.Count} 个。");
            for (int planIndex = 0; planIndex < attackPlans.Count; planIndex++)
            {
                LegacyAttackPlan plan = attackPlans[planIndex];
                if (!plan.HasMotion) continue;
                if (!definition.TryGetMotionBinding(plan.MotionBindingId, out ActorMotionBindingDefinition binding))
                {
                    report.Error($"Actor '{definition.ActorId}' 缺少根运动绑定 '{plan.MotionBindingId}'。");
                    continue;
                }
                if (!string.Equals(binding.RequiredVariantId, "Moving", StringComparison.Ordinal)) report.Error($"根运动绑定 '{plan.MotionBindingId}' 必须仅允许 Moving 变体。");
                if (binding.BakedDisplacementCount != plan.DurationTicks) report.Error($"根运动绑定 '{plan.MotionBindingId}' 应包含 {plan.DurationTicks} 个逐 Tick 样本，实际为 {binding.BakedDisplacementCount} 个。");
                int comparableCount = Mathf.Min(binding.BakedDisplacementCount, plan.BakedMotionSamples == null ? 0 : plan.BakedMotionSamples.Length);
                for (int tick = 0; tick < comparableCount; tick++) if ((binding.GetLocalDisplacement(tick) - plan.BakedMotionSamples[tick]).sqrMagnitude > 0.0000000001f) report.Error($"根运动绑定 '{plan.MotionBindingId}' 的 Tick {tick} 位移与 Spine 烘焙值不一致。");
            }
            for (int planIndex = 0; planIndex < actionPlans.Count; planIndex++)
            {
                LegacyActionPlan actionPlan = actionPlans[planIndex];
                for (int motionIndex = 0; motionIndex < actionPlan.MotionPlans.Length; motionIndex++)
                {
                    LegacyActionMotionPlan motionPlan = actionPlan.MotionPlans[motionIndex];
                    if (!definition.TryGetMotionBinding(motionPlan.MotionId, out ActorMotionBindingDefinition binding))
                    {
                        report.Error($"Actor '{definition.ActorId}' 缺少行为根运动绑定 '{motionPlan.MotionId}'。");
                        continue;
                    }
                    if (!string.Equals(binding.RequiredVariantId, motionPlan.RequiredVariantId, StringComparison.Ordinal)) report.Error($"根运动绑定 '{motionPlan.MotionId}' 的 RequiredVariantId 应为 '{motionPlan.RequiredVariantId}'。");
                    if (binding.BakedDisplacementCount != actionPlan.DurationTicks) report.Error($"根运动绑定 '{motionPlan.MotionId}' 应包含 {actionPlan.DurationTicks} 个逐 Tick 样本，实际为 {binding.BakedDisplacementCount} 个。");
                    int comparableCount = Mathf.Min(binding.BakedDisplacementCount, motionPlan.BakedMotionSamples.Length);
                    for (int tick = 0; tick < comparableCount; tick++) if ((binding.GetLocalDisplacement(tick) - motionPlan.BakedMotionSamples[tick]).sqrMagnitude > 0.0000000001f) report.Error($"根运动绑定 '{motionPlan.MotionId}' 的 Tick {tick} 位移与 Spine 烘焙值不一致。");
                }
            }
        }

        /// <summary>
        /// 返回旧 MotionBlockerStart 对应的控制阻塞集合；Input 不在其中，以便连击和长按特殊攻击仍能被采样。
        /// </summary>
        private static ActorCapability GetExpectedCharacterControlBlock()
        {
            return ActorCapability.Move | ActorCapability.Rotate | ActorCapability.Jump | ActorCapability.Dodge;
        }

        /// <summary>
        /// 校验表现变体内动画、音效与可选 VFX Cue 的引用、ID 和触发 Tick。
        /// </summary>
        private static void ValidateVariant(ActorBehaviorDefinition behavior, string variantId, UnityEngine.Object expectedAnimation, int expectedAnimationEndTick, LegacyAttackPlan plan, ActorMigrationValidationReport report)
        {
            if (!behavior.TryGetPresentationVariant(variantId, out ActorPresentationVariantDefinition variant))
            {
                report.Error($"行为 '{plan.BehaviorId}' 缺少必需表现变体 '{variantId}'。");
                return;
            }
            HashSet<string> cueIds = new HashSet<string>(StringComparer.Ordinal);
            bool foundAnimation = false;
            bool foundAudio = false;
            bool foundVfx = false;
            for (int index = 0; index < variant.Cues.Count; index++)
            {
                ActorPresentationCueDefinition cue = variant.Cues[index];
                if (cue == null)
                {
                    report.Error($"行为 '{plan.BehaviorId}' 的变体 '{variantId}' 包含空 Cue。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(cue.CueId) || !cueIds.Add(cue.CueId)) report.Error($"行为 '{plan.BehaviorId}' 的变体 '{variantId}' 包含空或重复 CueId '{cue.CueId}'。");
                if (cue.StartTick < 0 || cue.StartTick > plan.DurationTicks || cue.EndTick < 0 || cue.EndTick > plan.DurationTicks) report.Error($"行为 '{plan.BehaviorId}' 的 Cue '{cue.CueId}' 超出行为时长。");
                if (cue.Kind == ActorPresentationCueKind.SpineAnimation && cue.Animation == expectedAnimation && cue.StartTick == 0 && cue.EndTick == expectedAnimationEndTick) foundAnimation = true;
                if (cue.Kind == ActorPresentationCueKind.Audio && cue.AudioClip == plan.AudioClip && cue.StartTick == plan.HitStartTick) foundAudio = true;
                if (cue.Kind == ActorPresentationCueKind.Vfx && string.Equals(cue.BindingId, plan.VfxBindingId, StringComparison.Ordinal) && cue.StartTick == plan.HitStartTick) foundVfx = true;
            }
            if (!foundAnimation) report.Error($"行为 '{plan.BehaviorId}' 的变体 '{variantId}' 缺少正确动画 Cue 或动画结束 Tick。");
            if (!foundAudio) report.Error($"行为 '{plan.BehaviorId}' 的变体 '{variantId}' 缺少在 hit_start 触发的旧音效 Cue。");
            if (plan.HasVfxCue && !foundVfx) report.Error($"行为 '{plan.BehaviorId}' 的变体 '{variantId}' 缺少在 hit_start 触发的 VFX Cue。");
        }

        /// <summary>
        /// 校验 Prefab 根节点 Authoring、CameraSubject、显式 FacingRoot、原始攻击 Collider 和所有行为 VFX 绑定。
        /// </summary>
        private static void ValidatePrefab(string prefabPath, ActorDefinition expectedDefinition, bool requiresCameraSubject, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans, ActorMigrationValidationReport report)
        {
            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
            {
                report.Error($"缺少角色 Prefab '{prefabPath}'。");
                return;
            }
            if (root.GetComponentInChildren<CharacterController>(true) == null) report.Error($"Prefab '{prefabPath}' 缺少默认 Motion 所需 CharacterController。");
            AttackComponent attackComponent = root.GetComponentInChildren<AttackComponent>(true);
            Collider expectedShape = attackComponent == null || attackComponent.atkCollider == null ? null : ResolveQueryShapeReadOnly(attackComponent.atkCollider, ActorBehaviorCommand.BasicAttack, prefabPath, report);
            if (expectedShape == null) report.Error($"Prefab '{prefabPath}' 无法解析旧攻击 Collider 查询形状。");
            SpineComponent spineComponent = root.GetComponentInChildren<SpineComponent>(true);
            Transform expectedFacingRoot = spineComponent == null ? null : spineComponent.rotateRoot;
            if (expectedFacingRoot == null) report.Error($"Prefab '{prefabPath}' 缺少显式朝向根 SpineComponent.rotateRoot。");
            ActorAuthoringComponent authoring = root.GetComponent<ActorAuthoringComponent>();
            if (authoring == null)
            {
                report.Error($"Prefab '{prefabPath}' 根节点缺少 ActorAuthoringComponent。");
                return;
            }
            if (authoring.Definition != expectedDefinition) report.Error($"Prefab '{prefabPath}' 的 ActorDefinition 引用错误。");
            report.Capture($"验证 Prefab '{prefabPath}' Authoring", authoring.ValidateOrThrow);
            SerializedObject serializedAuthoring = new SerializedObject(authoring);
            if (authoring.FacingRoot != expectedFacingRoot) report.Error($"Prefab '{prefabPath}' 的 ActorAuthoringComponent 未显式绑定 SpineComponent.rotateRoot。");
            if (spineComponent != null && Quaternion.Angle(authoring.RightFacingRootLocalRotation, spineComponent.FacingRootRightLocalRotation) > 0.001f) report.Error($"Prefab '{prefabPath}' 的 FacingRoot 右朝向旋转基准必须匹配 SpineComponent 捕获的局部基准。");
            UnityEngine.Object boundShape = FindBindingReference(serializedAuthoring.FindProperty("hitboxes"), LegacyActorMigrationPaths.HitboxBindingId, "bindingId", "shape", report, prefabPath);
            if (boundShape != expectedShape) report.Error($"Prefab '{prefabPath}' 的 '{LegacyActorMigrationPaths.HitboxBindingId}' Hitbox 未绑定现有攻击 Collider。");
            ValidateHitboxFacingRule(serializedAuthoring.FindProperty("hitboxes"), LegacyActorMigrationPaths.HitboxBindingId, ResolveExpectedFacingRule(expectedShape, expectedFacingRoot), report, prefabPath);
            for (int actionIndex = 0; actionIndex < actionPlans.Count; actionIndex++)
            {
                LegacyActionPlan actionPlan = actionPlans[actionIndex];
                if (!actionPlan.HasHitWindow) continue;
                Collider expectedActionShape = ResolveActionQueryShapeReadOnly(root, actionPlan.Command, prefabPath, report);
                if (expectedActionShape == null) report.Error($"Prefab '{prefabPath}' 无法解析行为 '{actionPlan.BehaviorId}' 的原始 Collider 查询形状。");
                UnityEngine.Object boundActionShape = FindBindingReference(serializedAuthoring.FindProperty("hitboxes"), actionPlan.HitboxBindingId, "bindingId", "shape", report, prefabPath);
                if (boundActionShape != expectedActionShape) report.Error($"Prefab '{prefabPath}' 的 Hitbox '{actionPlan.HitboxBindingId}' 未绑定旧行为 ColliderProxy 对应的原始 Collider。");
                ValidateHitboxFacingRule(serializedAuthoring.FindProperty("hitboxes"), actionPlan.HitboxBindingId, ResolveExpectedFacingRule(expectedActionShape, expectedFacingRoot), report, prefabPath);
            }
            if (requiresCameraSubject)
            {
                CameraSubject cameraSubject = root.GetComponent<CameraSubject>();
                if (cameraSubject == null) report.Error($"Prefab '{prefabPath}' 根节点缺少 CameraSubject。");
                if (serializedAuthoring.FindProperty("cameraSubject").objectReferenceValue != cameraSubject) report.Error($"Prefab '{prefabPath}' 的 ActorAuthoringComponent 未显式绑定根节点 CameraSubject。");
            }
            ValidatePrefabVfxBindings(root, serializedAuthoring.FindProperty("vfxBindings"), attackPlans, actionPlans, report, prefabPath);
        }

        /// <summary>
        /// 只读解析行为旧 ColliderProxy 上唯一的 Box、Sphere 或 Capsule 原始形状，不创建或修改任何组件。
        /// </summary>
        private static Collider ResolveActionQueryShapeReadOnly(GameObject root, ActorBehaviorCommand command, string prefabPath, ActorMigrationValidationReport report)
        {
            ColliderProxy proxy = null;
            if (command == ActorBehaviorCommand.Skill)
            {
                SkillComponent component = root.GetComponentInChildren<SkillComponent>(true);
                proxy = component == null ? null : component.colliderProxy;
            }
            else if (command == ActorBehaviorCommand.Ultimate)
            {
                UltimateComponent component = root.GetComponentInChildren<UltimateComponent>(true);
                proxy = component == null ? null : component.colliderProxy;
            }
            else if (command == ActorBehaviorCommand.SpecialAttack)
            {
                SpecialAttackComponent component = root.GetComponentInChildren<SpecialAttackComponent>(true);
                proxy = component == null ? null : component.colliderProxy;
            }
            if (proxy == null)
            {
                report.Error($"Prefab '{prefabPath}' 缺少命令 '{command}' 对应的旧 ColliderProxy。");
                return null;
            }
            return ResolveQueryShapeReadOnly(proxy, command, prefabPath, report);
        }

        /// <summary>只读解析 ColliderProxy 的唯一支持形状，并把多形状或不受支持的配置作为迁移前置错误报告。</summary>
        private static Collider ResolveQueryShapeReadOnly(ColliderProxy proxy, ActorBehaviorCommand command, string prefabPath, ActorMigrationValidationReport report)
        {
            Collider[] allColliders = proxy.GetComponents<Collider>();
            Collider result = null;
            int supportedCount = 0;
            for (int index = 0; index < allColliders.Length; index++)
            {
                Collider candidate = allColliders[index];
                if (!(candidate is BoxCollider) && !(candidate is SphereCollider) && !(candidate is CapsuleCollider)) continue;
                supportedCount++;
                result = candidate;
            }
            if (allColliders.Length != 1 || supportedCount != 1)
            {
                report.Error($"Prefab '{prefabPath}' 的命令 '{command}' ColliderProxy 必须只包含一个 BoxCollider、SphereCollider 或 CapsuleCollider，当前 Collider 数量为 {allColliders.Length}，受支持形状数量为 {supportedCount}。");
                return null;
            }
            return result;
        }

        /// <summary>根据 Collider 是否已经继承 FacingRoot 层级计算迁移后应序列化的显式朝向规则。</summary>
        private static ActorHitboxFacingRule ResolveExpectedFacingRule(Collider shape, Transform facingRoot)
        {
            return shape != null && facingRoot != null && (shape.transform == facingRoot || shape.transform.IsChildOf(facingRoot)) ? ActorHitboxFacingRule.ShapeTransform : ActorHitboxFacingRule.MirrorWithFacingRoot;
        }

        /// <summary>只读校验指定 Hitbox 绑定的朝向枚举，使根直系 Ultimate 与 SpecialAttack 形状不会漏掉 FacingRoot 镜像。</summary>
        private static void ValidateHitboxFacingRule(SerializedProperty bindings, string bindingId, ActorHitboxFacingRule expectedRule, ActorMigrationValidationReport report, string prefabPath)
        {
            for (int index = 0; index < bindings.arraySize; index++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                if (!string.Equals(binding.FindPropertyRelative("bindingId").stringValue, bindingId, StringComparison.Ordinal)) continue;
                ActorHitboxFacingRule actualRule = (ActorHitboxFacingRule)binding.FindPropertyRelative("facingRule").enumValueIndex;
                if (actualRule != expectedRule) report.Error($"Prefab '{prefabPath}' 的 Hitbox '{bindingId}' 朝向规则应为 '{expectedRule}'，实际为 '{actualRule}'。");
                return;
            }
        }

        /// <summary>
        /// 校验每个 Yefa VFX Cue 都映射到旧 VfxComponent 对应的普通攻击表现根对象。
        /// </summary>
        private static void ValidatePrefabVfxBindings(GameObject root, SerializedProperty bindings, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans, ActorMigrationValidationReport report, string prefabPath)
        {
            global::Xuan.Prometheus.VfxComponent vfxComponent = root.GetComponentInChildren<global::Xuan.Prometheus.VfxComponent>(true);
            for (int index = 0; index < attackPlans.Count; index++)
            {
                LegacyAttackPlan plan = attackPlans[index];
                if (!plan.HasVfxCue) continue;
                ValidatePrefabVfxBinding(vfxComponent, bindings, plan.VfxBindingId, plan.VfxSlotIndex, report, prefabPath);
            }
            for (int index = 0; index < actionPlans.Count; index++)
            {
                LegacyActionPlan plan = actionPlans[index];
                if (!plan.HasVfxCue) continue;
                ValidatePrefabVfxBinding(vfxComponent, bindings, plan.VfxBindingId, plan.VfxSlotIndex, report, prefabPath);
            }
        }

        /// <summary>
        /// 校验一个行为 VFX 绑定指向旧 Executor 枚举值指定的真实 vfxSlots 根对象。
        /// </summary>
        private static void ValidatePrefabVfxBinding(global::Xuan.Prometheus.VfxComponent vfxComponent, SerializedProperty bindings, string bindingId, int slotIndex, ActorMigrationValidationReport report, string prefabPath)
        {
            GameObject expectedRoot = vfxComponent != null && vfxComponent.vfxSlots != null && slotIndex >= 0 && slotIndex < vfxComponent.vfxSlots.Count ? vfxComponent.vfxSlots[slotIndex] : null;
            if (expectedRoot == null)
            {
                report.Error($"Prefab '{prefabPath}' 缺少行为 VFX '{bindingId}' 请求的旧槽位 {slotIndex}。");
                return;
            }
            UnityEngine.Object boundRoot = FindBindingReference(bindings, bindingId, "bindingId", "visualRoot", report, prefabPath);
            if (boundRoot != expectedRoot) report.Error($"Prefab '{prefabPath}' 的 VFX 绑定 '{bindingId}' 未指向旧槽位 {slotIndex} 的根对象。");
        }

        /// <summary>
        /// 按稳定 ID 查找内联 Prefab 绑定并检测空 ID、重复 ID 和空引用。
        /// </summary>
        private static UnityEngine.Object FindBindingReference(SerializedProperty bindings, string expectedId, string idFieldName, string referenceFieldName, ActorMigrationValidationReport report, string prefabPath)
        {
            UnityEngine.Object result = null;
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < bindings.arraySize; index++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(index);
                string bindingId = binding.FindPropertyRelative(idFieldName).stringValue;
                if (string.IsNullOrWhiteSpace(bindingId) || !ids.Add(bindingId)) report.Error($"Prefab '{prefabPath}' 包含空或重复绑定 ID '{bindingId}'。");
                if (!string.Equals(bindingId, expectedId, StringComparison.Ordinal)) continue;
                result = binding.FindPropertyRelative(referenceFieldName).objectReferenceValue;
            }
            if (result == null) report.Error($"Prefab '{prefabPath}' 缺少有效绑定 '{expectedId}'。");
            return result;
        }
    }

    /// <summary>
    /// 汇总只读资产校验期间的错误和遗留警告，确保一次执行能够报告全部可检查问题。
    /// </summary>
    internal sealed class ActorMigrationValidationReport
    {
        private readonly List<string> errors = new List<string>();
        private readonly List<string> warnings = new List<string>();

        /// <summary>
        /// 记录一个校验错误。
        /// </summary>
        internal void Error(string message)
        {
            errors.Add(message);
        }

        /// <summary>
        /// 记录一个不会阻止资产使用的遗留内容警告。
        /// </summary>
        internal void Warning(string message)
        {
            warnings.Add(message);
        }

        /// <summary>
        /// 执行一个校验步骤并把异常转化为带上下文的错误，从而继续检查其他资产。
        /// </summary>
        internal void Capture(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                errors.Add(context + "失败：" + exception.Message);
            }
        }

        /// <summary>
        /// 输出全部警告和错误，并在存在错误时抛出统一异常使自动化流程失败。
        /// </summary>
        internal void FlushAndThrowIfInvalid()
        {
            for (int index = 0; index < warnings.Count; index++) Debug.LogWarning("[Actor Validation] " + warnings[index]);
            for (int index = 0; index < errors.Count; index++) Debug.LogError("[Actor Validation] " + errors[index]);
            if (errors.Count > 0) throw new InvalidOperationException($"Actor ValidateAll 发现 {errors.Count} 个错误和 {warnings.Count} 个警告；请查看 Console 获取完整列表。");
        }
    }
}
