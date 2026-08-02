using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Component;
using Xuan.Prometheus.Effects;
using Xuan.Prometheus.Logic;

namespace Xuan.Prometheus.Actor
{
    /// <summary>抽象攻击者与候选目标之间的动态关系判断，使 PVP TeamId、魅惑和临时同盟无需改写物理查询核心。</summary>
    public interface IActorHitTargetRelationResolver
    {
        /// <summary>判断候选目标是否满足当前行为资产声明的目标阵营语义。</summary>
        bool CanTarget(Entity sourceEntity, ActorAuthoringComponent sourceAuthoring, Entity targetEntity, ActorAuthoringComponent targetAuthoring, ActorFactionMask authoredTargetFactions);
    }

    /// <summary>提供当前客户端默认的静态 ActorDefinition.Faction 关系判断；动态玩法可以通过构造参数替换此实现。</summary>
    public sealed class ActorFactionHitTargetRelationResolver : IActorHitTargetRelationResolver
    {
        /// <summary>获取无状态默认实例，避免每个 Actor 分配重复策略对象。</summary>
        public static ActorFactionHitTargetRelationResolver Instance { get; } = new ActorFactionHitTargetRelationResolver();

        /// <summary>限制外部创建重复实例；扩展玩法应实现接口而不是继承默认策略。</summary>
        private ActorFactionHitTargetRelationResolver()
        {
        }

        /// <inheritdoc/>
        public bool CanTarget(Entity sourceEntity, ActorAuthoringComponent sourceAuthoring, Entity targetEntity, ActorAuthoringComponent targetAuthoring, ActorFactionMask authoredTargetFactions)
        {
            return targetAuthoring != null && targetAuthoring.Definition != null && authoredTargetFactions.Contains(targetAuthoring.Definition.Faction);
        }
    }

    /// <summary>标识固定 Tick 主动命中查询最终使用的精确 Unity 物理形状。</summary>
    public enum ActorHitQueryShapeKind
    {
        /// <summary>由 BoxCollider 解析出的有向包围盒。</summary>
        Box = 0,
        /// <summary>由 SphereCollider 解析出的球体。</summary>
        Sphere = 1,
        /// <summary>由 CapsuleCollider 解析出的胶囊体。</summary>
        Capsule = 2
    }

    /// <summary>保存一个 Collider 在当前世界姿态与 Actor 朝向规则下解析出的不可变查询几何。</summary>
    public readonly struct ActorHitQueryGeometry
    {
        private ActorHitQueryGeometry(ActorHitQueryShapeKind kind, Vector3 center, Quaternion rotation, Vector3 halfExtents, float radius, Vector3 capsulePointA, Vector3 capsulePointB)
        {
            Kind = kind;
            Center = center;
            Rotation = rotation;
            HalfExtents = halfExtents;
            Radius = radius;
            CapsulePointA = capsulePointA;
            CapsulePointB = capsulePointB;
        }

        /// <summary>获取需要调用的 Physics.Overlap 查询种类。</summary>
        public ActorHitQueryShapeKind Kind { get; }

        /// <summary>获取 Box 或 Sphere 查询中心；Capsule 查询时该值用于命中位置计算与诊断。</summary>
        public Vector3 Center { get; }

        /// <summary>获取 Box 查询旋转；其他形状返回单位旋转。</summary>
        public Quaternion Rotation { get; }

        /// <summary>获取 Box 查询半尺寸；其他形状返回零向量。</summary>
        public Vector3 HalfExtents { get; }

        /// <summary>获取 Sphere 或 Capsule 查询半径；Box 返回零。</summary>
        public float Radius { get; }

        /// <summary>获取 Capsule 中轴线的第一个球心；其他形状返回查询中心。</summary>
        public Vector3 CapsulePointA { get; }

        /// <summary>获取 Capsule 中轴线的第二个球心；其他形状返回查询中心。</summary>
        public Vector3 CapsulePointB { get; }

        /// <summary>创建一个精确 OverlapBox 查询几何。</summary>
        internal static ActorHitQueryGeometry CreateBox(Vector3 center, Quaternion rotation, Vector3 halfExtents)
        {
            return new ActorHitQueryGeometry(ActorHitQueryShapeKind.Box, center, rotation, halfExtents, 0f, center, center);
        }

        /// <summary>创建一个精确 OverlapSphere 查询几何。</summary>
        internal static ActorHitQueryGeometry CreateSphere(Vector3 center, float radius)
        {
            return new ActorHitQueryGeometry(ActorHitQueryShapeKind.Sphere, center, Quaternion.identity, Vector3.zero, radius, center, center);
        }

        /// <summary>创建一个精确 OverlapCapsule 查询几何。</summary>
        internal static ActorHitQueryGeometry CreateCapsule(Vector3 center, Vector3 pointA, Vector3 pointB, float radius)
        {
            return new ActorHitQueryGeometry(ActorHitQueryShapeKind.Capsule, center, Quaternion.identity, Vector3.zero, radius, pointA, pointB);
        }
    }

