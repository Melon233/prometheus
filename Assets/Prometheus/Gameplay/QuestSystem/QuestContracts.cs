using System;

namespace Xuan.Prometheus.Quest
{
    /// <summary>任务运行阶段；状态转换只能由 QuestSystem 执行。</summary>
    public enum QuestState
    {
        Unavailable,
        Available,
        Accepted,
        Active,
        Completed,
        Failed,
        Abandoned,
        Expired
    }

    /// <summary>任务目标匹配的领域事件类型。</summary>
    public enum QuestEventType
    {
        NpcInteraction,
        DialogueCompleted,
        FilmCompleted,
        ItemAdded,
        EnemyDefeated,
        EnteredRegion,
        NpcStateChanged
    }

    /// <summary>任务完成后交给奖励适配器处理的奖励类型。</summary>
    public enum QuestRewardType
    {
        Item,
        Currency,
        Experience
    }

    /// <summary>任务奖励发放通知；具体背包或货币写入由外部适配器完成。</summary>
    public readonly struct QuestRewardGranted
    {
        public QuestRewardGranted(string questId, QuestRewardType type, string rewardId, int amount)
        {
            QuestId = questId;
            Type = type;
            RewardId = rewardId;
            Amount = amount;
        }

        /// <summary>获取任务稳定 ID。</summary>
        public string QuestId { get; }
        /// <summary>获取奖励类型。</summary>
        public QuestRewardType Type { get; }
        /// <summary>获取物品、货币或经验的业务 ID。</summary>
        public string RewardId { get; }
        /// <summary>获取奖励数量。</summary>
        public int Amount { get; }
    }

    /// <summary>外部系统注入任务系统的不可变事件；EventId 用于幂等处理。</summary>
    public readonly struct QuestEvent
    {
        public QuestEvent(string eventId, QuestEventType type, string targetId, int amount = 1, string value = null)
        {
            EventId = eventId;
            Type = type;
            TargetId = targetId;
            Amount = amount;
            Value = value;
        }

        /// <summary>获取跨重连仍稳定的事件标识。</summary>
        public string EventId { get; }

        /// <summary>获取事件类型。</summary>
        public QuestEventType Type { get; }

        /// <summary>获取事件作用目标，例如 NPC、物品或区域 ID。</summary>
        public string TargetId { get; }

        /// <summary>获取本次事件贡献的数量。</summary>
        public int Amount { get; }

        /// <summary>获取可选字符串参数，例如对话选项。</summary>
        public string Value { get; }
    }

    /// <summary>向 UI、存档和调试工具暴露任务状态变化。</summary>
    public readonly struct QuestStateChanged
    {
        public QuestStateChanged(string questId, QuestState previous, QuestState current)
        {
            QuestId = questId;
            Previous = previous;
            Current = current;
        }

        /// <summary>获取任务稳定 ID。</summary>
        public string QuestId { get; }
        /// <summary>获取变化前状态。</summary>
        public QuestState Previous { get; }
        /// <summary>获取变化后状态。</summary>
        public QuestState Current { get; }
    }

    /// <summary>向任务 UI 暴露单个目标的进度变化。</summary>
    public readonly struct QuestObjectiveProgressChanged
    {
        public QuestObjectiveProgressChanged(string questId, string objectiveId, int previous, int current, int required)
        {
            QuestId = questId;
            ObjectiveId = objectiveId;
            Previous = previous;
            Current = current;
            Required = required;
        }

        /// <summary>获取任务稳定 ID。</summary>
        public string QuestId { get; }
        /// <summary>获取目标稳定 ID。</summary>
        public string ObjectiveId { get; }
        /// <summary>获取变化前进度。</summary>
        public int Previous { get; }
        /// <summary>获取变化后进度。</summary>
        public int Current { get; }
        /// <summary>获取目标完成所需数量。</summary>
        public int Required { get; }
    }
}
