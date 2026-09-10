using UnityEngine;
using Xuan.Prometheus.Quest;

namespace Xuan.Prometheus.World
{
    /// <summary>
    /// 场景中的一块具名区域：玩家进入时向任务系统投喂一条 <c>world.entered_region</c> 事件。
    ///
    /// 用触发器碰撞体而不是逐帧比距离，是为了守住「不轮询」这条线：进入与离开由物理系统在
    /// 真正发生的那一帧通知，任务系统那边同样只在收到事件时才动。
    ///
    /// 区域归 PoiSystem 而不是任务系统：它描述的是「世界里有这么一块地方」，
    /// 与「哪个任务关心它」无关——同一块区域可以同时被多个任务的触发器过滤命中。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class RegionTriggerMono : MonoBehaviour
    {
        [SerializeField] [Tooltip("区域稳定标识；任务触发器用 e.regionId 过滤它。")]
        private string regionId;

        [SerializeField] [Tooltip("勾选后该区域只在本局第一次进入时投喂事件。")]
        private bool once;

        /// <summary>标记本局是否已经投喂过事件。</summary>
        private bool fired;

        /// <summary>获取区域稳定标识。</summary>
        public string RegionId => regionId;

        /// <summary>把碰撞体强制设为触发器，避免摆放时漏勾导致区域把玩家挡在外面。</summary>
        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        /// <summary>玩家进入区域时投喂事件；非玩家对象一律忽略。</summary>
        /// <param name="other">进入区域的碰撞体。</param>
        private void OnTriggerEnter(Collider other)
        {
            if (once && fired) return;
            if (string.IsNullOrWhiteSpace(regionId)) return;
            if (!IsActiveTeamMember(other)) return;
            if (!Core.Gameplay.TryGetSystem(out IQuestSystem questSystem)) return;
            fired = true;
            questSystem.Emit(QuestEvent.Create(QuestEventNames.EnteredRegion, ("regionId", regionId)));
        }

        /// <summary>判断进入者是否为当前上场的队伍成员。</summary>
        private static bool IsActiveTeamMember(Collider other)
        {
            if (other == null) return false;
            if (!Core.Gameplay.TryGetSystem(out ITeamSystem teamSystem)) return false;
            GameObject active = teamSystem.ActiveMember?.bindGo;
            if (active == null) return false;
            // 碰撞体通常挂在角色根节点或其子节点上，因此比较根节点而不是碰撞体自身。
            return other.transform.IsChildOf(active.transform) || active.transform.IsChildOf(other.transform);
        }
    }
}
