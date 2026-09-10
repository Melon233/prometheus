using System;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一次性动作的执行上下文。
    ///
    /// 动作只能通过本上下文改变世界，因此「动作能做什么」在类型层面是封闭的，
    /// 不会有某个动作绕过任务系统直接去改别的系统状态。
    /// </summary>
    public sealed class QuestActionContext
    {
        /// <summary>创建一次动作执行上下文。</summary>
        /// <param name="questId">触发本次动作的任务标识。</param>
        /// <param name="variables">任务变量存储。</param>
        /// <param name="grantReward">发放奖励的回调；由任务系统转成 RewardGranted 事件。</param>
        /// <param name="startQuest">启动另一个任务的回调。</param>
        /// <param name="finishQuest">直接完成另一个任务的回调。</param>
        internal QuestActionContext(string questId, QuestVariables variables, Action<QuestRewardGranted> grantReward, Action<string> startQuest, Action<string> finishQuest)
        {
            QuestId = questId;
            Variables = variables;
            GrantReward = grantReward;
            StartQuest = startQuest;
            FinishQuest = finishQuest;
        }

        /// <summary>获取触发本次动作的任务标识。</summary>
        public string QuestId { get; }

        /// <summary>获取任务变量存储。</summary>
        public QuestVariables Variables { get; }

        /// <summary>发放一项奖励；任务系统只发通知，实际写入由适配器完成。</summary>
        public Action<QuestRewardGranted> GrantReward { get; }

        /// <summary>启动另一个任务。</summary>
        public Action<string> StartQuest { get; }

        /// <summary>直接完成另一个任务。</summary>
        public Action<string> FinishQuest { get; }
    }

    /// <summary>
    /// 一次性动作。
    ///
    /// 与「声明式绑定」的判据很清晰：**重算一遍还是同一个结果的是绑定，改变了任务系统之外的持久状态的是动作**。
    /// 动作有执行记录且记录进存档，跨读档恰好执行一次（铁律 Q3）。
    ///
    /// 加一种动作 = 加一个子类，任务内核零改动——这是用 <c>[SerializeReference]</c> 多态序列化换来的。
    /// </summary>
    [Serializable]
    public abstract class QuestAction
    {
        [SerializeField] [Tooltip("在所属任务内唯一的动作标识；执行记录以它为键。")]
        private string actionId;

        [SerializeField] [Tooltip("任务回滚到更早步骤时是否清除执行记录，使该动作可以再次执行。发奖励不清，设变量清。")]
        private bool rerunOnRollback;

        /// <summary>获取动作稳定标识。</summary>
        public string ActionId => actionId;

        /// <summary>获取任务回滚时是否允许重新执行。</summary>
        public bool RerunOnRollback => rerunOnRollback;

        /// <summary>执行动作；实现必须只通过上下文改变世界。</summary>
        /// <param name="context">动作执行上下文。</param>
        public abstract void Execute(QuestActionContext context);

        /// <summary>写入动作标识与回滚策略；供编辑器工具与测试使用。</summary>
        /// <param name="id">动作稳定标识。</param>
        /// <param name="rerun">任务回滚时是否允许重新执行。</param>
        public QuestAction WithId(string id, bool rerun = false)
        {
            actionId = id;
            rerunOnRollback = rerun;
            return this;
        }
    }

    /// <summary>发放一项奖励；任务系统不写背包，只发通知。</summary>
    [Serializable]
    public sealed class GrantRewardAction : QuestAction
    {
        [SerializeField] [Tooltip("奖励类型。")] private QuestRewardType type;
        [SerializeField] [Tooltip("物品、货币或经验的业务标识。")] private string rewardId;
        [SerializeField] [Tooltip("奖励数量。")] private int amount = 1;

        /// <summary>创建一个空奖励动作，供 Unity 序列化使用。</summary>
        public GrantRewardAction()
        {
        }

        /// <summary>创建一个奖励动作。</summary>
        public GrantRewardAction(QuestRewardType type, string rewardId, int amount)
        {
            this.type = type;
            this.rewardId = rewardId;
            this.amount = amount;
        }

        /// <summary>获取奖励类型。</summary>
        public QuestRewardType Type => type;

        /// <summary>获取奖励业务标识。</summary>
        public string RewardId => rewardId;

        /// <summary>获取奖励数量。</summary>
        public int Amount => amount;

        /// <inheritdoc />
        public override void Execute(QuestActionContext context)
        {
            context.GrantReward(new QuestRewardGranted(context.QuestId, type, rewardId, amount));
        }
    }

    /// <summary>写入一个任务变量。</summary>
    [Serializable]
    public sealed class SetQuestVariableAction : QuestAction
    {
        [SerializeField] [Tooltip("要写入的任务变量完整路径。")] private string path;
        [SerializeField] [Tooltip("要写入的数值。")] private double value;

        /// <summary>创建一个空变量写入动作，供 Unity 序列化使用。</summary>
        public SetQuestVariableAction()
        {
        }

        /// <summary>创建一个变量写入动作。</summary>
        public SetQuestVariableAction(string path, double value)
        {
            this.path = path;
            this.value = value;
        }

        /// <summary>获取要写入的任务变量完整路径。</summary>
        public string Path => path;

        /// <inheritdoc />
        public override void Execute(QuestActionContext context)
        {
            context.Variables.Set(path, new StoryValue(value));
        }
    }

    /// <summary>启动另一个任务；这是「并行支线」的逃生口，也是章节内多任务编排的手段。</summary>
    [Serializable]
    public sealed class StartQuestAction : QuestAction
    {
        [SerializeField] [Tooltip("要启动的任务标识。")] private string targetQuestId;

        /// <summary>创建一个空启动动作，供 Unity 序列化使用。</summary>
        public StartQuestAction()
        {
        }

        /// <summary>创建一个启动动作。</summary>
        public StartQuestAction(string targetQuestId)
        {
            this.targetQuestId = targetQuestId;
        }

        /// <summary>获取要启动的任务标识。</summary>
        public string TargetQuestId => targetQuestId;

        /// <inheritdoc />
        public override void Execute(QuestActionContext context)
        {
            context.StartQuest(targetQuestId);
        }
    }

    /// <summary>直接完成另一个任务。</summary>
    [Serializable]
    public sealed class FinishQuestAction : QuestAction
    {
        [SerializeField] [Tooltip("要完成的任务标识。")] private string targetQuestId;

        /// <summary>创建一个空完成动作，供 Unity 序列化使用。</summary>
        public FinishQuestAction()
        {
        }

        /// <summary>创建一个完成动作。</summary>
        public FinishQuestAction(string targetQuestId)
        {
            this.targetQuestId = targetQuestId;
        }

        /// <summary>获取要完成的任务标识。</summary>
        public string TargetQuestId => targetQuestId;

        /// <inheritdoc />
        public override void Execute(QuestActionContext context)
        {
            context.FinishQuest(targetQuestId);
        }
    }
}
