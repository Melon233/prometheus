using Xuan.Prometheus.Npc;

namespace Xuan.Prometheus.Quest
{
    /// <summary>将 NPC 交互请求转换为任务领域事件，不把任务规则写入 NpcLogic。</summary>
    internal sealed class QuestNpcAdapter
    {
        private readonly QuestSystem questSystem;

        internal QuestNpcAdapter(QuestSystem questSystem) { this.questSystem = questSystem; }

        /// <summary>发布稳定 NPC ID 和交互入口组成的任务事件。</summary>
        internal void OnInteractionRequested(NpcInteractionContext context)
        {
            string eventId = $"npc-interaction:{context.EntityId}:{context.InteractionId}";
            questSystem.PublishEvent(new QuestEvent(eventId, QuestEventType.NpcInteraction, context.NpcId, 1, context.InteractionId));
        }
    }
}