    /// <summary>把 Prefab 中禁用的 Collider 与显式朝向规则解析为可测试、可直接提交给 Physics 的世界几何。</summary>
    public static class ActorHitQueryGeometryResolver
    {
        /// <summary>按 Collider 的真实类型解析 Box、Sphere 或 Capsule，不使用跨形状近似。</summary>
        public static ActorHitQueryGeometry Resolve(ActorAuthoringComponent authoring, ActorHitboxBinding binding)
        {
            if (authoring == null) throw new ArgumentNullException(nameof(authoring));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            Collider shape = binding.Shape != null ? binding.Shape : throw new InvalidOperationException($"Actor '{authoring.name}' Hitbox '{binding.BindingId}' has no Collider shape.");
            ResolveFacing(authoring, binding, out Vector3 facingPivot, out Quaternion facingDelta);
            if (shape is BoxCollider box) return ResolveBox(box, facingPivot, facingDelta);
            if (shape is SphereCollider sphere) return ResolveSphere(sphere, facingPivot, facingDelta);
            if (shape is CapsuleCollider capsule) return ResolveCapsule(capsule, facingPivot, facingDelta);
            throw new NotSupportedException($"Actor '{authoring.name}' Hitbox '{binding.BindingId}' uses unsupported Collider type '{shape.GetType().Name}'.");
        }

        /// <summary>解析 BoxCollider 的世界中心、旋转和按绝对缩放计算的半尺寸。</summary>
        private static ActorHitQueryGeometry ResolveBox(BoxCollider shape, Vector3 facingPivot, Quaternion facingDelta)
        {
            Transform shapeTransform = shape.transform;
            Vector3 center = ApplyFacing(shapeTransform.TransformPoint(shape.center), facingPivot, facingDelta);
            Vector3 scale = Abs(shapeTransform.lossyScale);
            Vector3 halfExtents = Vector3.Scale(shape.size * 0.5f, scale);
            Quaternion rotation = facingDelta * shapeTransform.rotation;
            return ActorHitQueryGeometry.CreateBox(center, rotation, halfExtents);
        }

        /// <summary>解析 SphereCollider 的世界中心，并按照 Unity 语义使用最大绝对轴缩放半径。</summary>
        private static ActorHitQueryGeometry ResolveSphere(SphereCollider shape, Vector3 facingPivot, Quaternion facingDelta)
        {
            Transform shapeTransform = shape.transform;
            Vector3 center = ApplyFacing(shapeTransform.TransformPoint(shape.center), facingPivot, facingDelta);
            Vector3 scale = Abs(shapeTransform.lossyScale);
            float radius = shape.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            return ActorHitQueryGeometry.CreateSphere(center, radius);
        }

