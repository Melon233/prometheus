using System;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>导航目标的引用方式。</summary>
    public enum QuestGuideKind
    {
        /// <summary>不提供导航。</summary>
        None,

        /// <summary>指向一个具名 NPC 所在的位置。</summary>
        Npc,

        /// <summary>指向一个具名 POI 所在的位置。</summary>
        Poi,

        /// <summary>指向一个固定世界坐标。</summary>
        Position
    }

    /// <summary>
    /// 一个步骤的导航目标。
    ///
    /// 任务系统只保存**语义目标**（哪个 NPC、哪个 POI、哪个坐标），不保存世界坐标的解析结果——
    /// 把 <c>npc:"elder"</c> 换算成一个 <see cref="Vector3"/> 需要查 <c>IPoiSystem</c>，
    /// 而任务系统不允许解析任何其他 System（铁律 Q6）。换算由世界侧的 <c>QuestGuideLocator</c> 完成。
    ///
    /// 这条分工同时让存档保持稳定：NPC 被挪了位置，旧存档里的导航目标依然正确，
    /// 因为存的是「去找长者」而不是「去 (123, 0, 456)」。
    /// </summary>
    [Serializable]
    public struct QuestGuide
    {
        [SerializeField] [Tooltip("导航目标的引用方式。")] private QuestGuideKind kind;
        [SerializeField] [Tooltip("Npc 与 Poi 方式使用的稳定标识。")] private string targetId;
        [SerializeField] [Tooltip("Position 方式使用的世界坐标。")] private Vector3 position;

        /// <summary>创建一个导航目标。</summary>
        /// <param name="kind">引用方式。</param>
        /// <param name="targetId">Npc 或 Poi 的稳定标识。</param>
        /// <param name="position">固定世界坐标。</param>
        public QuestGuide(QuestGuideKind kind, string targetId = null, Vector3 position = default)
        {
            this.kind = kind;
            this.targetId = targetId;
            this.position = position;
        }

        /// <summary>获取导航目标的引用方式。</summary>
        public QuestGuideKind Kind => kind;

        /// <summary>获取 Npc 或 Poi 的稳定标识。</summary>
        public string TargetId => targetId;

        /// <summary>获取固定世界坐标。</summary>
        public Vector3 Position => position;

        /// <summary>获取该导航目标是否有效。</summary>
        public bool IsValid => kind != QuestGuideKind.None && (kind == QuestGuideKind.Position || !string.IsNullOrEmpty(targetId));

        /// <summary>创建一个指向具名 NPC 的导航目标。</summary>
        public static QuestGuide ToNpc(string npcId)
        {
            return new QuestGuide(QuestGuideKind.Npc, npcId);
        }

        /// <summary>创建一个指向具名 POI 的导航目标。</summary>
        public static QuestGuide ToPoi(string poiId)
        {
            return new QuestGuide(QuestGuideKind.Poi, poiId);
        }

        /// <summary>创建一个指向固定世界坐标的导航目标。</summary>
        public static QuestGuide ToPosition(Vector3 worldPosition)
        {
            return new QuestGuide(QuestGuideKind.Position, null, worldPosition);
        }
    }

    /// <summary>
    /// 当前追踪任务的展示摘要。
    ///
    /// HUD 追踪条需要的全部信息一次取齐：任务标题、当前步骤描述、导航目标。
    /// 分成多次查询会让 UI 有机会读到「标题是新任务的、步骤还是旧任务的」这种撕裂状态。
    /// </summary>
    public readonly struct QuestTrackSummary
    {
        /// <summary>创建一份追踪摘要。</summary>
        public QuestTrackSummary(string questId, string titleTextKey, string stepId, string stepDescTextKey, QuestGuide guide)
        {
            QuestId = questId;
            TitleTextKey = titleTextKey;
            StepId = stepId;
            StepDescTextKey = stepDescTextKey;
            Guide = guide;
        }

        /// <summary>获取被追踪任务的稳定标识。</summary>
        public string QuestId { get; }

        /// <summary>获取任务标题的文案键。</summary>
        public string TitleTextKey { get; }

        /// <summary>获取当前步骤的稳定标识。</summary>
        public string StepId { get; }

        /// <summary>获取当前步骤描述的文案键。</summary>
        public string StepDescTextKey { get; }

        /// <summary>获取当前步骤的导航目标。</summary>
        public QuestGuide Guide { get; }
    }
}
