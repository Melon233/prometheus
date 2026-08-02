using System;
using System.Collections.Generic;
using System.IO;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic.Talent;

namespace Xuan.Prometheus.Actor.Editor
{
    /// <summary>
    /// 将旧 Yefa 与 Slime 动画库、Prefab 绑定迁移为可复用 Actor 资产；所有写入均按稳定路径覆盖配置，因此重复执行不会创建重复资产或组件。
    /// </summary>
    public static partial class LegacyActorMigrationTool
    {
        /// <summary>
        /// 创建或更新默认运动、镜头、角色定义与五个普通攻击行为，并幂等接入现有 Yefa 和 Slime Prefab。
        /// </summary>
        [MenuItem("Prometheus/Actor/Migrate Legacy Yefa And Slime")]
        public static void MigrateLegacyYefaAndSlime()
        {
            try
            {
                EnsureFolder(LegacyActorMigrationPaths.OutputFolder);
                LegacyActorSource yefaSource = LegacyActorSourceReader.LoadYefa();
                LegacyActorSource slimeSource = LegacyActorSourceReader.LoadSlime();
                Action<string> reportWarning = message => Debug.LogWarning("[Actor Migration] " + message);
                LegacyAttackPlan[] yefaPlans = LegacyAttackPlanBuilder.BuildYefa(yefaSource, reportWarning);
                LegacyAttackPlan[] slimePlans = LegacyAttackPlanBuilder.BuildSlime(slimeSource, reportWarning);
                LegacyActionPlan[] yefaActionPlans = LegacyYefaActionPlanBuilder.Build(yefaSource, reportWarning);
                CharacterControllerMotionModelDefinition motion = LoadOrCreate<CharacterControllerMotionModelDefinition>(LegacyActorMigrationPaths.DefaultMotionPath);
                CameraFollowProfile camera = LoadOrCreate<CameraFollowProfile>(LegacyActorMigrationPaths.DefaultCameraPath);
                ActorBehaviorDefinition[] yefaBasicAttackBehaviors = CreateOrUpdateBehaviors(yefaPlans, LegacyActorMigrationPaths.GetYefaBehaviorPath);
                ActorBehaviorDefinition[] yefaActionBehaviors = CreateOrUpdateActionBehaviors(yefaActionPlans);
                ActorBehaviorDefinition[] yefaBehaviors = CombineBehaviors(yefaBasicAttackBehaviors, yefaActionBehaviors);
                ActorBehaviorDefinition[] slimeBehaviors = CreateOrUpdateBehaviors(slimePlans, LegacyActorMigrationPaths.GetSlimeBehaviorPath);
                ActorDefinition yefaDefinition = LoadOrCreate<ActorDefinition>(LegacyActorMigrationPaths.YefaDefinitionPath);
                ActorDefinition slimeDefinition = LoadOrCreate<ActorDefinition>(LegacyActorMigrationPaths.SlimeDefinitionPath);
                ConfigureActorDefinition(yefaDefinition, "Yefa", GameplayObjectCategory.Character, ActorFaction.Player, ActorCapability.All, motion, camera, yefaSource, true, 5f, 8f, yefaBehaviors, yefaPlans, yefaActionPlans);
                ConfigureActorDefinition(slimeDefinition, "Slime", GameplayObjectCategory.Monster, ActorFaction.Enemy, ActorCapability.Move | ActorCapability.Rotate | ActorCapability.BasicAttack | ActorCapability.ReceiveHit, motion, null, slimeSource, false, 3f, 3f, slimeBehaviors, slimePlans, Array.Empty<LegacyActionPlan>());
                ConfigurePrefab(LegacyActorMigrationPaths.YefaPrefabPath, yefaDefinition, true, yefaPlans, yefaActionPlans);
                ConfigurePrefab(LegacyActorMigrationPaths.SlimePrefabPath, slimeDefinition, false, slimePlans, Array.Empty<LegacyActionPlan>());
                AssetDatabase.SaveAssets();
                Debug.Log($"[Actor Migration] 已在 '{LegacyActorMigrationPaths.OutputFolder}' 完成 Yefa 四段普通攻击、Skill、Ultimate、SpecialAttack、Dodge 与 Slime 一段普通攻击迁移；迁移器未导入任何孤儿移动攻击。");
                ValidateAll();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Actor Migration] 迁移失败，未完成的资产将在下次执行时按稳定路径重新收敛。\n" + exception);
                throw;
            }
        }

