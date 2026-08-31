using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>可由策划创建的任务静态配置；所有运行时进度保存在 QuestRuntimeState。</summary>
    [CreateAssetMenu(fileName = "QuestDefinition", menuName = "Prometheus/Quest/Quest Definition")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [SerializeField] private string questId;
        [SerializeField] private string displayName;
        [SerializeField] private List<string> prerequisiteQuestIds = new List<string>();
        [SerializeField] private List<QuestObjectiveDefinition> objectives = new List<QuestObjectiveDefinition>();
        [SerializeField] private List<QuestRewardDefinition> rewards = new List<QuestRewardDefinition>();

        /// <summary>获取任务稳定 ID。</summary>
        public string QuestId => questId;

        /// <summary>获取任务显示名称。</summary>
        public string DisplayName => displayName;

        /// <summary>获取前置任务 ID 列表。</summary>
        public IReadOnlyList<string> PrerequisiteQuestIds => prerequisiteQuestIds;

        /// <summary>获取目标配置列表。</summary>
        public IReadOnlyList<QuestObjectiveDefinition> Objectives => objectives;

        /// <summary>获取任务完成后发放的奖励配置。</summary>
        public IReadOnlyList<QuestRewardDefinition> Rewards => rewards;

        /// <summary>校验任务 ID、目标 ID 和数量，避免运行时生成不可推进的任务。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(questId)) throw new InvalidOperationException($"QuestDefinition '{name}' requires a non-empty QuestId.");
            if (objectives.Count == 0) throw new InvalidOperationException($"QuestDefinition '{questId}' requires at least one objective.");
            HashSet<string> ids = new HashSet<string>();
            foreach (QuestObjectiveDefinition objective in objectives)
            {
                objective.Validate();
                if (!ids.Add(objective.ObjectiveId)) throw new InvalidOperationException($"QuestDefinition '{questId}' contains duplicate objective '{objective.ObjectiveId}'.");
            }
            foreach (QuestRewardDefinition reward in rewards) reward.Validate();
        }
    }

    /// <summary>描述任务完成后产生的一项逻辑奖励，不直接操作背包或货币系统。</summary>
    [Serializable]
    public sealed class QuestRewardDefinition
    {
        [SerializeField] private QuestRewardType type;
        [SerializeField] private string rewardId;
        [SerializeField] private int amount = 1;

        /// <summary>获取奖励类型。</summary>
        public QuestRewardType Type => type;
        /// <summary>获取奖励业务 ID。</summary>
        public string RewardId => rewardId;
        /// <summary>获取奖励数量。</summary>
        public int Amount => amount;

        /// <summary>校验奖励 ID 和数量。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(rewardId)) throw new InvalidOperationException("Quest reward requires a non-empty RewardId.");
            if (amount <= 0) throw new InvalidOperationException($"Quest reward '{rewardId}' requires a positive amount.");
        }
    }

    /// <summary>描述一个由领域事件推进的任务目标。</summary>
    [Serializable]
    public sealed class QuestObjectiveDefinition
    {
        [SerializeField] private string objectiveId;
        [SerializeField] private QuestEventType eventType;
        [SerializeField] private string targetId;
        [SerializeField] private int requiredAmount = 1;

        /// <summary>获取目标稳定 ID。</summary>
        public string ObjectiveId => objectiveId;
        /// <summary>获取目标匹配的事件类型。</summary>
        public QuestEventType EventType => eventType;
        /// <summary>获取目标匹配的业务对象 ID。</summary>
        public string TargetId => targetId;
        /// <summary>获取目标完成所需数量。</summary>
        public int RequiredAmount => requiredAmount;

        /// <summary>校验目标的唯一标识和完成数量。</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(objectiveId)) throw new InvalidOperationException("Quest objective requires a non-empty ObjectiveId.");
            if (requiredAmount <= 0) throw new InvalidOperationException($"Quest objective '{objectiveId}' requires a positive amount.");
        }
    }

    /// <summary>任务实例的可存档运行时状态。</summary>
    [Serializable]
    public sealed class QuestRuntimeState
    {
        [SerializeField] private string questId;
        [SerializeField] private QuestState state = QuestState.Unavailable;
        [SerializeField] private List<QuestObjectiveProgress> objectiveProgress = new List<QuestObjectiveProgress>();
        [SerializeField] private List<string> processedEventIds = new List<string>();

        public QuestRuntimeState(string questId) { this.questId = questId; }
        /// <summary>获取任务稳定 ID。</summary>
        public string QuestId => questId;
        /// <summary>获取当前任务状态。</summary>
        public QuestState State => state;
        /// <summary>获取所有目标进度。</summary>
        public IReadOnlyList<QuestObjectiveProgress> ObjectiveProgress => objectiveProgress;
        /// <summary>获取已处理事件 ID，用于存档和幂等恢复。</summary>
        public IReadOnlyList<string> ProcessedEventIds => processedEventIds;

        internal void SetState(QuestState value) { state = value; }
        internal bool HasProcessed(string eventId) { return !string.IsNullOrEmpty(eventId) && processedEventIds.Contains(eventId); }
        internal void MarkProcessed(string eventId) { if (!string.IsNullOrEmpty(eventId)) processedEventIds.Add(eventId); }
        internal QuestObjectiveProgress GetOrCreateProgress(string objectiveId)
        {
            for (int index = 0; index < objectiveProgress.Count; index++) if (objectiveProgress[index].ObjectiveId == objectiveId) return objectiveProgress[index];
            QuestObjectiveProgress created = new QuestObjectiveProgress(objectiveId);
            objectiveProgress.Add(created);
            return created;
        }
    }

    /// <summary>单个目标的当前数量。</summary>
    [Serializable]
    public sealed class QuestObjectiveProgress
    {
        [SerializeField] private string objectiveId;
        [SerializeField] private int amount;

        public QuestObjectiveProgress(string objectiveId) { this.objectiveId = objectiveId; }
        /// <summary>获取目标稳定 ID。</summary>
        public string ObjectiveId => objectiveId;
        /// <summary>获取当前累计数量。</summary>
        public int Amount => amount;
        internal void SetAmount(int value) { amount = value; }
    }

    /// <summary>任务系统的 JSON 存档容器；只保存运行时状态，不复制静态配置。</summary>
    [Serializable]
    public sealed class QuestSystemSnapshot
    {
        [SerializeField] private List<QuestRuntimeState> states = new List<QuestRuntimeState>();

        /// <summary>获取快照中的任务状态列表。</summary>
        public IReadOnlyList<QuestRuntimeState> States => states;

        /// <summary>创建一个包含指定任务状态的快照。</summary>
        public QuestSystemSnapshot(IReadOnlyCollection<QuestRuntimeState> source)
        {
            foreach (QuestRuntimeState state in source) states.Add(state);
        }

        /// <summary>供 Unity JsonUtility 反序列化使用的无参构造函数。</summary>
        public QuestSystemSnapshot() { }
    }
}
