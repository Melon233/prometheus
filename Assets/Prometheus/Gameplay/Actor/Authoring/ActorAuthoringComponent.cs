using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus.Actor
{
    /// <summary>声明命中查询形状如何响应 Actor 的显式朝向根节点。</summary>
    public enum ActorHitboxFacingRule
    {
        /// <summary>直接采用 Collider 当前世界变换；适用于已经位于 RotateRoot 等朝向层级下的形状和不具有方向性的形状。</summary>
        ShapeTransform = 0,
        /// <summary>以 FacingRoot 的右朝向姿态为基准，把不在朝向层级下的 Collider 查询姿态旋转到当前朝向。</summary>
        MirrorWithFacingRoot = 1
    }

    /// <summary>把一个稳定 Hitbox 编号映射到 Prefab 中仅作为查询形状使用的 Collider，并显式声明朝向规则。</summary>
    [Serializable]
    public sealed class ActorHitboxBinding
    {
        [SerializeField] private string bindingId = "Attack";
        [SerializeField] private Collider shape;
        [SerializeField] private ActorHitboxFacingRule facingRule;

        /// <summary>获取 Hitbox 稳定编号。</summary>
        public string BindingId => bindingId;

        /// <summary>获取用于主动物理查询的 BoxCollider、SphereCollider 或 CapsuleCollider 原始形状。</summary>
        public Collider Shape => shape;

        /// <summary>获取当前形状相对显式 FacingRoot 的朝向处理规则。</summary>
        public ActorHitboxFacingRule FacingRule => facingRule;
    }

    /// <summary>把一个稳定 VFX 编号映射到 Prefab 中的表现根对象。</summary>
    [Serializable]
    public sealed class ActorVfxBinding
    {
        [SerializeField] private string bindingId = "Vfx";
        [SerializeField] private GameObject visualRoot;

        /// <summary>获取 VFX 稳定编号。</summary>
        public string BindingId => bindingId;

        /// <summary>获取运行时重新触发的表现根对象。</summary>
        public GameObject VisualRoot => visualRoot;
    }

    /// <summary>
    /// 作为 Prefab 与可复用 ActorDefinition 之间唯一显式场景绑定，避免 Definition 反向引用 Prefab 形成资产循环。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorAuthoringComponent : MonoComponent
    {
        [SerializeField] private ActorDefinition definition;
        [SerializeField] private CameraSubject cameraSubject;
        [SerializeField] private Transform facingRoot;
        [SerializeField] private Vector3 rightFacingRootLocalEulerAngles;
        [SerializeField] private List<ActorHitboxBinding> hitboxes = new List<ActorHitboxBinding>();
        [SerializeField] private List<ActorVfxBinding> vfxBindings = new List<ActorVfxBinding>();

        /// <summary>获取当前 Prefab 使用的只读对象定义。</summary>
        public ActorDefinition Definition => definition;

        /// <summary>获取可选镜头目标，并在未显式填写时查询同一对象。</summary>
        public CameraSubject CameraSubject => cameraSubject != null ? cameraSubject : cameraSubject = GetComponent<CameraSubject>();

        /// <summary>获取方向性查询使用的显式朝向根节点；只有声明 MirrorWithFacingRoot 的绑定必须配置此引用。</summary>
        public Transform FacingRoot => facingRoot;

        /// <summary>获取 FacingRoot 表示右朝向时的局部旋转基准，运行时以当前局部旋转与该基准的差值镜像查询姿态。</summary>
        public Quaternion RightFacingRootLocalRotation => Quaternion.Euler(rightFacingRootLocalEulerAngles);

        /// <summary>按稳定编号解析完整 Hitbox 绑定，使查询运行时同时获得精确 Collider 类型与显式朝向规则。</summary>
        public bool TryGetHitbox(string bindingId, out ActorHitboxBinding binding)
        {
            for (int index = 0; index < hitboxes.Count; index++)
            {
                ActorHitboxBinding candidate = hitboxes[index];
                if (candidate != null && string.Equals(candidate.BindingId, bindingId, StringComparison.Ordinal) && candidate.Shape != null)
                {
                    binding = candidate;
                    return true;
                }
            }
            binding = null;
            return false;
        }

        /// <summary>按稳定编号重新触发一个绑定到 Prefab 的 VFX。</summary>
        public bool TryPlayVfx(string bindingId)
        {
            for (int index = 0; index < vfxBindings.Count; index++)
            {
                ActorVfxBinding binding = vfxBindings[index];
                if (binding == null || !string.Equals(binding.BindingId, bindingId, StringComparison.Ordinal) || binding.VisualRoot == null) continue;
                binding.VisualRoot.SetActive(false);
                binding.VisualRoot.SetActive(true);
                return true;
            }
            return false;
        }

        /// <summary>验证 Prefab 场景绑定和所有稳定编号的唯一性。</summary>
        public void ValidateOrThrow()
        {
            if (definition == null) throw new InvalidOperationException($"Actor authoring component on '{name}' requires an ActorDefinition.");
            definition.ValidateOrThrow();
            ValidateBindingIds(hitboxes, binding => binding == null ? null : binding.BindingId, "Hitbox");
            ValidateBindingIds(vfxBindings, binding => binding == null ? null : binding.BindingId, "VFX");
            for (int index = 0; index < hitboxes.Count; index++)
            {
                ActorHitboxBinding binding = hitboxes[index];
                if (binding.Shape == null) throw new InvalidOperationException($"Actor '{name}' Hitbox binding '{binding.BindingId}' requires a Collider shape.");
                if (!(binding.Shape is BoxCollider) && !(binding.Shape is SphereCollider) && !(binding.Shape is CapsuleCollider)) throw new InvalidOperationException($"Actor '{name}' Hitbox binding '{binding.BindingId}' uses unsupported Collider type '{binding.Shape.GetType().Name}'; only BoxCollider, SphereCollider and CapsuleCollider are supported.");
                if (!binding.Shape.transform.IsChildOf(transform)) throw new InvalidOperationException($"Actor '{name}' Hitbox shape '{binding.BindingId}' must belong to the ActorAuthoring hierarchy.");
                if (binding.Shape.enabled) throw new InvalidOperationException($"Actor '{name}' Hitbox shape '{binding.BindingId}' must remain disabled because fixed Tick queries own hit detection.");
                ValidateShapeDimensions(binding);
                ValidateFacingRule(binding);
            }
            for (int index = 0; index < vfxBindings.Count; index++)
            {
                ActorVfxBinding binding = vfxBindings[index];
                if (binding.VisualRoot == null) throw new InvalidOperationException($"Actor '{name}' VFX binding '{binding.BindingId}' requires a visual root.");
            }
            ValidateBehaviorBindings();
        }

        /// <summary>验证具体 Collider 的几何尺寸在 Physics.Overlap 查询中具有明确且非退化的含义。</summary>
        private void ValidateShapeDimensions(ActorHitboxBinding binding)
        {
            if (binding.Shape is BoxCollider box && (box.size.x <= 0f || box.size.y <= 0f || box.size.z <= 0f)) throw new InvalidOperationException($"Actor '{name}' Box Hitbox '{binding.BindingId}' requires a positive size on every axis.");
            if (binding.Shape is SphereCollider sphere && sphere.radius <= 0f) throw new InvalidOperationException($"Actor '{name}' Sphere Hitbox '{binding.BindingId}' requires a positive radius.");
            if (binding.Shape is CapsuleCollider capsule && (capsule.radius <= 0f || capsule.height <= 0f || capsule.direction < 0 || capsule.direction > 2)) throw new InvalidOperationException($"Actor '{name}' Capsule Hitbox '{binding.BindingId}' requires a positive radius and height plus a valid X, Y or Z direction.");
        }

        /// <summary>验证显式朝向镜像不会与 Collider 已经继承的 FacingRoot 变换重复叠加。</summary>
        private void ValidateFacingRule(ActorHitboxBinding binding)
        {
            if (binding.FacingRule != ActorHitboxFacingRule.ShapeTransform && binding.FacingRule != ActorHitboxFacingRule.MirrorWithFacingRoot) throw new InvalidOperationException($"Actor '{name}' Hitbox '{binding.BindingId}' uses unsupported facing rule '{binding.FacingRule}'.");
            if (binding.FacingRule != ActorHitboxFacingRule.MirrorWithFacingRoot) return;
            if (facingRoot == null) throw new InvalidOperationException($"Actor '{name}' Hitbox '{binding.BindingId}' requires an explicit FacingRoot for mirrored queries.");
            if (!facingRoot.IsChildOf(transform)) throw new InvalidOperationException($"Actor '{name}' FacingRoot must belong to the ActorAuthoring hierarchy.");
            if (binding.Shape.transform == facingRoot || binding.Shape.transform.IsChildOf(facingRoot)) throw new InvalidOperationException($"Actor '{name}' Hitbox '{binding.BindingId}' already inherits FacingRoot and must use ShapeTransform to avoid applying facing twice.");
            if (!IsFinite(rightFacingRootLocalEulerAngles)) throw new InvalidOperationException($"Actor '{name}' FacingRoot right-facing Euler angles must contain finite values.");
        }

        /// <summary>判断一个三维向量是否可以安全参与 Quaternion 和查询姿态计算。</summary>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        /// <summary>验证所有行为引用的 Hitbox、Motion 和 VFX 稳定编号都能在当前 Definition 或 Prefab 上解析。</summary>
        private void ValidateBehaviorBindings()
        {
            IReadOnlyList<ActorBehaviorDefinition> behaviors = definition.Behaviors;
            for (int behaviorIndex = 0; behaviorIndex < behaviors.Count; behaviorIndex++)
            {
                ActorBehaviorDefinition behavior = behaviors[behaviorIndex];
                for (int clipIndex = 0; clipIndex < behavior.SimulationClips.Count; clipIndex++)
                {
                    ActorSimulationClipDefinition clip = behavior.SimulationClips[clipIndex];
                    if (clip.Kind == SimulationClipKind.HitWindow && !TryGetHitbox(clip.BindingId, out _)) throw new InvalidOperationException($"Actor '{name}' behavior '{behavior.BehaviorId}' cannot resolve Hitbox '{clip.BindingId}'.");
                    if (clip.Kind == SimulationClipKind.Motion)
                    {
                        if (!definition.TryGetMotionBinding(clip.BindingId, out ActorMotionBindingDefinition motionBinding)) throw new InvalidOperationException($"Actor '{name}' behavior '{behavior.BehaviorId}' cannot resolve Motion '{clip.BindingId}'.");
                        if (!string.IsNullOrEmpty(motionBinding.RequiredVariantId) && !behavior.TryGetPresentationVariant(motionBinding.RequiredVariantId, out _)) throw new InvalidOperationException($"Actor '{name}' behavior '{behavior.BehaviorId}' motion '{clip.BindingId}' requires missing presentation variant '{motionBinding.RequiredVariantId}'.");
                    }
                }
                for (int variantIndex = 0; variantIndex < behavior.PresentationVariants.Count; variantIndex++)
                {
                    ActorPresentationVariantDefinition variant = behavior.PresentationVariants[variantIndex];
                    for (int cueIndex = 0; cueIndex < variant.Cues.Count; cueIndex++)
                    {
                        ActorPresentationCueDefinition cue = variant.Cues[cueIndex];
                        if (cue.Kind == ActorPresentationCueKind.Vfx && !HasVfxBinding(cue.BindingId)) throw new InvalidOperationException($"Actor '{name}' behavior '{behavior.BehaviorId}' cannot resolve VFX '{cue.BindingId}'.");
                    }
                }
            }
        }

        /// <summary>判断当前 Prefab 是否声明指定 VFX 稳定绑定。</summary>
        private bool HasVfxBinding(string bindingId)
        {
            for (int index = 0; index < vfxBindings.Count; index++)
            {
                ActorVfxBinding binding = vfxBindings[index];
                if (binding != null && string.Equals(binding.BindingId, bindingId, StringComparison.Ordinal) && binding.VisualRoot != null) return true;
            }
            return false;
        }

        /// <summary>验证一组场景绑定不包含空元素、空编号或重复编号。</summary>
        private void ValidateBindingIds<TBinding>(IReadOnlyList<TBinding> bindings, Func<TBinding, string> idSelector, string bindingKind)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < bindings.Count; index++)
            {
                string bindingId = idSelector(bindings[index]);
                if (string.IsNullOrWhiteSpace(bindingId) || !ids.Add(bindingId)) throw new InvalidOperationException($"Actor '{name}' contains an empty or duplicate {bindingKind} binding ID '{bindingId}'.");
            }
        }
    }
}