        /// <summary>解析 CapsuleCollider 的世界中轴线端点与半径，分别处理轴向和垂直方向的非均匀缩放。</summary>
        private static ActorHitQueryGeometry ResolveCapsule(CapsuleCollider shape, Vector3 facingPivot, Quaternion facingDelta)
        {
            Transform shapeTransform = shape.transform;
            Vector3 center = ApplyFacing(shapeTransform.TransformPoint(shape.center), facingPivot, facingDelta);
            Vector3 scale = Abs(shapeTransform.lossyScale);
            Vector3 localAxis = ResolveCapsuleLocalAxis(shape.direction);
            Vector3 worldAxis = facingDelta * shapeTransform.TransformDirection(localAxis).normalized;
            ResolveCapsuleScales(scale, shape.direction, out float axisScale, out float radiusScale);
            float radius = shape.radius * radiusScale;
            float worldHeight = Mathf.Max(shape.height * axisScale, radius * 2f);
            float halfSegmentLength = Mathf.Max(0f, worldHeight * 0.5f - radius);
            Vector3 pointA = center + worldAxis * halfSegmentLength;
            Vector3 pointB = center - worldAxis * halfSegmentLength;
            return ActorHitQueryGeometry.CreateCapsule(center, pointA, pointB, radius);
        }

        /// <summary>解析绑定的镜像规则，并把 FacingRoot 当前旋转相对右朝向基准转换到世界空间。</summary>
        private static void ResolveFacing(ActorAuthoringComponent authoring, ActorHitboxBinding binding, out Vector3 pivot, out Quaternion delta)
        {
            if (binding.FacingRule == ActorHitboxFacingRule.ShapeTransform)
            {
                pivot = Vector3.zero;
                delta = Quaternion.identity;
                return;
            }
            if (binding.FacingRule != ActorHitboxFacingRule.MirrorWithFacingRoot) throw new ArgumentOutOfRangeException(nameof(binding), binding.FacingRule, "Unsupported Actor Hitbox facing rule.");
            Transform facingRoot = authoring.FacingRoot != null ? authoring.FacingRoot : throw new InvalidOperationException($"Actor '{authoring.name}' Hitbox '{binding.BindingId}' requires an explicit FacingRoot.");
            Quaternion parentRotation = facingRoot.parent != null ? facingRoot.parent.rotation : Quaternion.identity;
            Quaternion localDelta = facingRoot.localRotation * Quaternion.Inverse(authoring.RightFacingRootLocalRotation);
            pivot = facingRoot.position;
            delta = parentRotation * localDelta * Quaternion.Inverse(parentRotation);
        }

        /// <summary>绕 FacingRoot 的世界枢轴旋转一个查询点；无镜像规则时单位旋转保持原值。</summary>
        private static Vector3 ApplyFacing(Vector3 point, Vector3 pivot, Quaternion delta)
        {
            return pivot + delta * (point - pivot);
        }

        /// <summary>返回向量每个轴的绝对值，使负缩放不会生成无效查询尺寸。</summary>
        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        /// <summary>把 CapsuleCollider.direction 转换为本地中轴线。</summary>
        private static Vector3 ResolveCapsuleLocalAxis(int direction)
        {
            switch (direction)
            {
                case 0: return Vector3.right;
                case 1: return Vector3.up;
                case 2: return Vector3.forward;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, "Capsule direction must be X, Y or Z.");
            }
        }

