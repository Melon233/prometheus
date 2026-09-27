using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一个任务偏离默认状态后的运行时记录。
    ///
    /// 只有**偏离默认**的任务才有记录：从未碰过的任务不占内存，其状态由解锁条件现算（铁律 Q5）。
    /// 记录只存在于本次运行：单机版不做存档，每次进入游戏全部任务都从默认状态开始。
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
}
