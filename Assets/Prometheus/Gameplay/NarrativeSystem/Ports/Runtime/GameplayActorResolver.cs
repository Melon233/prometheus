using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.World;

namespace Xuan.Prometheus.Narrative
{
    /// <summary>
    /// 走玩法系统的运行时角色解析器。
    ///
    /// 与场景登记表版本（<see cref="SceneActorResolver"/>）的根本差别在于：开放世界里参演对象
    /// 不可能预先摆在场景里逐个登记，只能按类别向持有它们的系统反查——
    /// 队伍成员问 <c>ITeamSystem</c>，场景 NPC 问 <c>IPoiSystem</c>。
    ///
    /// 锚点仍然需要显式登记：它描述的是「这段演出里的某个位置」，没有任何系统天然持有它。
    /// 具名演出机位复用同一张锚点表（见 <see cref="GameplayCameraPort"/>）。
    /// </summary>
    internal sealed class GameplayActorResolver : IActorResolver
    {
        /// <summary>保存本场演出已解析的角色。</summary>
        private readonly Dictionary<ActorRef, ActorHandle> resolved = new Dictionary<ActorRef, ActorHandle>();

        /// <summary>保存本场演出可引用的具名锚点。</summary>
        private readonly Dictionary<string, Transform> anchors = new Dictionary<string, Transform>(StringComparer.Ordinal);

        /// <summary>登记一个具名锚点，供摆位与具名机位引用；同名登记以最后一次为准。</summary>
        /// <param name="anchorId">剧情中引用的锚点标识。</param>
        /// <param name="anchor">锚点变换。</param>
        internal void RegisterAnchor(string anchorId, Transform anchor)
        {
            if (string.IsNullOrWhiteSpace(anchorId)) throw new ArgumentException("Anchor id cannot be empty.", nameof(anchorId));
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            anchors[anchorId] = anchor;
        }

        /// <inheritdoc />
        public UniTask<ActorHandle> ResolveAsync(ActorRef actor, CancellationToken cancellationToken)
        {
            if (resolved.TryGetValue(actor, out ActorHandle cached) && cached.IsAlive) return UniTask.FromResult(cached);
            GameObject target = Resolve(actor);
            if (target == null) throw new InvalidOperationException($"Narrative actor '{actor}' resolved to no live game object.");
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
            // 只有本次演出临时生成的替身才销毁；世界中既有的对象只解除登记。
            if (handle.IsStunt && handle.IsAlive) StageScope.DestroyObject(handle.GameObject);
        }

        /// <inheritdoc />
        public void ReleaseAll()
        {
            ActorHandle[] snapshot = new ActorHandle[resolved.Count];
            resolved.Values.CopyTo(snapshot, 0);
            for (int index = 0; index < snapshot.Length; index++) Release(snapshot[index]);
            resolved.Clear();
            anchors.Clear();
        }

        /// <inheritdoc />
        public bool TryGetAnchor(string anchorId, out Transform anchor)
        {
            return anchors.TryGetValue(anchorId, out anchor) && anchor != null;
        }

        /// <summary>按角色类别向持有它的系统反查场景对象。</summary>
        private GameObject Resolve(ActorRef actor)
        {
            switch (actor.Kind)
            {
                case ActorKind.ActiveMember:
                    return RequireTeam().ActiveMember?.bindGo;
                case ActorKind.TeamSlot:
                    return ResolveTeamSlot(actor.Id);
                case ActorKind.Npc:
                    return ResolveNpc(actor.Id);
                default:
                    // Companion 需要常驻同伴系统，SceneProp 需要场景物件登记，Stunt 需要替身生成，
                    // 三者都还没有对应的持有方；此时静默返回空会让失败点漂移到很远的地方。
                    throw new NotSupportedException($"Narrative actor kind '{actor.Kind}' is not resolvable at runtime yet.");
            }
        }

        /// <summary>按槽位号解析队伍成员。</summary>
        private GameObject ResolveTeamSlot(string slotId)
        {
            if (!int.TryParse(slotId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int slotIndex)) throw new InvalidOperationException($"Narrative actor TeamSlot id '{slotId}' is not an integer slot index.");
            if (!RequireTeam().TryGetMember(slotIndex, out Logic.Entity member)) throw new InvalidOperationException($"Narrative actor TeamSlot '{slotIndex}' has no member.");
            return member.bindGo;
        }

        /// <summary>按 NPC 业务标识在已加载的 POI 中查找承载它的场景对象。</summary>
        private GameObject ResolveNpc(string npcId)
        {
            if (!Core.Gameplay.TryGetSystem(out IPoiSystem poiSystem)) throw new InvalidOperationException($"{nameof(GameplayActorResolver)} requires {nameof(IPoiSystem)} to resolve NPC actors.");
            IReadOnlyList<PoiMono> pois = poiSystem.AllPois;
            for (int index = 0; index < pois.Count; index++)
            {
                PoiMono poi = pois[index];
                if (poi == null || poi.Config == null || poi.Config.Npc == null) continue;
                if (string.Equals(poi.Config.Npc.NpcId, npcId, StringComparison.Ordinal)) return poi.gameObject;
            }
            throw new InvalidOperationException($"Narrative actor Npc '{npcId}' is not among the currently loaded POIs.");
        }

        /// <summary>取用小队系统；缺失属于组合根装配错误。</summary>
        private static ITeamSystem RequireTeam()
        {
            if (!Core.Gameplay.TryGetSystem(out ITeamSystem teamSystem)) throw new InvalidOperationException($"{nameof(GameplayActorResolver)} requires {nameof(ITeamSystem)}.");
            return teamSystem;
        }
    }
}
