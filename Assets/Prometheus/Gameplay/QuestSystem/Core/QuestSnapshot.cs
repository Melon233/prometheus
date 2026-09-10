using System;
using System.Collections.Generic;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一个任务偏离默认状态后的运行时记录。
    ///
    /// 只有**偏离默认**的任务才有记录：从未碰过的任务不占存档，其状态由解锁条件现算（铁律 Q5）。
    /// 因此版本更新新增的任务落在旧存档上自然正确，不需要迁移脚本；
    /// 存档大小与「玩家实际碰过的任务数」成正比，与配置总量无关。
    /// </summary>
    [Serializable]
    public sealed class QuestRecord
    {
        [SerializeField] private string questId;
        [SerializeField] private QuestStatus status;
        [SerializeField] private string stepId;
        [SerializeField] private string failReason;
        [SerializeField] private List<string> executed = new List<string>();

        /// <summary>创建一条空记录，供 Unity 序列化使用。</summary>
        public QuestRecord()
        {
        }

        /// <summary>创建一条任务记录。</summary>
        /// <param name="questId">任务稳定标识。</param>
        public QuestRecord(string questId)
        {
            this.questId = questId;
        }

        /// <summary>获取任务稳定标识。</summary>
        public string QuestId => questId;

        /// <summary>获取当前状态。</summary>
        public QuestStatus Status => status;

        /// <summary>获取当前步骤；非进行中时为空。</summary>
        public string StepId => stepId;

        /// <summary>获取失败原因；非失败时为空。</summary>
        public string FailReason => failReason;

        /// <summary>获取已执行的一次性动作标识集合。</summary>
        public IReadOnlyList<string> Executed => executed;

        /// <summary>写入状态。</summary>
        internal void SetStatus(QuestStatus value)
        {
            status = value;
        }

        /// <summary>写入当前步骤。</summary>
        internal void SetStep(string value)
        {
            stepId = value;
        }

        /// <summary>写入失败原因。</summary>
        internal void SetFailReason(string value)
        {
            failReason = value;
        }

        /// <summary>判断一个动作是否已经执行过。</summary>
        internal bool HasExecuted(string actionId)
        {
            return !string.IsNullOrEmpty(actionId) && executed.Contains(actionId);
        }

        /// <summary>记录一个动作已经执行。</summary>
        internal void MarkExecuted(string actionId)
        {
            if (string.IsNullOrEmpty(actionId) || executed.Contains(actionId)) return;
            executed.Add(actionId);
        }

        /// <summary>清除一个动作的执行记录，使它可以在回滚后重新执行。</summary>
        internal void ClearExecuted(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return;
            executed.Remove(actionId);
        }

        /// <summary>清空全部执行记录与进度；放弃并重置任务时使用。</summary>
        internal void Reset()
        {
            status = QuestStatus.Available;
            stepId = null;
            failReason = null;
            executed.Clear();
        }
    }

    /// <summary>可 JSON 序列化的任务变量条目；与剧情存档使用同一套值解析规则。</summary>
    [Serializable]
    public sealed class QuestVariableEntry
    {
        [SerializeField] private string path;
        [SerializeField] private int kind;
        [SerializeField] private string value;

        /// <summary>创建一条空条目，供 Unity 序列化使用。</summary>
        public QuestVariableEntry()
        {
        }

        /// <summary>创建一条变量条目。</summary>
        public QuestVariableEntry(string path, StoryValue storyValue)
        {
            this.path = path;
            kind = (int)storyValue.Kind;
            value = storyValue.ToString();
        }

        /// <summary>获取变量完整路径。</summary>
        public string Path => path;

        /// <summary>还原为运行时值。</summary>
        public StoryValue ToValue()
        {
            return StoryValue.Parse((StoryValueKind)kind, value);
        }
    }

    /// <summary>任务系统的存档容器；只保存偏离默认的记录与已写入的变量，不复制静态配置。</summary>
    [Serializable]
    public sealed class QuestSnapshot
    {
        [SerializeField] private List<QuestRecord> quests = new List<QuestRecord>();
        [SerializeField] private List<QuestVariableEntry> variables = new List<QuestVariableEntry>();
        [SerializeField] private string trackedQuestId;

        /// <summary>供 Unity JsonUtility 反序列化使用的无参构造函数。</summary>
        public QuestSnapshot()
        {
        }

        /// <summary>捕获一份任务系统快照。</summary>
        /// <param name="records">当前全部任务记录。</param>
        /// <param name="capturedVariables">当前全部任务变量。</param>
        /// <param name="tracked">当前追踪的任务标识。</param>
        public QuestSnapshot(IEnumerable<QuestRecord> records, IReadOnlyDictionary<string, StoryValue> capturedVariables, string tracked)
        {
            foreach (QuestRecord record in records) quests.Add(record);
            foreach (KeyValuePair<string, StoryValue> pair in capturedVariables) variables.Add(new QuestVariableEntry(pair.Key, pair.Value));
            trackedQuestId = tracked;
        }

        /// <summary>获取快照中的任务记录。</summary>
        public IReadOnlyList<QuestRecord> Quests => quests;

        /// <summary>获取快照中的任务变量条目。</summary>
        public IReadOnlyList<QuestVariableEntry> Variables => variables;

        /// <summary>获取快照中记录的追踪任务标识。</summary>
        public string TrackedQuestId => trackedQuestId;

        /// <summary>把变量条目还原成运行时字典。</summary>
        public Dictionary<string, StoryValue> RestoreVariables()
        {
            Dictionary<string, StoryValue> map = new Dictionary<string, StoryValue>(variables.Count, StringComparer.Ordinal);
            for (int index = 0; index < variables.Count; index++) map[variables[index].Path] = variables[index].ToValue();
            return map;
        }
    }
}