        /// <summary>
        /// 按迁移计划创建或更新一组行为资产。
        /// </summary>
        private static ActorBehaviorDefinition[] CreateOrUpdateBehaviors(IReadOnlyList<LegacyAttackPlan> plans, Func<int, string> resolvePath)
        {
            ActorBehaviorDefinition[] behaviors = new ActorBehaviorDefinition[plans.Count];
            for (int index = 0; index < plans.Count; index++)
            {
                ActorBehaviorDefinition behavior = LoadOrCreate<ActorBehaviorDefinition>(resolvePath(index));
                ConfigureBehavior(behavior, plans[index]);
                behaviors[index] = behavior;
            }
            return behaviors;
        }

        /// <summary>
        /// 按每个计划携带的稳定路径创建或更新 Yefa 非普通攻击行为资产。
        /// </summary>
        private static ActorBehaviorDefinition[] CreateOrUpdateActionBehaviors(IReadOnlyList<LegacyActionPlan> plans)
        {
            ActorBehaviorDefinition[] behaviors = new ActorBehaviorDefinition[plans.Count];
            for (int index = 0; index < plans.Count; index++)
            {
                ActorBehaviorDefinition behavior = LoadOrCreate<ActorBehaviorDefinition>(plans[index].AssetPath);
                ConfigureActionBehavior(behavior, plans[index]);
                behaviors[index] = behavior;
            }
            return behaviors;
        }

        /// <summary>
        /// 以固定顺序合并普通攻击和其他行为引用，避免重复迁移改变 ActorDefinition.behaviors 的序列化顺序。
        /// </summary>
        private static ActorBehaviorDefinition[] CombineBehaviors(IReadOnlyList<ActorBehaviorDefinition> first, IReadOnlyList<ActorBehaviorDefinition> second)
        {
            ActorBehaviorDefinition[] result = new ActorBehaviorDefinition[first.Count + second.Count];
            for (int index = 0; index < first.Count; index++) result[index] = first[index];
            for (int index = 0; index < second.Count; index++) result[first.Count + index] = second[index];
            return result;
        }

        /// <summary>
        /// 把一个确定性攻击计划写入 ActorBehaviorDefinition 的模拟、战斗与表现字段。
        /// </summary>
        private static void ConfigureBehavior(ActorBehaviorDefinition behavior, LegacyAttackPlan plan)
        {
            SerializedObject serializedBehavior = new SerializedObject(behavior);
            serializedBehavior.FindProperty("behaviorId").stringValue = plan.BehaviorId;
            serializedBehavior.FindProperty("command").enumValueIndex = (int)ActorBehaviorCommand.BasicAttack;
            serializedBehavior.FindProperty("commandIndex").intValue = plan.CommandIndex;
            serializedBehavior.FindProperty("durationTicks").intValue = plan.DurationTicks;
            serializedBehavior.FindProperty("chainFromTick").intValue = plan.ChainFromTick;
            ConfigureAttackSimulationClips(serializedBehavior.FindProperty("simulationClips"), plan);
            ConfigureHitSignal(serializedBehavior.FindProperty("hitSignal"), plan.BehaviorId, EffectTag.Attack | EffectTag.NormalAttack, plan.TargetFactions);
            ConfigurePresentationVariants(serializedBehavior.FindProperty("presentationVariants"), plan);
            serializedBehavior.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(behavior);
            behavior.BuildProgram();
        }

        /// <summary>
        /// 写入一个使用半开区间和稳定 Attack 绑定的权威 HitWindow。
        /// </summary>
        private static void ConfigureAttackSimulationClips(SerializedProperty clips, LegacyAttackPlan plan)
        {
            bool blocksCharacterControl = plan.HasMovingVariant;
            clips.arraySize = 1 + (plan.HasMotion ? 1 : 0) + (blocksCharacterControl ? 1 : 0);
            SerializedProperty clip = clips.GetArrayElementAtIndex(0);
            clip.FindPropertyRelative("clipId").stringValue = "HitWindow";
            clip.FindPropertyRelative("kind").enumValueIndex = (int)SimulationClipKind.HitWindow;
            clip.FindPropertyRelative("startTick").intValue = plan.HitStartTick;
            clip.FindPropertyRelative("endTick").intValue = plan.HitEndTick;
            clip.FindPropertyRelative("bindingId").stringValue = LegacyActorMigrationPaths.HitboxBindingId;
            clip.FindPropertyRelative("blockedCapabilities").intValue = 0;
            int nextClipIndex = 1;
            if (plan.HasMotion)
            {
                ConfigureSimulationClip(clips.GetArrayElementAtIndex(nextClipIndex++), "Motion", SimulationClipKind.Motion, 0, plan.DurationTicks, plan.MotionBindingId, ActorCapability.None);
            }
            if (blocksCharacterControl) ConfigureSimulationClip(clips.GetArrayElementAtIndex(nextClipIndex), "ControlBlock", SimulationClipKind.CapabilityBlock, 0, plan.DurationTicks, string.Empty, GetLegacyCharacterControlBlock());
        }

