using System;
using System.Collections.Generic;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务运行阶段。
    ///
    /// 从旧实现的 8 个收敛到 5 个：<c>Accepted</c> 在旧实现里同一次调用就转 <c>Active</c>，是个不存在的状态；
    /// <c>Expired</c> 是失败的一种原因，用 <see cref="QuestRecord.FailReason"/> 表达；
    /// <c>Abandoned</c> 不是终态，而是「回到 Available」的操作。
    ///
    /// <see cref="Locked"/> 与 <see cref="Available"/> 是**推导值**：存档里没有记录的任务按解锁条件现算，
    /// 因此版本更新新增的任务落在旧存档上自然处于正确状态，不需要迁移脚本（铁律 Q5）。
    /// </summary>
    public enum QuestStatus
    {
        /// <summary>解锁条件未满足；推导值，不入档。</summary>
        Locked,

        /// <summary>可接取；推导值，不入档。</summary>
        Available,

        /// <summary>进行中，带当前步骤。</summary>
        Active,

        /// <summary>已完成。</summary>
        Completed,

        /// <summary>已失败，带失败原因。</summary>
        Failed
    }

    /// <summary>任务的接取方式。</summary>
    public enum QuestAcceptMode
    {
        /// <summary>解锁条件满足即自动进入进行中；绝大多数剧情任务用这种。</summary>
        Auto,

        /// <summary>需要玩家显式接取，例如委托。</summary>
        Manual
    }

    /// <summary>任务分类；与原神一致，决定 UI 分组与 NPC 标记的默认呈现。</summary>
    public enum QuestCategory
    {
        /// <summary>魔神任务（主线）。</summary>
        Archon,

        /// <summary>传说任务（角色）。</summary>
        Story,

        /// <summary>世界任务。</summary>
        World,

        /// <summary>每日委托。</summary>
        Commission,

        /// <summary>活动任务。</summary>
        Event
    }

    /// <summary>
    /// NPC 头顶标记。
    ///
    /// 该枚举归任务系统而不是 NPC 系统：标记值配在 <see cref="DialogueBinding.Marker"/> 上，
    /// 由任务作者显式声明，与「这个 NPC 现在说哪段话」是同一次解析的两个输出（见 QuestLoopIntegration.md §2.3）。
    /// </summary>
    public enum QuestMarker
    {
        /// <summary>无标记。</summary>
        None,

        /// <summary>金色感叹号：可接主线或传说任务。</summary>
        QuestAvailable,

        /// <summary>蓝色感叹号：可接世界任务。</summary>
        WorldQuestAvailable,

        /// <summary>灰色：任务进行中，但当前步骤不指向本 NPC。</summary>
        QuestInProgress,

        /// <summary>问号：可交付或可推进。</summary>
        QuestTurnIn
    }

    /// <summary>任务完成后交给奖励适配器处理的奖励类型。</summary>
    public enum QuestRewardType
    {
        /// <summary>物品。</summary>
        Item,

        /// <summary>货币。</summary>
        Currency,

        /// <summary>经验。</summary>
        Experience
    }

    /// <summary>转移的落点类型。</summary>
    public enum QuestTransitionKind
    {
        /// <summary>跳转到指定步骤；目标可以是更早的步骤，这就是任务回滚。</summary>
        ToStep,

        /// <summary>完成任务。</summary>
        CompleteQuest,

        /// <summary>失败任务。</summary>
        FailQuest
    }

    /// <summary>触发器对目标变量的写入方式。</summary>
    public enum QuestTriggerOp
    {
        /// <summary>直接赋值。</summary>
        Set,

        /// <summary>累加。</summary>
        Increment,

        /// <summary>取较大值。</summary>
        Max
    }

    /// <summary>
    /// 外部系统投喂给任务系统的领域事件。
    ///
    /// 用「名字 + 载荷」而不是封闭枚举：新增一种触发方式只需加一个常量与一个发布方，任务内核零改动。
    /// 已知事件名集中登记在 <see cref="QuestEventNames"/>，编辑期校验器据此检查触发器引用的名字。
    /// </summary>
    public readonly struct QuestEvent
    {
        /// <summary>保存事件载荷；为空时表示无载荷。</summary>
        private readonly Dictionary<string, StoryValue> payload;

        /// <summary>创建一条领域事件。</summary>
        /// <param name="name">事件名，取自 <see cref="QuestEventNames"/>。</param>
        /// <param name="payload">事件载荷；触发器的过滤表达式以 <c>e.</c> 前缀引用其中的键。</param>
        public QuestEvent(string name, Dictionary<string, StoryValue> payload = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Quest event name cannot be empty.", nameof(name));
            Name = name;
            this.payload = payload;
        }

        /// <summary>获取事件名。</summary>
        public string Name { get; }

        /// <summary>按键读取载荷；键不存在时返回空值。</summary>
        /// <param name="key">载荷键名，不含 <c>e.</c> 前缀。</param>
        public StoryValue GetPayload(string key)
        {
            return payload != null && payload.TryGetValue(key, out StoryValue value) ? value : StoryValue.None;
        }

        /// <summary>判断载荷中是否存在指定键。</summary>
        public bool HasPayload(string key)
        {
            return payload != null && payload.ContainsKey(key);
        }

        /// <summary>用键值对快捷构造一条事件。</summary>
        /// <param name="name">事件名。</param>
        /// <param name="entries">交替出现的键与值。</param>
        public static QuestEvent Create(string name, params (string Key, StoryValue Value)[] entries)
        {
            if (entries == null || entries.Length == 0) return new QuestEvent(name);
            Dictionary<string, StoryValue> map = new Dictionary<string, StoryValue>(entries.Length, StringComparer.Ordinal);
            for (int index = 0; index < entries.Length; index++) map[entries[index].Key] = entries[index].Value;
            return new QuestEvent(name, map);
        }
    }

    /// <summary>
    /// 任务状态、当前步骤与目标进度的合并变化通知。
    ///
    /// 之所以合并成一条而不是三条独立事件：三者在同一次帧末 flush 里经常一起变，
    /// 分开发布会让 UI 在同一帧内重建多次。
    /// </summary>
    public readonly struct QuestChanged
    {
        /// <summary>创建一条任务变化通知。</summary>
        public QuestChanged(string questId, QuestStatus previousStatus, QuestStatus currentStatus, string previousStepId, string currentStepId)
        {
            QuestId = questId;
            PreviousStatus = previousStatus;
            CurrentStatus = currentStatus;
            PreviousStepId = previousStepId;
            CurrentStepId = currentStepId;
        }

        /// <summary>获取任务稳定标识。</summary>
        public string QuestId { get; }

        /// <summary>获取变化前状态。</summary>
        public QuestStatus PreviousStatus { get; }

        /// <summary>获取变化后状态。</summary>
        public QuestStatus CurrentStatus { get; }

        /// <summary>获取变化前的当前步骤；无步骤时为空。</summary>
        public string PreviousStepId { get; }

        /// <summary>获取变化后的当前步骤；无步骤时为空。</summary>
        public string CurrentStepId { get; }

        /// <summary>获取本次变化是否改变了状态。</summary>
        public bool StatusChanged => PreviousStatus != CurrentStatus;

        /// <summary>获取本次变化是否改变了当前步骤。</summary>
        public bool StepChanged => !string.Equals(PreviousStepId, CurrentStepId, StringComparison.Ordinal);
    }

    /// <summary>任务奖励发放通知；具体背包或货币写入由外部适配器完成，任务系统不写背包。</summary>
    public readonly struct QuestRewardGranted
    {
        /// <summary>创建一条奖励发放通知。</summary>
        public QuestRewardGranted(string questId, QuestRewardType type, string rewardId, int amount)
        {
            QuestId = questId;
            Type = type;
            RewardId = rewardId;
            Amount = amount;
        }

        /// <summary>获取任务稳定标识。</summary>
        public string QuestId { get; }

        /// <summary>获取奖励类型。</summary>
        public QuestRewardType Type { get; }

        /// <summary>获取物品、货币或经验的业务标识。</summary>
        public string RewardId { get; }

        /// <summary>获取奖励数量。</summary>
        public int Amount { get; }
    }

    /// <summary>
    /// 一个 NPC 当前应当呈现的对话与标记。
    ///
    /// 标记与对话地址来自同一次解析，因此不可能出现「头顶挂问号、点进去是闲聊」这种
    /// 只在特定任务状态组合下复现的不一致。
    /// </summary>
    public readonly struct QuestDialogueResolution
    {
        /// <summary>创建一次对话解析结果。</summary>
        public QuestDialogueResolution(string questId, string bindingId, string storyLocation, QuestMarker marker, string offersQuestId)
        {
            QuestId = questId;
            BindingId = bindingId;
            StoryLocation = storyLocation;
            Marker = marker;
            OffersQuestId = offersQuestId;
        }

        /// <summary>获取命中绑定所属的任务标识。</summary>
        public string QuestId { get; }

        /// <summary>获取绑定的稳定标识，用于回填事件载荷。</summary>
        public string BindingId { get; }

        /// <summary>获取要演绎的剧情图资源地址。</summary>
        public string StoryLocation { get; }

        /// <summary>获取该绑定生效时 NPC 头顶的标记。</summary>
        public QuestMarker Marker { get; }

        /// <summary>获取该对话用于显式接取的任务标识；不提供接取时为空。</summary>
        public string OffersQuestId { get; }
    }
}
