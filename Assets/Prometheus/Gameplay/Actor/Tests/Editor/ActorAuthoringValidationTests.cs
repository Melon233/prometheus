using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证行为、对象定义与 Prefab 场景绑定的资产约束，确保错误配置在进入运行时前即可被定位。</summary>
    public sealed class ActorAuthoringValidationTests
    {
        /// <summary>保存每个测试创建的 Unity 对象，避免临时 ScriptableObject 与 GameObject 污染后续用例。</summary>
        private readonly List<UnityEngine.Object> createdObjects = new List<UnityEngine.Object>();

        /// <summary>在每个测试结束后按逆序销毁临时对象，使父子 GameObject 与共享资产都能安全清理。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int index = createdObjects.Count - 1; index >= 0; index--)
            {
                UnityEngine.Object createdObject = createdObjects[index];
                if (createdObject != null) UnityEngine.Object.DestroyImmediate(createdObject);
            }
            createdObjects.Clear();
        }

        /// <summary>验证合法行为资产会保留创作顺序并编译出完整的纯模拟语义，同时表现变体查找保持稳定编号的大小写敏感规则。</summary>
        [Test]
        public void BehaviorDefinition_WithValidConfiguration_BuildsCompleteProgramAndUsesOrdinalVariantIds()
        {
            ActorBehaviorDefinition behavior = CreateValidBehavior("Hero.Attack.01", true);
            BehaviorProgram program = behavior.BuildProgram();
            Assert.That(program.ProgramId, Is.EqualTo("Hero.Attack.01"));
            Assert.That(program.DurationTicks, Is.EqualTo(12));
            Assert.That(program.SimulationClips.Count, Is.EqualTo(4));
            Assert.That(program.SimulationClips[0], Is.TypeOf<HitWindowClip>());
            Assert.That(((HitWindowClip)program.SimulationClips[0]).HitboxId, Is.EqualTo("Attack"));
            Assert.That(program.SimulationClips[1], Is.TypeOf<CapabilityBlockClip>());
            Assert.That(((CapabilityBlockClip)program.SimulationClips[1]).BlockedCapabilities, Is.EqualTo(ActorCapability.Move | ActorCapability.Rotate));
            Assert.That(program.SimulationClips[2], Is.TypeOf<GameplayEventClip>());
            Assert.That(((GameplayEventClip)program.SimulationClips[2]).EventId, Is.EqualTo("CommitCost"));
            Assert.That(program.SimulationClips[2].StartTick, Is.EqualTo(6));
            Assert.That(program.SimulationClips[2].EndTick, Is.EqualTo(7));
            Assert.That(program.SimulationClips[3], Is.TypeOf<MotionClip>());
            Assert.That(((MotionClip)program.SimulationClips[3]).MotionId, Is.EqualTo("Lunge"));
            Assert.That(behavior.TryGetPresentationVariant("Moving", out ActorPresentationVariantDefinition movingVariant), Is.True);
            Assert.That(movingVariant.VariantId, Is.EqualTo("Moving"));
            Assert.That(behavior.TryGetPresentationVariant("moving", out _), Is.False);
        }

        /// <summary>验证模拟片段的重复编号、越界区间与缺失语义绑定都会在资产验证阶段失败，而不会生成部分有效的程序。</summary>
        [Test]
        public void BehaviorDefinition_WithInvalidSimulationClips_RejectsDuplicateIdsIntervalsAndBindings()
        {
            ActorBehaviorDefinition duplicateIds = CreateBehavior("Invalid.Duplicate", new List<ActorSimulationClipDefinition> { CreateClip("Duplicate", SimulationClipKind.HitWindow, 1, 3, "Attack"), CreateClip("Duplicate", SimulationClipKind.Motion, 3, 6, "Lunge") }, CreateDefaultVariants());
            Assert.Throws<InvalidOperationException>(() => duplicateIds.ValidateOrThrow());
            ActorBehaviorDefinition outsideDuration = CreateBehavior("Invalid.Interval", new List<ActorSimulationClipDefinition> { CreateClip("Late", SimulationClipKind.Motion, 12, 13, "Lunge") }, CreateDefaultVariants());
            Assert.Throws<InvalidOperationException>(() => outsideDuration.ValidateOrThrow());
            ActorBehaviorDefinition missingBinding = CreateBehavior("Invalid.Binding", new List<ActorSimulationClipDefinition> { CreateClip("Motion", SimulationClipKind.Motion, 0, 4, " ") }, CreateDefaultVariants());
            Assert.Throws<InvalidOperationException>(() => missingBinding.ValidateOrThrow());
        }

        /// <summary>验证命中窗口必须声明有效战斗信号，并且每个行为都必须提供 Default 表现降级路径。</summary>
        [Test]
        public void BehaviorDefinition_WithHitWindowOrVariants_RequiresHitSignalAndDefaultFallback()
        {
            ActorBehaviorDefinition missingHitSignal = CreateValidBehavior("Invalid.HitSignal", false);
            SetPrivateField(missingHitSignal, "hitSignal", null);
            Assert.Throws<InvalidOperationException>(() => missingHitSignal.ValidateOrThrow());
            ActorBehaviorDefinition missingDefaultVariant = CreateValidBehavior("Invalid.DefaultVariant", false);
            SetPrivateField(missingDefaultVariant, "presentationVariants", new List<ActorPresentationVariantDefinition> { CreateVariant("Moving") });
            Assert.Throws<InvalidOperationException>(() => missingDefaultVariant.ValidateOrThrow());
        }

        /// <summary>验证需要外部表现资源的 Cue 不接受空动画、空镜头配置或空稳定绑定，避免播放阶段才出现静默缺失。</summary>
        [Test]
        public void BehaviorDefinition_WithResourceBackedCues_RejectsMissingRequiredResources()
        {
            ActorBehaviorDefinition missingAnimation = CreateBehaviorWithCue("Invalid.SpineCue", CreateCue("Animation", ActorPresentationCueKind.SpineAnimation, 0, 8, null));
            Assert.Throws<InvalidOperationException>(() => missingAnimation.ValidateOrThrow());
            ActorBehaviorDefinition missingAudioClip = CreateBehaviorWithCue("Invalid.AudioCue", CreateCue("Audio", ActorPresentationCueKind.Audio, 1, 0, null));
            Assert.Throws<InvalidOperationException>(() => missingAudioClip.ValidateOrThrow());
            ActorBehaviorDefinition missingCameraProfile = CreateBehaviorWithCue("Invalid.CameraCue", CreateCue("Camera", ActorPresentationCueKind.Camera, 1, 5, null));
            Assert.Throws<InvalidOperationException>(() => missingCameraProfile.ValidateOrThrow());
            ActorBehaviorDefinition missingVfxBinding = CreateBehaviorWithCue("Invalid.VfxCue", CreateCue("Vfx", ActorPresentationCueKind.Vfx, 2, 4, " "));
            Assert.Throws<InvalidOperationException>(() => missingVfxBinding.ValidateOrThrow());
        }

        /// <summary>验证对象定义可以按稳定编号解析合法行为与位移，并拒绝跨列表的重复稳定编号。</summary>
        [Test]
        public void ActorDefinition_ValidatesAndResolvesStableBehaviorAndMotionIds()
        {
            ActorBehaviorDefinition behavior = CreateValidBehavior("Hero.Attack.01", false);
            ActorDefinition validDefinition = CreateDefinition("Hero", new List<ActorBehaviorDefinition> { behavior }, new List<ActorMotionBindingDefinition> { CreateMotionBinding("Lunge", new Vector3(0f, 0f, 0.1f)) });
            Assert.DoesNotThrow(() => validDefinition.ValidateOrThrow());
            Assert.That(validDefinition.TryGetBehavior("Hero.Attack.01", out ActorBehaviorDefinition resolvedBehavior), Is.True);
            Assert.That(resolvedBehavior, Is.SameAs(behavior));
            Assert.That(validDefinition.TryGetBehavior("hero.attack.01", out _), Is.False);
            Assert.That(validDefinition.TryGetMotionBinding("Lunge", out ActorMotionBindingDefinition resolvedMotion), Is.True);
            Assert.That(resolvedMotion.LocalDisplacementPerBehaviorTick, Is.EqualTo(new Vector3(0f, 0f, 0.1f)));
            ActorDefinition duplicateMotionDefinition = CreateDefinition("Invalid.Motion", new List<ActorBehaviorDefinition> { behavior }, new List<ActorMotionBindingDefinition> { CreateMotionBinding("Lunge", Vector3.forward), CreateMotionBinding("Lunge", Vector3.back) });
            Assert.Throws<InvalidOperationException>(() => duplicateMotionDefinition.ValidateOrThrow());
            ActorBehaviorDefinition duplicateBehavior = CreateValidBehavior("Hero.Attack.01", false);
            ActorDefinition duplicateBehaviorDefinition = CreateDefinition("Invalid.Behavior", new List<ActorBehaviorDefinition> { behavior, duplicateBehavior }, new List<ActorMotionBindingDefinition>());
            Assert.Throws<InvalidOperationException>(() => duplicateBehaviorDefinition.ValidateOrThrow());
            ActorDefinition invalidHeldAttackDefinition = CreateDefinition("Invalid.HeldAttack", new List<ActorBehaviorDefinition> { behavior }, new List<ActorMotionBindingDefinition>());
            SetPrivateField(invalidHeldAttackDefinition, "heldAttackSpecialTriggerTicks", 0);
            Assert.Throws<InvalidOperationException>(() => invalidHeldAttackDefinition.ValidateOrThrow());
            ActorMotionBindingDefinition invalidDisplacement = CreateMotionBinding("InvalidDisplacement", new Vector3(float.NaN, 0f, 0f));
            ActorDefinition invalidDisplacementDefinition = CreateDefinition("Invalid.Displacement", new List<ActorBehaviorDefinition>(), new List<ActorMotionBindingDefinition> { invalidDisplacement });
            Assert.Throws<InvalidOperationException>(() => invalidDisplacementDefinition.ValidateOrThrow());
        }

        /// <summary>验证完整 Prefab 绑定能够解析 Hitbox、Motion 与 VFX，并强制 Hitbox 仅作为固定 Tick 主动查询的禁用形状。</summary>
        [Test]
        public void ActorAuthoring_WithCompleteBindings_ValidatesLookupAndDisabledHitboxOwnership()
        {
            ActorPresentationCueDefinition vfxCue = CreateCue("SlashVfx", ActorPresentationCueKind.Vfx, 2, 5, "Slash");
            ActorBehaviorDefinition behavior = CreateBehavior("Hero.Attack.01", new List<ActorSimulationClipDefinition> { CreateClip("Hit", SimulationClipKind.HitWindow, 2, 5, "Attack"), CreateClip("Motion", SimulationClipKind.Motion, 0, 6, "Lunge") }, new List<ActorPresentationVariantDefinition> { CreateVariant("Default", vfxCue) });
            ActorDefinition definition = CreateDefinition("Hero", new List<ActorBehaviorDefinition> { behavior }, new List<ActorMotionBindingDefinition> { CreateMotionBinding("Lunge", Vector3.forward * 0.1f) });
            GameObject actorObject = Track(new GameObject("AuthoringActor"));
            ActorAuthoringComponent authoring = actorObject.AddComponent<ActorAuthoringComponent>();
            GameObject hitboxObject = new GameObject("AttackHitbox");
            hitboxObject.transform.SetParent(actorObject.transform, false);
            BoxCollider hitbox = hitboxObject.AddComponent<BoxCollider>();
            hitbox.enabled = false;
            GameObject vfxObject = new GameObject("SlashVfx");
            vfxObject.transform.SetParent(actorObject.transform, false);
            vfxObject.SetActive(false);
            SetPrivateField(authoring, "definition", definition);
            SetPrivateField(authoring, "hitboxes", new List<ActorHitboxBinding> { CreateHitboxBinding("Attack", hitbox) });
            SetPrivateField(authoring, "vfxBindings", new List<ActorVfxBinding> { CreateVfxBinding("Slash", vfxObject) });
            Assert.DoesNotThrow(() => authoring.ValidateOrThrow());
            Assert.That(authoring.TryGetHitbox("Attack", out ActorHitboxBinding resolvedBinding), Is.True);
            Assert.That(resolvedBinding.Shape, Is.SameAs(hitbox));
            Assert.That(resolvedBinding.FacingRule, Is.EqualTo(ActorHitboxFacingRule.ShapeTransform));
            Assert.That(authoring.TryPlayVfx("Slash"), Is.True);
            Assert.That(vfxObject.activeSelf, Is.True);
            hitbox.enabled = true;
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
        }

        /// <summary>验证 Authoring 原样接受禁用的 Sphere 与 Capsule，并拒绝不受支持形状、启用形状和缺失 FacingRoot 的镜像绑定。</summary>
        [Test]
        public void ActorAuthoring_WithExactColliderShapes_ValidatesSupportedTypesAndFacingOwnership()
        {
            ActorDefinition definition = CreateDefinition("ShapeActor", new List<ActorBehaviorDefinition>(), new List<ActorMotionBindingDefinition>());
            GameObject actorObject = Track(new GameObject("ShapeActor"));
            ActorAuthoringComponent authoring = actorObject.AddComponent<ActorAuthoringComponent>();
            GameObject facingObject = new GameObject("FacingRoot");
            facingObject.transform.SetParent(actorObject.transform, false);
            GameObject sphereObject = new GameObject("SphereHitbox");
            sphereObject.transform.SetParent(actorObject.transform, false);
            SphereCollider sphere = sphereObject.AddComponent<SphereCollider>();
            sphere.enabled = false;
            GameObject capsuleObject = new GameObject("CapsuleHitbox");
            capsuleObject.transform.SetParent(actorObject.transform, false);
            CapsuleCollider capsule = capsuleObject.AddComponent<CapsuleCollider>();
            capsule.enabled = false;
            SetPrivateField(authoring, "definition", definition);
            SetPrivateField(authoring, "facingRoot", facingObject.transform);
            SetPrivateField(authoring, "hitboxes", new List<ActorHitboxBinding> { CreateHitboxBinding("Sphere", sphere, ActorHitboxFacingRule.MirrorWithFacingRoot), CreateHitboxBinding("Capsule", capsule, ActorHitboxFacingRule.MirrorWithFacingRoot) });
            Assert.DoesNotThrow(() => authoring.ValidateOrThrow());
            sphere.enabled = true;
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
            sphere.enabled = false;
            SetPrivateField(authoring, "facingRoot", null);
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
            SetPrivateField(authoring, "facingRoot", facingObject.transform);
            GameObject meshObject = new GameObject("MeshHitbox");
            meshObject.transform.SetParent(actorObject.transform, false);
            MeshCollider mesh = meshObject.AddComponent<MeshCollider>();
            mesh.enabled = false;
            SetPrivateField(authoring, "hitboxes", new List<ActorHitboxBinding> { CreateHitboxBinding("Mesh", mesh) });
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
        }

        /// <summary>验证直接位于 Actor 根下的方向性 Box 会绕显式 FacingRoot 从右朝向旋转到左朝向，而尺寸保持原 Collider 精度。</summary>
        [Test]
        public void HitQueryGeometry_WithMirroredRootSiblingBox_RotatesCenterAndOrientation()
        {
            GameObject actorObject = Track(new GameObject("MirroredActor"));
            actorObject.transform.SetPositionAndRotation(new Vector3(10f, 2f, -3f), Quaternion.Euler(0f, 30f, 0f));
            ActorAuthoringComponent authoring = actorObject.AddComponent<ActorAuthoringComponent>();
            GameObject facingObject = new GameObject("FacingRoot");
            facingObject.transform.SetParent(actorObject.transform, false);
            GameObject shapeObject = new GameObject("UltimateHitbox");
            shapeObject.transform.SetParent(actorObject.transform, false);
            shapeObject.transform.localPosition = new Vector3(2f, 0f, 0f);
            BoxCollider box = shapeObject.AddComponent<BoxCollider>();
            box.center = new Vector3(1f, 0.5f, 0f);
            box.size = new Vector3(4f, 2f, 1f);
            box.enabled = false;
            ActorHitboxBinding binding = CreateHitboxBinding("Ultimate", box, ActorHitboxFacingRule.MirrorWithFacingRoot);
            SetPrivateField(authoring, "facingRoot", facingObject.transform);
            SetPrivateField(authoring, "rightFacingRootLocalEulerAngles", Vector3.zero);
            facingObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            ActorHitQueryGeometry geometry = ActorHitQueryGeometryResolver.Resolve(authoring, binding);
            Vector3 expectedCenter = actorObject.transform.TransformPoint(new Vector3(-3f, 0.5f, 0f));
            Quaternion expectedRotation = actorObject.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            Assert.That(geometry.Kind, Is.EqualTo(ActorHitQueryShapeKind.Box));
            Assert.That(Vector3.Distance(geometry.Center, expectedCenter), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(geometry.Rotation, expectedRotation), Is.LessThan(0.0001f));
            Assert.That(geometry.HalfExtents, Is.EqualTo(new Vector3(2f, 1f, 0.5f)));
        }

        /// <summary>验证 Sphere 使用最大轴缩放半径且 Capsule 分离高度轴与垂直半径缩放，二者都不会退化为 Box 近似。</summary>
        [Test]
        public void HitQueryGeometry_WithSphereAndCapsule_PreservesExactShapeSemantics()
        {
            GameObject actorObject = Track(new GameObject("ExactShapeActor"));
            ActorAuthoringComponent authoring = actorObject.AddComponent<ActorAuthoringComponent>();
            GameObject sphereObject = new GameObject("SphereHitbox");
            sphereObject.transform.SetParent(actorObject.transform, false);
            sphereObject.transform.localScale = new Vector3(2f, 3f, 4f);
            SphereCollider sphere = sphereObject.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            sphere.enabled = false;
            ActorHitQueryGeometry sphereGeometry = ActorHitQueryGeometryResolver.Resolve(authoring, CreateHitboxBinding("Sphere", sphere));
            Assert.That(sphereGeometry.Kind, Is.EqualTo(ActorHitQueryShapeKind.Sphere));
            Assert.That(sphereGeometry.Radius, Is.EqualTo(2f).Within(0.0001f));
            GameObject capsuleObject = new GameObject("CapsuleHitbox");
            capsuleObject.transform.SetParent(actorObject.transform, false);
            capsuleObject.transform.localScale = new Vector3(2f, 3f, 4f);
            CapsuleCollider capsule = capsuleObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = 0.5f;
            capsule.height = 4f;
            capsule.enabled = false;
            ActorHitQueryGeometry capsuleGeometry = ActorHitQueryGeometryResolver.Resolve(authoring, CreateHitboxBinding("Capsule", capsule));
            Assert.That(capsuleGeometry.Kind, Is.EqualTo(ActorHitQueryShapeKind.Capsule));
            Assert.That(capsuleGeometry.Radius, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(Vector3.Distance(capsuleGeometry.CapsulePointA, capsuleGeometry.CapsulePointB), Is.EqualTo(8f).Within(0.0001f));
        }

        /// <summary>验证 Prefab 的跨资产引用必须全部闭环解析，防止行为中的 Motion 或 VFX 稳定编号静默落空。</summary>
        [Test]
        public void ActorAuthoring_WithUnresolvedBehaviorBindings_RejectsMotionAndVfxReferences()
        {
            ActorBehaviorDefinition missingMotionBehavior = CreateBehavior("Invalid.MotionReference", new List<ActorSimulationClipDefinition> { CreateClip("Motion", SimulationClipKind.Motion, 0, 6, "MissingMotion") }, CreateDefaultVariants());
            ActorDefinition missingMotionDefinition = CreateDefinition("Invalid.MotionActor", new List<ActorBehaviorDefinition> { missingMotionBehavior }, new List<ActorMotionBindingDefinition>());
            ActorAuthoringComponent authoring = CreateAuthoringComponent(missingMotionDefinition);
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
            ActorPresentationCueDefinition missingVfxCue = CreateCue("MissingVfx", ActorPresentationCueKind.Vfx, 1, 3, "MissingVfx");
            ActorBehaviorDefinition missingVfxBehavior = CreateBehavior("Invalid.VfxReference", new List<ActorSimulationClipDefinition>(), new List<ActorPresentationVariantDefinition> { CreateVariant("Default", missingVfxCue) });
            ActorDefinition missingVfxDefinition = CreateDefinition("Invalid.VfxActor", new List<ActorBehaviorDefinition> { missingVfxBehavior }, new List<ActorMotionBindingDefinition>());
            SetPrivateField(authoring, "definition", missingVfxDefinition);
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
            ActorBehaviorDefinition missingMotionVariantBehavior = CreateBehavior("Invalid.MotionVariantReference", new List<ActorSimulationClipDefinition> { CreateClip("Motion", SimulationClipKind.Motion, 0, 6, "VariantMotion") }, CreateDefaultVariants());
            ActorMotionBindingDefinition variantMotion = CreateMotionBinding("VariantMotion", Vector3.forward * 0.1f);
            SetPrivateField(variantMotion, "requiredVariantId", "Moving");
            ActorDefinition missingMotionVariantDefinition = CreateDefinition("Invalid.MotionVariantActor", new List<ActorBehaviorDefinition> { missingMotionVariantBehavior }, new List<ActorMotionBindingDefinition> { variantMotion });
            SetPrivateField(authoring, "definition", missingMotionVariantDefinition);
            Assert.Throws<InvalidOperationException>(() => authoring.ValidateOrThrow());
        }

        /// <summary>创建包含四类模拟片段与默认表现降级路径的合法行为资产。</summary>
        private ActorBehaviorDefinition CreateValidBehavior(string behaviorId, bool includeMovingVariant)
        {
            var clips = new List<ActorSimulationClipDefinition> { CreateClip("Hit", SimulationClipKind.HitWindow, 2, 5, "Attack"), CreateCapabilityClip("Block", 0, 8, ActorCapability.Move | ActorCapability.Rotate), CreateClip("Commit", SimulationClipKind.GameplayEvent, 6, 7, "CommitCost"), CreateClip("Motion", SimulationClipKind.Motion, 0, 12, "Lunge") };
            List<ActorPresentationVariantDefinition> variants = CreateDefaultVariants();
            if (includeMovingVariant) variants.Add(CreateVariant("Moving"));
            return CreateBehavior(behaviorId, clips, variants);
        }

        /// <summary>创建仅包含指定 Cue 的行为资产，用于隔离表现资源验证规则。</summary>
        private ActorBehaviorDefinition CreateBehaviorWithCue(string behaviorId, ActorPresentationCueDefinition cue)
        {
            return CreateBehavior(behaviorId, new List<ActorSimulationClipDefinition>(), new List<ActorPresentationVariantDefinition> { CreateVariant("Default", cue) });
        }

        /// <summary>创建一个具有合法基础字段的临时行为资产，并写入指定模拟片段和表现变体。</summary>
        private ActorBehaviorDefinition CreateBehavior(string behaviorId, List<ActorSimulationClipDefinition> clips, List<ActorPresentationVariantDefinition> variants)
        {
            ActorBehaviorDefinition behavior = Track(ScriptableObject.CreateInstance<ActorBehaviorDefinition>());
            behavior.name = behaviorId;
            SetPrivateField(behavior, "behaviorId", behaviorId);
            SetPrivateField(behavior, "durationTicks", 12);
            SetPrivateField(behavior, "chainFromTick", 6);
            SetPrivateField(behavior, "simulationClips", clips);
            SetPrivateField(behavior, "presentationVariants", variants);
            return behavior;
        }

        /// <summary>创建需要稳定绑定编号的普通模拟片段配置。</summary>
        private static ActorSimulationClipDefinition CreateClip(string clipId, SimulationClipKind kind, int startTick, int endTick, string bindingId)
        {
            var clip = new ActorSimulationClipDefinition();
            SetPrivateField(clip, "clipId", clipId);
            SetPrivateField(clip, "kind", kind);
            SetPrivateField(clip, "startTick", startTick);
            SetPrivateField(clip, "endTick", endTick);
            SetPrivateField(clip, "bindingId", bindingId);
            return clip;
        }

        /// <summary>创建阻塞指定能力集合的模拟片段配置。</summary>
        private static ActorSimulationClipDefinition CreateCapabilityClip(string clipId, int startTick, int endTick, ActorCapability capabilities)
        {
            ActorSimulationClipDefinition clip = CreateClip(clipId, SimulationClipKind.CapabilityBlock, startTick, endTick, null);
            SetPrivateField(clip, "blockedCapabilities", capabilities);
            return clip;
        }

        /// <summary>创建具有指定类型、区间和稳定绑定的表现 Cue。</summary>
        private static ActorPresentationCueDefinition CreateCue(string cueId, ActorPresentationCueKind kind, int startTick, int endTick, string bindingId)
        {
            var cue = new ActorPresentationCueDefinition();
            SetPrivateField(cue, "cueId", cueId);
            SetPrivateField(cue, "kind", kind);
            SetPrivateField(cue, "startTick", startTick);
            SetPrivateField(cue, "endTick", endTick);
            SetPrivateField(cue, "bindingId", bindingId);
            return cue;
        }

        /// <summary>创建指定稳定编号和 Cue 集合的表现变体。</summary>
        private static ActorPresentationVariantDefinition CreateVariant(string variantId, params ActorPresentationCueDefinition[] cues)
        {
            var variant = new ActorPresentationVariantDefinition();
            SetPrivateField(variant, "variantId", variantId);
            SetPrivateField(variant, "cues", new List<ActorPresentationCueDefinition>(cues));
            return variant;
        }

        /// <summary>创建仅包含必需 Default 降级路径的表现变体集合。</summary>
        private static List<ActorPresentationVariantDefinition> CreateDefaultVariants()
        {
            return new List<ActorPresentationVariantDefinition> { CreateVariant("Default") };
        }

        /// <summary>创建具备运动模型、行为集合和位移绑定的临时对象定义。</summary>
        private ActorDefinition CreateDefinition(string actorId, List<ActorBehaviorDefinition> behaviors, List<ActorMotionBindingDefinition> motionBindings)
        {
            ActorDefinition definition = Track(ScriptableObject.CreateInstance<ActorDefinition>());
            CharacterControllerMotionModelDefinition motionModel = Track(ScriptableObject.CreateInstance<CharacterControllerMotionModelDefinition>());
            definition.name = actorId;
            SetPrivateField(definition, "actorId", actorId);
            SetPrivateField(definition, "motionModel", motionModel);
            SetPrivateField(definition, "behaviors", behaviors);
            SetPrivateField(definition, "motionBindings", motionBindings);
            return definition;
        }

        /// <summary>创建稳定编号到每 Tick 局部位移的对象定义绑定。</summary>
        private static ActorMotionBindingDefinition CreateMotionBinding(string motionId, Vector3 displacement)
        {
            var binding = new ActorMotionBindingDefinition();
            SetPrivateField(binding, "motionId", motionId);
            SetPrivateField(binding, "localDisplacementPerBehaviorTick", displacement);
            return binding;
        }

        /// <summary>创建稳定编号到禁用 Collider 查询形状及显式朝向规则的 Prefab 绑定。</summary>
        private static ActorHitboxBinding CreateHitboxBinding(string bindingId, Collider shape, ActorHitboxFacingRule facingRule = ActorHitboxFacingRule.ShapeTransform)
        {
            var binding = new ActorHitboxBinding();
            SetPrivateField(binding, "bindingId", bindingId);
            SetPrivateField(binding, "shape", shape);
            SetPrivateField(binding, "facingRule", facingRule);
            return binding;
        }

        /// <summary>创建稳定编号到 VFX 根对象的 Prefab 绑定。</summary>
        private static ActorVfxBinding CreateVfxBinding(string bindingId, GameObject visualRoot)
        {
            var binding = new ActorVfxBinding();
            SetPrivateField(binding, "bindingId", bindingId);
            SetPrivateField(binding, "visualRoot", visualRoot);
            return binding;
        }

        /// <summary>创建未声明任何 Prefab 局部绑定的 Authoring 组件，用于验证引用闭环失败。</summary>
        private ActorAuthoringComponent CreateAuthoringComponent(ActorDefinition definition)
        {
            GameObject actorObject = Track(new GameObject("InvalidAuthoringActor"));
            ActorAuthoringComponent authoring = actorObject.AddComponent<ActorAuthoringComponent>();
            SetPrivateField(authoring, "definition", definition);
            SetPrivateField(authoring, "hitboxes", new List<ActorHitboxBinding>());
            SetPrivateField(authoring, "vfxBindings", new List<ActorVfxBinding>());
            return authoring;
        }

        /// <summary>记录测试创建的 Unity 对象并原样返回，便于资产工厂保持单行调用。</summary>
        private T Track<T>(T createdObject) where T : UnityEngine.Object
        {
            createdObjects.Add(createdObject);
            return createdObject;
        }

        /// <summary>仅在测试侧写入 Unity 序列化私有字段，使生产资产类型不需要暴露可变 API 或测试钩子。</summary>
        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Test configuration field '{target.GetType().FullName}.{fieldName}' was not found.");
            field.SetValue(target, value);
        }
    }
}