        /// <summary>
        /// 写入普通攻击命中后发布 EffectSignal 所需的稳定语义。
        /// </summary>
        private static void ConfigureHitSignal(SerializedProperty hitSignal, string signalId, EffectTag tags, ActorFactionMask targetFactions)
        {
            hitSignal.FindPropertyRelative("signalId").stringValue = signalId;
            hitSignal.FindPropertyRelative("tags").intValue = (int)tags;
            hitSignal.FindPropertyRelative("targetFactions").intValue = (int)targetFactions;
            hitSignal.FindPropertyRelative("targetLayerMask").intValue = ~0;
            hitSignal.FindPropertyRelative("requiredTargetTag").stringValue = string.Empty;
            hitSignal.FindPropertyRelative("damageSource").enumValueIndex = (int)ActorDamageSource.CalculatedAttack;
            hitSignal.FindPropertyRelative("constantDamage").floatValue = 1f;
        }

        /// <summary>
        /// 将 Yefa 非普通攻击计划写入权威时间轴、阵营过滤、EffectTag 和多段客户端表现字段。
        /// </summary>
        private static void ConfigureActionBehavior(ActorBehaviorDefinition behavior, LegacyActionPlan plan)
        {
            SerializedObject serializedBehavior = new SerializedObject(behavior);
            serializedBehavior.FindProperty("behaviorId").stringValue = plan.BehaviorId;
            serializedBehavior.FindProperty("command").enumValueIndex = (int)plan.Command;
            serializedBehavior.FindProperty("commandIndex").intValue = 0;
            serializedBehavior.FindProperty("durationTicks").intValue = plan.DurationTicks;
            serializedBehavior.FindProperty("chainFromTick").intValue = plan.ChainFromTick;
            ConfigureActionSimulationClips(serializedBehavior.FindProperty("simulationClips"), plan);
            ConfigureHitSignal(serializedBehavior.FindProperty("hitSignal"), plan.BehaviorId, plan.EffectTags, plan.TargetFactions);
            ConfigureActionPresentationVariants(serializedBehavior.FindProperty("presentationVariants"), plan);
            serializedBehavior.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(behavior);
            behavior.BuildProgram();
        }

        /// <summary>
        /// Dodge 清空模拟片段，其他主动行为写入一个与旧 ColliderProxy 对应的半开 HitWindow。
        /// </summary>
        private static void ConfigureActionSimulationClips(SerializedProperty clips, LegacyActionPlan plan)
        {
            clips.arraySize = (plan.HasHitWindow ? 1 : 0) + plan.MotionPlans.Length + 1;
            int nextClipIndex = 0;
            if (plan.HasHitWindow) ConfigureSimulationClip(clips.GetArrayElementAtIndex(nextClipIndex++), "HitWindow", SimulationClipKind.HitWindow, plan.HitStartTick, plan.HitEndTick, plan.HitboxBindingId, ActorCapability.None);
            for (int motionIndex = 0; motionIndex < plan.MotionPlans.Length; motionIndex++) ConfigureSimulationClip(clips.GetArrayElementAtIndex(nextClipIndex++), "Motion." + (motionIndex + 1), SimulationClipKind.Motion, 0, plan.DurationTicks, plan.MotionPlans[motionIndex].MotionId, ActorCapability.None);
            ConfigureSimulationClip(clips.GetArrayElementAtIndex(nextClipIndex), "ControlBlock", SimulationClipKind.CapabilityBlock, 0, plan.DurationTicks, string.Empty, GetLegacyCharacterControlBlock());
        }

        /// <summary>
        /// 完整覆盖一个权威模拟片段的公共字段，避免重复迁移后残留上一种 Clip 类型的数据。
        /// </summary>
        private static void ConfigureSimulationClip(SerializedProperty clip, string clipId, SimulationClipKind kind, int startTick, int endTick, string bindingId, ActorCapability blockedCapabilities)
        {
            clip.FindPropertyRelative("clipId").stringValue = clipId;
            clip.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            clip.FindPropertyRelative("startTick").intValue = startTick;
            clip.FindPropertyRelative("endTick").intValue = endTick;
            clip.FindPropertyRelative("bindingId").stringValue = bindingId;
            clip.FindPropertyRelative("blockedCapabilities").intValue = (int)blockedCapabilities;
        }

