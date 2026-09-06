using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Spine.Unity;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 一个已解析到场景对象的剧情角色。
    /// 角色可能是既有玩法实体、场景中的具名对象，也可能是仅本次演出存在的替身。
    /// </summary>
    public sealed class ActorHandle
    {
        /// <summary>创建一个角色句柄。</summary>
        /// <param name="actor">角色引用。</param>
        /// <param name="target">解析到的场景对象。</param>
        /// <param name="isStunt">是否是仅本次演出存在的替身；替身会在舞台退出时销毁。</param>
        public ActorHandle(ActorRef actor, GameObject target, bool isStunt = false)
        {
            Actor = actor;
            GameObject = target != null ? target : throw new ArgumentNullException(nameof(target));
            Transform = target.transform;
            IsStunt = isStunt;
            Animator = target.GetComponentInChildren<Animator>();
            Skeleton = target.GetComponentInChildren<SkeletonAnimation>();
        }

        /// <summary>获取角色引用。</summary>
        public ActorRef Actor { get; }

        /// <summary>获取解析到的场景对象。</summary>
        public GameObject GameObject { get; }

        /// <summary>获取解析到的变换。</summary>
        public Transform Transform { get; }

        /// <summary>获取角色身上的原生 Animator；没有时为空。</summary>
        public Animator Animator { get; }

        /// <summary>获取角色身上的 Spine 骨骼动画组件；没有时为空。</summary>
        public SkeletonAnimation Skeleton { get; }

        /// <summary>获取该角色是否是仅本次演出存在的替身。</summary>
        public bool IsStunt { get; }

        /// <summary>获取该角色对象是否仍然有效。</summary>
        public bool IsAlive => GameObject != null;
    }

    /// <summary>
    /// 剧情角色与场景锚点的解析端口。
    /// 解析失败必须在舞台进入阶段抛出，不允许演到一半才发现角色不存在。
    /// </summary>
    public interface IActorResolver
    {
        /// <summary>解析一个角色引用；必要时生成演出替身。</summary>
        UniTask<ActorHandle> ResolveAsync(ActorRef actor, CancellationToken cancellationToken);

        /// <summary>读取一个已经解析过的角色。</summary>
        bool TryGetResolved(ActorRef actor, out ActorHandle handle);

        /// <summary>释放一个角色句柄；替身会被销毁，既有实体只解除登记。</summary>
        void Release(ActorHandle handle);

        /// <summary>释放全部已解析角色。</summary>
        void ReleaseAll();

        /// <summary>按标识查找场景锚点。</summary>
        bool TryGetAnchor(string anchorId, out Transform anchor);
    }

    /// <summary>描述一个演出摆位目标，可以是场景锚点，也可以是固定世界坐标。</summary>
    public readonly struct Anchor
    {
        /// <summary>创建一个引用场景锚点的目标。</summary>
        private Anchor(string anchorId, Vector3 position, bool hasPosition)
        {
            AnchorId = anchorId;
            Position = position;
            HasPosition = hasPosition;
        }

        /// <summary>获取场景锚点标识；使用固定坐标时为空。</summary>
        public string AnchorId { get; }

        /// <summary>获取固定世界坐标。</summary>
        public Vector3 Position { get; }

        /// <summary>获取该目标是否直接使用固定世界坐标。</summary>
        public bool HasPosition { get; }

        /// <summary>引用一个场景锚点。</summary>
        public static Anchor Named(string anchorId)
        {
            if (string.IsNullOrWhiteSpace(anchorId)) throw new ArgumentException("Anchor id cannot be empty.", nameof(anchorId));
            return new Anchor(anchorId, Vector3.zero, false);
        }

        /// <summary>使用一个固定世界坐标。</summary>
        public static Anchor World(Vector3 position)
        {
            return new Anchor(null, position, true);
        }

        /// <summary>在给定解析器下求出世界坐标；锚点缺失时立即报错。</summary>
        public Vector3 Resolve(IActorResolver resolver)
        {
            if (HasPosition) return Position;
            if (resolver != null && resolver.TryGetAnchor(AnchorId, out Transform anchor) && anchor != null) return anchor.position;
            throw new InvalidOperationException($"Narrative anchor '{AnchorId}' was not found in the current scene.");
        }
    }
}
