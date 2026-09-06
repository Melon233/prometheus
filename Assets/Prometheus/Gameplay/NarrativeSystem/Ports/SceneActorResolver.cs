using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>场景中一个可参演对象的登记项。</summary>
    [Serializable]
    public sealed class SceneActorEntry
    {
        [SerializeField] [Tooltip("与 ActorRef.Id 对应的稳定标识。")] private string actorId;
        [SerializeField] [Tooltip("场景中的对象。")] private GameObject target;

        /// <summary>获取角色标识。</summary>
        public string ActorId => actorId;

        /// <summary>获取场景对象。</summary>
        public GameObject Target => target;
    }

    /// <summary>场景中一个摆位锚点的登记项。</summary>
    [Serializable]
    public sealed class SceneAnchorEntry
    {
        [SerializeField] [Tooltip("与 Anchor.Named 对应的稳定标识。")] private string anchorId;
        [SerializeField] [Tooltip("锚点变换。")] private Transform target;

        /// <summary>获取锚点标识。</summary>
        public string AnchorId => anchorId;

        /// <summary>获取锚点变换。</summary>
        public Transform Target => target;
    }

    /// <summary>
    /// 基于场景登记表的角色解析器。
    /// <para>
    /// 适用于演出发生在固定场景中的情形：策划在 Inspector 里把角色标识与场景对象对应起来即可。
    /// 需要在 NPC 不在场时动态生成替身的正式流程，应在此基础上补一个查询 EntitySystem 与
    /// NpcDefinition 的解析器；<see cref="ActorHandle.IsStunt"/> 已为替身预留了销毁语义。
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneActorResolver : MonoBehaviour, IActorResolver
    {
        [SerializeField] [Tooltip("场景中可参演的对象。")] private List<SceneActorEntry> actors = new List<SceneActorEntry>();
        [SerializeField] [Tooltip("场景中的摆位锚点。")] private List<SceneAnchorEntry> anchors = new List<SceneAnchorEntry>();

        /// <summary>保存本场演出已解析的角色。</summary>
        private readonly Dictionary<ActorRef, ActorHandle> resolved = new Dictionary<ActorRef, ActorHandle>();

        /// <inheritdoc />
        public UniTask<ActorHandle> ResolveAsync(ActorRef actor, CancellationToken cancellationToken)
        {
            if (resolved.TryGetValue(actor, out ActorHandle cached) && cached.IsAlive) return UniTask.FromResult(cached);
            GameObject target = FindTarget(actor.Id);
            if (target == null) throw new InvalidOperationException($"SceneActorResolver on '{name}' has no entry for actor id '{actor.Id}'.");
            ActorHandle handle = new ActorHandle(actor, target);
            resolved[actor] = handle;
            return UniTask.FromResult(handle);
        }

        /// <inheritdoc />
        public bool TryGetResolved(ActorRef actor, out ActorHandle handle)
        {
            return resolved.TryGetValue(actor, out handle) && handle.IsAlive;
        }

        /// <inheritdoc />
        public void Release(ActorHandle handle)
        {
            if (handle == null) return;
            resolved.Remove(handle.Actor);
            // 只有本次演出临时生成的替身才销毁；场景中既有的对象只解除登记。
            if (handle.IsStunt && handle.IsAlive) StageScope.DestroyObject(handle.GameObject);
        }

        /// <inheritdoc />
        public void ReleaseAll()
        {
            ActorHandle[] snapshot = new ActorHandle[resolved.Count];
            resolved.Values.CopyTo(snapshot, 0);
            for (int index = 0; index < snapshot.Length; index++) Release(snapshot[index]);
            resolved.Clear();
        }

        /// <inheritdoc />
        public bool TryGetAnchor(string anchorId, out Transform anchor)
        {
            for (int index = 0; index < anchors.Count; index++)
            {
                SceneAnchorEntry entry = anchors[index];
                if (entry != null && string.Equals(entry.AnchorId, anchorId, StringComparison.Ordinal) && entry.Target != null)
                {
                    anchor = entry.Target;
                    return true;
                }
            }
            anchor = null;
            return false;
        }

        /// <summary>按标识查找登记的场景对象。</summary>
        private GameObject FindTarget(string actorId)
        {
            if (string.IsNullOrEmpty(actorId)) return null;
            for (int index = 0; index < actors.Count; index++)
            {
                SceneActorEntry entry = actors[index];
                if (entry != null && string.Equals(entry.ActorId, actorId, StringComparison.Ordinal)) return entry.Target;
            }
            return null;
        }
    }
}