        /// <summary>
        /// 返回旧 MotionBlockerStart 在角色行为期间关闭的控制能力集合。
        /// </summary>
        private static ActorCapability GetLegacyCharacterControlBlock()
        {
            return ActorCapability.Move | ActorCapability.Rotate | ActorCapability.Jump | ActorCapability.Dodge;
        }

        /// <summary>
        /// 写入主动行为的全部表现变体；每个变体都保留其独立 Spine Cue 序列，并在真实 hit_start Tick 触发音效和 VFX。
        /// </summary>
        private static void ConfigureActionPresentationVariants(SerializedProperty variants, LegacyActionPlan plan)
        {
            variants.arraySize = plan.Variants.Length;
            for (int variantIndex = 0; variantIndex < plan.Variants.Length; variantIndex++)
            {
                LegacyActionVariantPlan variantPlan = plan.Variants[variantIndex];
                SerializedProperty variant = variants.GetArrayElementAtIndex(variantIndex);
                variant.FindPropertyRelative("variantId").stringValue = variantPlan.VariantId;
                SerializedProperty cues = variant.FindPropertyRelative("cues");
                int extraCueCount = (plan.HasAudioCue ? 1 : 0) + (plan.HasVfxCue ? 1 : 0);
                cues.arraySize = variantPlan.AnimationCues.Length + extraCueCount;
                for (int cueIndex = 0; cueIndex < variantPlan.AnimationCues.Length; cueIndex++)
                {
                    LegacyAnimationCuePlan animationCue = variantPlan.AnimationCues[cueIndex];
                    ConfigureCue(cues.GetArrayElementAtIndex(cueIndex), animationCue.CueId, ActorPresentationCueKind.SpineAnimation, animationCue.StartTick, animationCue.EndTick, string.Empty, animationCue.Animation, null);
                }
                int nextCueIndex = variantPlan.AnimationCues.Length;
                if (plan.HasAudioCue) ConfigureCue(cues.GetArrayElementAtIndex(nextCueIndex++), "Audio", ActorPresentationCueKind.Audio, plan.AudioTick, 0, string.Empty, null, plan.AudioClip);
                if (plan.HasVfxCue) ConfigureCue(cues.GetArrayElementAtIndex(nextCueIndex), "Vfx", ActorPresentationCueKind.Vfx, plan.VfxTick, 0, plan.VfxBindingId, null, null);
            }
        }

        /// <summary>
        /// 为 Yefa 写入 Default 与 Moving，为 Slime 只写入 Default，并让音效与 VFX 在 hit_start Tick 触发。
        /// </summary>
        private static void ConfigurePresentationVariants(SerializedProperty variants, LegacyAttackPlan plan)
        {
            variants.arraySize = plan.HasMovingVariant ? 2 : 1;
            ConfigurePresentationVariant(variants.GetArrayElementAtIndex(0), "Default", plan.DefaultAnimation, plan.DefaultAnimationEndTick, plan);
            if (plan.HasMovingVariant) ConfigurePresentationVariant(variants.GetArrayElementAtIndex(1), "Moving", plan.MovingAnimation, plan.MovingAnimationEndTick, plan);
        }

        /// <summary>
        /// 写入一个动画变体及其一次性音效、可选 VFX Cue。
        /// </summary>
        private static void ConfigurePresentationVariant(SerializedProperty variant, string variantId, AnimationReferenceAsset animation, int animationEndTick, LegacyAttackPlan plan)
        {
            variant.FindPropertyRelative("variantId").stringValue = variantId;
            SerializedProperty cues = variant.FindPropertyRelative("cues");
            cues.arraySize = plan.HasVfxCue ? 3 : 2;
            ConfigureCue(cues.GetArrayElementAtIndex(0), "Animation", ActorPresentationCueKind.SpineAnimation, 0, animationEndTick, string.Empty, animation, null);
            ConfigureCue(cues.GetArrayElementAtIndex(1), "Audio", ActorPresentationCueKind.Audio, plan.HitStartTick, 0, string.Empty, null, plan.AudioClip);
            if (plan.HasVfxCue) ConfigureCue(cues.GetArrayElementAtIndex(2), "Vfx", ActorPresentationCueKind.Vfx, plan.HitStartTick, 0, plan.VfxBindingId, null, null);
        }