        /// <summary>分别解析 Capsule 高度轴缩放与垂直平面最大缩放，匹配 Unity 胶囊体的半径规则。</summary>
        private static void ResolveCapsuleScales(Vector3 scale, int direction, out float axisScale, out float radiusScale)
        {
            switch (direction)
            {
                case 0:
                    axisScale = scale.x;
                    radiusScale = Mathf.Max(scale.y, scale.z);
                    return;
                case 1:
                    axisScale = scale.y;
                    radiusScale = Mathf.Max(scale.x, scale.z);
                    return;
                case 2:
                    axisScale = scale.z;
                    radiusScale = Mathf.Max(scale.x, scale.y);
                    return;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, "Capsule direction must be X, Y or Z.");
            }
        }
    }

    /// <summary>使用 Prefab 中禁用的精确 Collider 作为只读形状资产，在固定 Tick 主动查询并通过现有 EffectSignal 战斗链发布唯一命中。</summary>
    public sealed class ActorHitQueryRuntime : IDisposable
    {
        private readonly ActorAuthoringComponent authoring;
        private readonly Entity sourceEntity;
        private readonly PropertyComponent sourceProperty;
        private readonly EffectComponent sourceEffect;
        private readonly IActorHitTargetRelationResolver targetRelationResolver;
        private readonly Collider[] overlapBuffer;
        private readonly Dictionary<HitWindowKey, HashSet<int>> hitTargetsByWindow = new Dictionary<HitWindowKey, HashSet<int>>();
        private readonly List<PendingEffectSignal> pendingSignals = new List<PendingEffectSignal>();
        private bool disposed;

        /// <summary>创建一个只属于单个攻击者的主动 Hitbox 查询运行时。</summary>
        public ActorHitQueryRuntime(ActorAuthoringComponent authoring, Entity sourceEntity, PropertyComponent sourceProperty, EffectComponent sourceEffect, int queryCapacity = 32, IActorHitTargetRelationResolver targetRelationResolver = null)
        {
            this.authoring = authoring != null ? authoring : throw new ArgumentNullException(nameof(authoring));
            this.sourceEntity = sourceEntity ?? throw new ArgumentNullException(nameof(sourceEntity));
            this.sourceProperty = sourceProperty != null ? sourceProperty : throw new ArgumentNullException(nameof(sourceProperty));
            this.sourceEffect = sourceEffect != null ? sourceEffect : throw new ArgumentNullException(nameof(sourceEffect));
            this.targetRelationResolver = targetRelationResolver ?? ActorFactionHitTargetRelationResolver.Instance;
            if (queryCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(queryCapacity), queryCapacity, "Hit-query capacity must be positive.");
            overlapBuffer = new Collider[queryCapacity];
        }

        /// <summary>为一个具体行为实例中的 HitWindow 建立独立命中去重集合，使同名 Clip 的连段交接互不冲突。</summary>
        public void Open(BehaviorHandle handle, HitWindowClip clip)
        {
            ThrowIfDisposed();
            HitWindowKey key = CreateWindowKey(handle, clip);
            if (hitTargetsByWindow.ContainsKey(key)) throw new InvalidOperationException($"Hit window '{clip.ClipId}' is already open for behavior instance '{handle.InstanceId}' on actor '{authoring.name}'.");
            hitTargetsByWindow.Add(key, new HashSet<int>());
        }

        /// <summary>查询一个具体行为实例当前 Hitbox 内的新目标，并为该窗口中的每个目标收集一次延迟提交的 HitConfirmed。</summary>
        public void Sample(BehaviorHandle handle, HitWindowClip clip, ActorHitSignalDefinition signalDefinition)
        {
            ThrowIfDisposed();
            HitWindowKey key = CreateWindowKey(handle, clip);
            if (signalDefinition == null) throw new ArgumentNullException(nameof(signalDefinition));
            if (!hitTargetsByWindow.TryGetValue(key, out HashSet<int> hitTargets)) throw new InvalidOperationException($"Hit window '{clip.ClipId}' for behavior instance '{handle.InstanceId}' must be opened before it can be sampled.");
            if (!authoring.TryGetHitbox(clip.HitboxId, out ActorHitboxBinding binding)) throw new InvalidOperationException($"Actor '{authoring.name}' cannot resolve hitbox binding '{clip.HitboxId}'.");
            ActorHitQueryGeometry geometry = ActorHitQueryGeometryResolver.Resolve(authoring, binding);
            int count = Query(geometry, signalDefinition.TargetLayerMask);
            try
            {
                for (int index = 0; index < count; index++)
                {
                    Collider candidate = overlapBuffer[index];
                    if (candidate == null) continue;
                    PropertyComponent targetProperty = candidate.GetComponentInParent<PropertyComponent>();
                    if (!IsValidTarget(targetProperty, signalDefinition.TargetFactions, signalDefinition.RequiredTargetTag)) continue;
                    int targetId = targetProperty.Entity.EntityId;
                    if (!hitTargets.Add(targetId)) continue;
                    float requestedDamage = ResolveRequestedDamage(signalDefinition);
                    EffectSignal signal = new EffectSignal(EffectSignalType.HitConfirmed, sourceEntity, targetProperty.Entity, sourceEntity, requestedDamage, requestedDamage, signalDefinition.Tags, signalDefinition.SignalId, position: candidate.ClosestPoint(geometry.Center));
                    pendingSignals.Add(new PendingEffectSignal(sourceEffect.Runtime, signal));
                }
            }
            finally
            {
                Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
            }
            if (count == overlapBuffer.Length) Debug.LogWarning($"Actor '{authoring.name}' filled its hit-query buffer for window '{clip.ClipId}' in behavior instance '{handle.InstanceId}'; increase the configured capacity if targets are missing.");
        }

        /// <summary>在全体 Actor 都完成查询后发布当前运行时收集的不可变信号，并保证任意发布异常都不会把旧信号泄漏到下一 Tick。</summary>
        public void CommitSignals()
        {
            ThrowIfDisposed();
            List<Exception> exceptions = null;
            try
            {
                for (int index = 0; index < pendingSignals.Count; index++)
                {
                    PendingEffectSignal pending = pendingSignals[index];
                    try
                    {
                        pending.Runtime.Publish(pending.Signal);
                    }
                    catch (Exception exception)
                    {
                        if (exceptions == null) exceptions = new List<Exception>();
                        exceptions.Add(new InvalidOperationException($"Actor '{authoring.name}' failed to publish deferred EffectSignal '{pending.Signal.AbilityId}'.", exception));
                    }
                }
            }
            finally
            {
                pendingSignals.Clear();
            }
            if (exceptions != null) throw new AggregateException($"Actor '{authoring.name}' failed to commit one or more deferred hit signals.", exceptions);
        }

        /// <summary>关闭一个具体行为实例中的 HitWindow 并释放其命中去重状态；重复关闭安全返回 false。</summary>
        public bool Close(BehaviorHandle handle, HitWindowClip clip)
        {
            if (disposed || !handle.IsValid || clip == null) return false;
            return hitTargetsByWindow.Remove(new HitWindowKey(handle.InstanceId, clip.ClipId));
        }

        /// <summary>关闭全部窗口并永久停止当前查询运行时；重复释放保持幂等。</summary>
        public void Dispose()
        {
            if (disposed) return;
            hitTargetsByWindow.Clear();
            pendingSignals.Clear();
            Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
            disposed = true;
        }

        /// <summary>根据几何种类调用对应的 NonAlloc 物理查询，确保 Sphere 与 Capsule 不退化为 Box 近似。</summary>
        private int Query(ActorHitQueryGeometry geometry, LayerMask targetLayerMask)
        {
            switch (geometry.Kind)
            {
                case ActorHitQueryShapeKind.Box: return Physics.OverlapBoxNonAlloc(geometry.Center, geometry.HalfExtents, overlapBuffer, geometry.Rotation, targetLayerMask, QueryTriggerInteraction.UseGlobal);
                case ActorHitQueryShapeKind.Sphere: return Physics.OverlapSphereNonAlloc(geometry.Center, geometry.Radius, overlapBuffer, targetLayerMask, QueryTriggerInteraction.UseGlobal);
                case ActorHitQueryShapeKind.Capsule: return Physics.OverlapCapsuleNonAlloc(geometry.CapsulePointA, geometry.CapsulePointB, geometry.Radius, overlapBuffer, targetLayerMask, QueryTriggerInteraction.UseGlobal);
                default: throw new ArgumentOutOfRangeException(nameof(geometry), geometry.Kind, "Unsupported Actor Hitbox query geometry.");
            }
        }

        /// <summary>验证行为句柄和 Clip，并构造按行为实例与稳定 Clip 编号隔离的窗口键。</summary>
        private static HitWindowKey CreateWindowKey(BehaviorHandle handle, HitWindowClip clip)
        {
            if (!handle.IsValid) throw new ArgumentException("A hit window requires a valid behavior handle.", nameof(handle));
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            return new HitWindowKey(handle.InstanceId, clip.ClipId);
        }

        /// <summary>判断目标是否属于其他仍然存活的 Gameplay Entity，并匹配行为资产声明的阵营与可选 Unity Tag。</summary>
        private bool IsValidTarget(PropertyComponent targetProperty, ActorFactionMask targetFactions, string requiredTag)
        {
            if (targetProperty == null || targetProperty == sourceProperty || targetProperty.Entity == null || targetProperty.Entity == sourceEntity || targetProperty.IsDead || !targetProperty.Entity.IsActive || !targetProperty.gameObject.activeInHierarchy) return false;
            ActorAuthoringComponent targetAuthoring = targetProperty.GetComponentInParent<ActorAuthoringComponent>();
            if (targetAuthoring == null || !targetRelationResolver.CanTarget(sourceEntity, authoring, targetProperty.Entity, targetAuthoring, targetFactions)) return false;
            if (targetProperty.Entity.TryGetLogic(out ActorRuntimeLogic targetRuntime) && !targetRuntime.CanReceiveHit) return false;
            return string.IsNullOrWhiteSpace(requiredTag) || targetProperty.CompareTag(requiredTag);
        }

        /// <summary>根据行为资产声明选择计算攻击、原始攻击或常量请求伤害。</summary>
        private float ResolveRequestedDamage(ActorHitSignalDefinition signalDefinition)
        {
            switch (signalDefinition.DamageSource)
            {
                case ActorDamageSource.CalculatedAttack: return sourceProperty.GetCalculatedDamage();
                case ActorDamageSource.RawAttack: return sourceProperty.Atk;
                case ActorDamageSource.Constant: return signalDefinition.ConstantDamage;
                default: throw new ArgumentOutOfRangeException(nameof(signalDefinition), signalDefinition.DamageSource, "Unsupported actor damage source.");
            }
        }

        /// <summary>阻止已经释放的命中查询继续操作物理世界或 EffectRuntime。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(ActorHitQueryRuntime));
        }

        /// <summary>以行为实例编号和区分大小写的 Clip 稳定编号标识一个独立命中窗口。</summary>
        private readonly struct HitWindowKey : IEquatable<HitWindowKey>
        {
            internal HitWindowKey(long behaviorInstanceId, string clipId)
            {
                BehaviorInstanceId = behaviorInstanceId;
                ClipId = clipId;
            }

            /// <summary>获取 BehaviorController 分配的单调递增实例编号。</summary>
            private long BehaviorInstanceId { get; }

            /// <summary>获取行为程序内区分大小写的 Clip 稳定编号。</summary>
            private string ClipId { get; }

            /// <summary>判断两个窗口键是否属于同一行为实例中的同名 Clip。</summary>
            public bool Equals(HitWindowKey other)
            {
                return BehaviorInstanceId == other.BehaviorInstanceId && string.Equals(ClipId, other.ClipId, StringComparison.Ordinal);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return obj is HitWindowKey other && Equals(other);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (BehaviorInstanceId.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(ClipId);
                }
            }
        }

        /// <summary>把查询阶段创建的 EffectSignal 与当时仍有效的单局 EffectRuntime 绑定，避免提交顺序受攻击者组件回收影响。</summary>
        private readonly struct PendingEffectSignal
        {
            internal PendingEffectSignal(EffectRuntime runtime, EffectSignal signal)
            {
                Runtime = runtime != null ? runtime : throw new ArgumentNullException(nameof(runtime));
                Signal = signal ?? throw new ArgumentNullException(nameof(signal));
            }

            /// <summary>获取信号被采样时所属的单局 EffectRuntime。</summary>
            internal EffectRuntime Runtime { get; }

            /// <summary>获取尚未进入因果链、只保存命中快照数据的信号。</summary>
            internal EffectSignal Signal { get; }
        }
    }
}
