using System;

namespace Xuan.Prometheus.Quest
{
    /// <summary>定义任务配置注册、状态流转、事件推进和快照持久化入口。</summary>
    public interface IQuestSystem : ISystemContract
    {
        /// <summary>任务状态发生变化时触发。</summary>
        event Action<QuestStateChanged> StateChanged;

        /// <summary>任务目标进度发生变化时触发。</summary>
        event Action<QuestObjectiveProgressChanged> ObjectiveProgressChanged;

        /// <summary>任务完成并产生奖励声明时触发。</summary>
        event Action<QuestRewardGranted> RewardGranted;

        /// <summary>批量注册任务目录。</summary>
        void RegisterCatalog(QuestCatalog catalog);

        /// <summary>注册一个任务定义。</summary>
        void RegisterDefinition(QuestDefinition definition);

        /// <summary>尝试接取任务。</summary>
        bool TryAccept(string questId);

        /// <summary>放弃进行中的任务。</summary>
        bool Abandon(string questId);

        /// <summary>将进行中的任务标记为失败。</summary>
        bool Fail(string questId);

        /// <summary>将进行中的任务标记为过期。</summary>
        bool Expire(string questId);

        /// <summary>向全部活动任务发布一条领域事件。</summary>
        void PublishEvent(QuestEvent questEvent);

        /// <summary>获取指定任务的只读运行时状态对象。</summary>
        QuestRuntimeState GetState(string questId);

        /// <summary>捕获当前任务系统快照。</summary>
        string CaptureSnapshot();

        /// <summary>从快照恢复已注册任务状态。</summary>
        void RestoreSnapshot(string json);
    }
}