        /// <summary>
        /// 完整覆盖一个表现 Cue 的所有序列化字段，避免旧生成资产残留其他 Cue 类型的数据。
        /// </summary>
        private static void ConfigureCue(SerializedProperty cue, string cueId, ActorPresentationCueKind kind, int startTick, int endTick, string bindingId, AnimationReferenceAsset animation, AudioClip audioClip)
        {
            cue.FindPropertyRelative("cueId").stringValue = cueId;
            cue.FindPropertyRelative("kind").enumValueIndex = (int)kind;
            cue.FindPropertyRelative("startTick").intValue = startTick;
            cue.FindPropertyRelative("endTick").intValue = endTick;
            cue.FindPropertyRelative("bindingId").stringValue = bindingId;
            cue.FindPropertyRelative("animation").objectReferenceValue = animation;
            cue.FindPropertyRelative("audioClip").objectReferenceValue = audioClip;
            cue.FindPropertyRelative("spineTrack").intValue = 0;
            cue.FindPropertyRelative("loop").boolValue = false;
            cue.FindPropertyRelative("mixIn").floatValue = 0f;
            cue.FindPropertyRelative("mixOut").floatValue = 0f;
            cue.FindPropertyRelative("cameraProfile").objectReferenceValue = null;
            cue.FindPropertyRelative("cameraPriority").intValue = 100;
        }

        /// <summary>
        /// 配置一个角色定义的稳定 ID、能力、共享运动、基础 Spine 表现和行为引用。
        /// </summary>
        private static void ConfigureActorDefinition(ActorDefinition definition, string actorId, GameplayObjectCategory category, ActorFaction faction, ActorCapability defaultCapabilities, CharacterControllerMotionModelDefinition motion, CameraFollowProfile camera, LegacyActorSource source, bool includeCharacterLocomotion, float moveSpeed, float sprintSpeed, IReadOnlyList<ActorBehaviorDefinition> behaviors, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans)
        {
            SerializedObject serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("actorId").stringValue = actorId;
            serializedDefinition.FindProperty("category").enumValueIndex = (int)category;
            serializedDefinition.FindProperty("faction").enumValueIndex = (int)faction;
            serializedDefinition.FindProperty("defaultCapabilities").intValue = (int)defaultCapabilities;
            serializedDefinition.FindProperty("motionModel").objectReferenceValue = motion;
            serializedDefinition.FindProperty("cameraProfile").objectReferenceValue = camera;
            serializedDefinition.FindProperty("moveSpeed").floatValue = moveSpeed;
            serializedDefinition.FindProperty("sprintSpeed").floatValue = sprintSpeed;
            serializedDefinition.FindProperty("heldAttackSpecialTriggerTicks").intValue = 30;
            ConfigureLocomotion(serializedDefinition.FindProperty("locomotionPresentation"), source, includeCharacterLocomotion);
            ConfigureMotionBindings(serializedDefinition.FindProperty("motionBindings"), attackPlans, actionPlans);
            SerializedProperty behaviorReferences = serializedDefinition.FindProperty("behaviors");
            behaviorReferences.arraySize = behaviors.Count;
            for (int index = 0; index < behaviors.Count; index++) behaviorReferences.GetArrayElementAtIndex(index).objectReferenceValue = behaviors[index];
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            definition.ValidateOrThrow();
        }

        /// <summary>
        /// 写入所有带 MotionClip 普通攻击的逐 Tick 根运动样本；Slime 没有此类计划时会幂等清空旧生成绑定。
        /// </summary>
        private static void ConfigureMotionBindings(SerializedProperty bindings, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans)
        {
            int motionCount = 0;
            for (int index = 0; index < attackPlans.Count; index++) if (attackPlans[index].HasMotion) motionCount++;
            for (int index = 0; index < actionPlans.Count; index++) motionCount += actionPlans[index].MotionPlans.Length;
            bindings.arraySize = motionCount;
            int bindingIndex = 0;
            for (int planIndex = 0; planIndex < attackPlans.Count; planIndex++)
            {
                LegacyAttackPlan plan = attackPlans[planIndex];
                if (!plan.HasMotion) continue;
                if (plan.BakedMotionSamples == null || plan.BakedMotionSamples.Length != plan.DurationTicks) throw new InvalidOperationException($"行为 '{plan.BehaviorId}' 的根运动样本数量必须等于行为时长 {plan.DurationTicks}。");
                ConfigureMotionBinding(bindings.GetArrayElementAtIndex(bindingIndex++), plan.MotionBindingId, "Moving", plan.BakedMotionSamples);
            }
            for (int planIndex = 0; planIndex < actionPlans.Count; planIndex++)
            {
                LegacyActionPlan actionPlan = actionPlans[planIndex];
                for (int motionIndex = 0; motionIndex < actionPlan.MotionPlans.Length; motionIndex++)
                {
                    LegacyActionMotionPlan motionPlan = actionPlan.MotionPlans[motionIndex];
                    if (motionPlan.BakedMotionSamples.Length != actionPlan.DurationTicks) throw new InvalidOperationException($"行为 '{actionPlan.BehaviorId}' 的根运动 '{motionPlan.MotionId}' 样本数量必须等于行为时长 {actionPlan.DurationTicks}。");
                    ConfigureMotionBinding(bindings.GetArrayElementAtIndex(bindingIndex++), motionPlan.MotionId, motionPlan.RequiredVariantId, motionPlan.BakedMotionSamples);
                }
            }
        }

