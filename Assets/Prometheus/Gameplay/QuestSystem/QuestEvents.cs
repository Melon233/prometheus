namespace Xuan.Prometheus.Quest
{
    /// <summary>任务进入新节点的事实（包括接取时进入第一个节点）。HUD 据此刷新任务追踪条。</summary>
    public sealed class QuestNodeChangedEvent : IEvent
    {
        /// <summary>创建节点变化事件。</summary>
        /// <param name="quest">任务配置。</param>
        /// <param name="nodeIndex">新节点下标。</param>
        public QuestNodeChangedEvent(QuestDefinition quest, int nodeIndex)
        {
            Quest = quest;
            NodeIndex = nodeIndex;
        }

        /// <summary>任务配置。</summary>
        public QuestDefinition Quest { get; }

        /// <summary>新节点下标。</summary>
        public int NodeIndex { get; }
    }

    /// <summary>任务全部节点完成的事实。</summary>
    public sealed class QuestCompletedEvent : IEvent
    {
        /// <summary>创建任务完成事件。</summary>
        /// <param name="quest">完成的任务。</param>
        public QuestCompletedEvent(QuestDefinition quest)
        {
            Quest = quest;
        }

        /// <summary>完成的任务。</summary>
        public QuestDefinition Quest { get; }
    }
}