        /// <summary>
        /// 写入一个 MotionId、变体约束和完整逐 Tick 位移列表，并清空不再使用的常量位移。
        /// </summary>
        private static void ConfigureMotionBinding(SerializedProperty binding, string motionId, string requiredVariantId, IReadOnlyList<Vector3> bakedMotionSamples)
        {
            binding.FindPropertyRelative("motionId").stringValue = motionId;
            binding.FindPropertyRelative("requiredVariantId").stringValue = requiredVariantId;
            binding.FindPropertyRelative("localDisplacementPerBehaviorTick").vector3Value = Vector3.zero;
            SerializedProperty samples = binding.FindPropertyRelative("localDisplacementsByBehaviorTick");
            samples.arraySize = bakedMotionSamples.Count;
            for (int sampleIndex = 0; sampleIndex < bakedMotionSamples.Count; sampleIndex++) samples.GetArrayElementAtIndex(sampleIndex).vector3Value = bakedMotionSamples[sampleIndex];
        }

        /// <summary>
        /// 从旧动画库迁移基础状态动画；史莱姆只迁移实际使用的 Idle 与 Move，避免引入指向 Yefa 骨骼的旧空中动画引用。
        /// </summary>
        private static void ConfigureLocomotion(SerializedProperty locomotion, LegacyActorSource source, bool includeCharacterLocomotion)
        {
            locomotion.FindPropertyRelative("idle").objectReferenceValue = source.IdleAnimation;
            locomotion.FindPropertyRelative("move").objectReferenceValue = source.MoveAnimation;
            locomotion.FindPropertyRelative("sprint").objectReferenceValue = includeCharacterLocomotion ? source.SprintAnimation : null;
            locomotion.FindPropertyRelative("jump").objectReferenceValue = includeCharacterLocomotion ? source.JumpAnimation : null;
            locomotion.FindPropertyRelative("fall").objectReferenceValue = includeCharacterLocomotion ? source.FallAnimation : null;
            locomotion.FindPropertyRelative("land").objectReferenceValue = includeCharacterLocomotion ? source.LandAnimation : null;
            locomotion.FindPropertyRelative("mixDuration").floatValue = 0.15f;
        }

        /// <summary>
        /// 在 Prefab 根节点幂等添加并配置 ActorAuthoringComponent，Yefa 同时添加 CameraSubject 和四个 VFX 绑定。
        /// </summary>
        private static void ConfigurePrefab(string prefabPath, ActorDefinition definition, bool requiresCameraSubject, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) throw new InvalidOperationException($"无法加载 Prefab '{prefabPath}'。");
            try
            {
                AttackComponent attackComponent = root.GetComponentInChildren<AttackComponent>(true);
                if (attackComponent == null || attackComponent.atkCollider == null) throw new InvalidOperationException($"Prefab '{prefabPath}' 缺少旧 AttackComponent.atkCollider 绑定。");
                Collider attackShape = ResolveQueryShape(attackComponent.atkCollider, prefabPath, ActorBehaviorCommand.BasicAttack);
                SpineComponent spineComponent = root.GetComponentInChildren<SpineComponent>(true);
                Transform facingRoot = spineComponent == null ? null : spineComponent.rotateRoot;
                if (facingRoot == null) throw new InvalidOperationException($"Prefab '{prefabPath}' 缺少 Actor Hitbox 镜像所需的 SpineComponent.rotateRoot。");
                ActorAuthoringComponent authoring = root.GetComponent<ActorAuthoringComponent>();
                if (authoring == null) authoring = root.AddComponent<ActorAuthoringComponent>();
                CameraSubject cameraSubject = requiresCameraSubject ? root.GetComponent<CameraSubject>() : null;
                if (requiresCameraSubject && cameraSubject == null) cameraSubject = root.AddComponent<CameraSubject>();
                SerializedObject serializedAuthoring = new SerializedObject(authoring);
                serializedAuthoring.FindProperty("definition").objectReferenceValue = definition;
                serializedAuthoring.FindProperty("cameraSubject").objectReferenceValue = cameraSubject;
                serializedAuthoring.FindProperty("facingRoot").objectReferenceValue = facingRoot;
                serializedAuthoring.FindProperty("rightFacingRootLocalEulerAngles").vector3Value = spineComponent.FacingRootRightLocalRotation.eulerAngles;
                SerializedProperty hitboxes = serializedAuthoring.FindProperty("hitboxes");
                int actionHitboxCount = 0;
                for (int actionIndex = 0; actionIndex < actionPlans.Count; actionIndex++) if (actionPlans[actionIndex].HasHitWindow) actionHitboxCount++;
                hitboxes.arraySize = 1 + actionHitboxCount;
                ConfigureHitboxBinding(hitboxes.GetArrayElementAtIndex(0), LegacyActorMigrationPaths.HitboxBindingId, attackShape, facingRoot);
                int hitboxIndex = 1;
                for (int actionIndex = 0; actionIndex < actionPlans.Count; actionIndex++)
                {
                    LegacyActionPlan actionPlan = actionPlans[actionIndex];
                    if (!actionPlan.HasHitWindow) continue;
                    Collider actionShape = ResolveActionQueryShape(root, actionPlan.Command, prefabPath);
                    SerializedProperty binding = hitboxes.GetArrayElementAtIndex(hitboxIndex++);
                    ConfigureHitboxBinding(binding, actionPlan.HitboxBindingId, actionShape, facingRoot);
                }
                ConfigurePrefabVfxBindings(root, serializedAuthoring.FindProperty("vfxBindings"), attackPlans, actionPlans);
                serializedAuthoring.ApplyModifiedPropertiesWithoutUndo();
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if (savedPrefab == null) throw new InvalidOperationException($"保存 Prefab '{prefabPath}' 失败。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// 把稳定编号、原始 Collider 与显式朝向规则写入一个 Hitbox 绑定；已经继承 FacingRoot 的形状不再重复镜像。
        /// </summary>
        private static void ConfigureHitboxBinding(SerializedProperty binding, string bindingId, Collider shape, Transform facingRoot)
        {
            ActorHitboxFacingRule facingRule = shape.transform == facingRoot || shape.transform.IsChildOf(facingRoot) ? ActorHitboxFacingRule.ShapeTransform : ActorHitboxFacingRule.MirrorWithFacingRoot;
            binding.FindPropertyRelative("bindingId").stringValue = bindingId;
            binding.FindPropertyRelative("shape").objectReferenceValue = shape;
            binding.FindPropertyRelative("facingRule").enumValueIndex = (int)facingRule;
        }

        /// <summary>
        /// 从旧 Skill、Ultimate 或 SpecialAttack Component 解析 ColliderProxy，并原样保留 Box、Sphere 或 Capsule 的真实几何类型。
        /// </summary>
        private static Collider ResolveActionQueryShape(GameObject root, ActorBehaviorCommand command, string prefabPath)
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
            if (proxy == null) throw new InvalidOperationException($"Prefab '{prefabPath}' 缺少命令 '{command}' 对应的旧 ColliderProxy。");
            return ResolveQueryShape(proxy, prefabPath, command);
        }

        /// <summary>从一个旧 ColliderProxy 解析唯一受支持的原始查询形状，并清理由旧迁移版本生成且可证明等价的 Sphere-to-Box 近似。</summary>
        private static Collider ResolveQueryShape(ColliderProxy proxy, string prefabPath, ActorBehaviorCommand command)
        {
            BoxCollider box = proxy.GetComponent<BoxCollider>();
            SphereCollider sphere = proxy.GetComponent<SphereCollider>();
            CapsuleCollider capsule = proxy.GetComponent<CapsuleCollider>();
            if (box != null && sphere != null && IsLegacySphereApproximation(box, sphere))
            {
                UnityEngine.Object.DestroyImmediate(box);
                box = null;
            }
            Collider[] allColliders = proxy.GetComponents<Collider>();
            int supportedCount = (box != null ? 1 : 0) + (sphere != null ? 1 : 0) + (capsule != null ? 1 : 0);
            if (supportedCount != 1 || allColliders.Length != 1) throw new InvalidOperationException($"Prefab '{prefabPath}' 的命令 '{command}' ColliderProxy 必须只包含一个 BoxCollider、SphereCollider 或 CapsuleCollider，当前 Collider 数量为 {allColliders.Length}，受支持形状数量为 {supportedCount}。");
            Collider shape = box != null ? (Collider)box : sphere != null ? sphere : capsule;
            shape.enabled = false;
            return shape;
        }

        /// <summary>识别旧迁移器从 SphereCollider 生成的等中心、等直径 BoxCollider，避免删除可能具有独立语义的手工 Box。</summary>
        private static bool IsLegacySphereApproximation(BoxCollider box, SphereCollider sphere)
        {
            Vector3 expectedSize = Vector3.one * sphere.radius * 2f;
            return !box.enabled && box.isTrigger == sphere.isTrigger && (box.center - sphere.center).sqrMagnitude <= 0.000001f && (box.size - expectedSize).sqrMagnitude <= 0.000001f;
        }

        /// <summary>
        /// 把 Yefa 旧 VfxComponent 的前四个普通攻击插槽映射到行为 Cue；Slime 行为没有 VFX 时清空生成绑定。
        /// </summary>
        private static void ConfigurePrefabVfxBindings(GameObject root, SerializedProperty bindings, IReadOnlyList<LegacyAttackPlan> attackPlans, IReadOnlyList<LegacyActionPlan> actionPlans)
        {
            int requiredCount = 0;
            for (int index = 0; index < attackPlans.Count; index++) if (attackPlans[index].HasVfxCue) requiredCount++;
            for (int index = 0; index < actionPlans.Count; index++) if (actionPlans[index].HasVfxCue) requiredCount++;
            bindings.arraySize = requiredCount;
            if (requiredCount == 0) return;
            global::Xuan.Prometheus.VfxComponent vfxComponent = root.GetComponentInChildren<global::Xuan.Prometheus.VfxComponent>(true);
            if (vfxComponent == null || vfxComponent.vfxSlots == null) throw new InvalidOperationException($"Prefab '{root.name}' 缺少旧 VfxComponent.vfxSlots。");
            int bindingIndex = 0;
            for (int planIndex = 0; planIndex < attackPlans.Count; planIndex++)
            {
                LegacyAttackPlan plan = attackPlans[planIndex];
                if (!plan.HasVfxCue) continue;
                ConfigurePrefabVfxBinding(vfxComponent, bindings.GetArrayElementAtIndex(bindingIndex++), plan.VfxBindingId, plan.VfxSlotIndex, root.name);
            }
            for (int planIndex = 0; planIndex < actionPlans.Count; planIndex++)
            {
                LegacyActionPlan plan = actionPlans[planIndex];
                if (!plan.HasVfxCue) continue;
                ConfigurePrefabVfxBinding(vfxComponent, bindings.GetArrayElementAtIndex(bindingIndex++), plan.VfxBindingId, plan.VfxSlotIndex, root.name);
            }
        }

        /// <summary>
        /// 将一个行为 VFX 绑定到旧 Executor 序列化枚举指定的真实 vfxSlots 索引，并拒绝越界或空槽位。
        /// </summary>
        private static void ConfigurePrefabVfxBinding(global::Xuan.Prometheus.VfxComponent vfxComponent, SerializedProperty binding, string bindingId, int slotIndex, string prefabName)
        {
            if (slotIndex < 0 || slotIndex >= vfxComponent.vfxSlots.Count) throw new InvalidOperationException($"Prefab '{prefabName}' 的行为 VFX '{bindingId}' 请求旧槽位 {slotIndex}，但 vfxSlots 数量为 {vfxComponent.vfxSlots.Count}。");
            GameObject visualRoot = vfxComponent.vfxSlots[slotIndex];
            if (visualRoot == null) throw new InvalidOperationException($"Prefab '{prefabName}' 的旧 VFX 槽位 {slotIndex} 为空。");
            binding.FindPropertyRelative("bindingId").stringValue = bindingId;
            binding.FindPropertyRelative("visualRoot").objectReferenceValue = visualRoot;
        }

        /// <summary>
        /// 按稳定路径加载资产；路径为空时创建新资产，路径被其他类型占用时拒绝破坏性覆盖。
        /// </summary>
        private static TAsset LoadOrCreate<TAsset>(string assetPath) where TAsset : ScriptableObject
        {
            TAsset asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
            if (asset != null) return asset;
            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (existingAsset != null) throw new InvalidOperationException($"路径 '{assetPath}' 已被不兼容资产类型 '{existingAsset.GetType().FullName}' 占用。");
            asset = ScriptableObject.CreateInstance<TAsset>();
            asset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(asset, assetPath);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>
        /// 逐级创建输出文件夹，确保首次迁移与重复迁移使用相同路径。
        /// </summary>
        private static void EnsureFolder(string folderPath)
        {
            string[] segments = folderPath.Split('/');
            string currentPath = segments[0];
            for (int index = 1; index < segments.Length; index++)
            {
                string nextPath = currentPath + "/" + segments[index];
                if (!AssetDatabase.IsValidFolder(nextPath)) AssetDatabase.CreateFolder(currentPath, segments[index]);
                currentPath = nextPath;
            }
        }
    }
}
