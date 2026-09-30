using System;
using System.Collections.Generic;
using UnityEngine;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 一个任务的静态配置。
    ///
    /// 模型是「有序步骤 ×（条件, 动作）」而不是「无序计数器集合」：任一时刻一个任务处于唯一一个步骤，
    /// 于是存档、追踪、导航、断点恢复全部退化成单点问题。并行目标只影响*显示*与*完成判定*，
    /// 因此降级为步骤内的展示行（<see cref="QuestObjective"/>），不是状态机节点。
    ///
    /// 全部运行时进度保存在 <see cref="QuestRecord"/> 与 <see cref="QuestVariables"/> 中，本资产只读。
    /// </summary>
    [CreateAssetMenu(fileName = "QuestDefinition", menuName = "Prometheus/Quest/Quest Definition")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [SerializeField] [Tooltip("跨存档稳定的任务标识；同时是 quest.<id>.* 变量的中段。")]
        private string questId;

        [SerializeField] [Tooltip("任务标题的文案键。")]
        private string titleTextKey;

        [SerializeField] [Tooltip("任务分类；决定 UI 分组与标记的默认呈现。")]
        private QuestCategory category = QuestCategory.World;

        [SerializeField] [Tooltip("解锁条件表达式；留空表示恒定可接。可引用 quest.*、flag.*、var.* 等命名空间。")]
        private string unlockCondition;

        [SerializeField] [Tooltip("接取方式；Auto 表示解锁条件满足即自动进入进行中。")]
        private QuestAcceptMode acceptMode = QuestAcceptMode.Auto;

        [SerializeField] [Tooltip("放弃任务时是否清空该任务的全部变量与步骤进度。")]
        private bool resetOnAbandon = true;

        [SerializeField] [Tooltip("有序步骤；第一个步骤是接取后的起点。")]
        private List<QuestStep> steps = new List<QuestStep>();

        [SerializeField] [Tooltip("整个任务活动期间都生效的触发器。")]
        private List<QuestTrigger> triggers = new List<QuestTrigger>();

        [SerializeField] [Tooltip("整个任务活动期间都参与解析的对话绑定。")]
        private List<DialogueBinding> dialogueBindings = new List<DialogueBinding>();

        [SerializeReference] [Tooltip("任务完成时执行的一次性动作，例如发奖励、解锁传送点、启动后续任务。")]
        private List<QuestAction> completeActions = new List<QuestAction>();

        /// <summary>获取任务稳定标识。</summary>
        public string QuestId => questId;

        /// <summary>获取任务标题的文案键。</summary>
        public string TitleTextKey => titleTextKey;

        /// <summary>获取任务分类。</summary>
        public QuestCategory Category => category;

        /// <summary>获取解锁条件表达式；留空表示恒真。</summary>
        public string UnlockCondition => unlockCondition;

        /// <summary>获取接取方式。</summary>
        public QuestAcceptMode AcceptMode => acceptMode;

        /// <summary>获取放弃任务时是否重置进度。</summary>
        public bool ResetOnAbandon => resetOnAbandon;

        /// <summary>获取有序步骤列表。</summary>
        public IReadOnlyList<QuestStep> Steps => steps;

        /// <summary>获取任务级触发器。</summary>
        public IReadOnlyList<QuestTrigger> Triggers => triggers;

        /// <summary>获取任务级对话绑定。</summary>
        public IReadOnlyList<DialogueBinding> DialogueBindings => dialogueBindings;

        /// <summary>获取任务完成时执行的一次性动作。</summary>
        public IReadOnlyList<QuestAction> CompleteActions => completeActions;

        /// <summary>获取起始步骤标识；无步骤时为空。</summary>
        public string FirstStepId => steps.Count > 0 ? steps[0].StepId : null;

        /// <summary>按标识查找步骤；不存在时返回空。</summary>
        /// <param name="stepId">步骤稳定标识。</param>
        public QuestStep FindStep(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return null;
            for (int index = 0; index < steps.Count; index++)
            {
                if (string.Equals(steps[index].StepId, stepId, StringComparison.Ordinal)) return steps[index];
            }
            return null;
        }

        /// <summary>供编辑器工具与测试构造配置使用的写入入口。</summary>
        /// <param name="id">任务稳定标识。</param>
        /// <param name="questCategory">任务分类。</param>
        /// <param name="mode">接取方式。</param>
        /// <param name="unlock">解锁条件表达式。</param>
        public void Configure(string id, QuestCategory questCategory = QuestCategory.World, QuestAcceptMode mode = QuestAcceptMode.Auto, string unlock = null)
        {
            questId = id;
            category = questCategory;
            acceptMode = mode;
            unlockCondition = unlock;
        }

        /// <summary>追加一个步骤；供编辑器工具与测试使用。</summary>
        public QuestStep AddStep(QuestStep step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));
            steps.Add(step);
            return step;
        }

        /// <summary>追加一条任务级触发器；供编辑器工具与测试使用。</summary>
        public void AddTrigger(QuestTrigger trigger)
        {
            if (trigger == null) throw new ArgumentNullException(nameof(trigger));
            triggers.Add(trigger);
        }

        /// <summary>追加一条任务级对话绑定；供编辑器工具与测试使用。</summary>
        public void AddDialogueBinding(DialogueBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            dialogueBindings.Add(binding);
        }

        /// <summary>追加一个完成动作；供编辑器工具与测试使用。</summary>
        public void AddCompleteAction(QuestAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            completeActions.Add(action);
        }
    }

    /// <summary>
    /// 任务的唯一活动单元。
    ///
    /// 步骤持有展示用的目标行、推进用的转移列表、把领域事件翻译成变量写入的触发器，
    /// 以及该步骤期间生效的对话绑定。
    /// </summary>
    [Serializable]
    public sealed class QuestStep
    {
        [SerializeField] [Tooltip("在所属任务内唯一的步骤标识。")]
        private string stepId;

        [SerializeField] [Tooltip("步骤描述的文案键；HUD 追踪条显示它。")]
        private string descTextKey;

        [SerializeField] [Tooltip("勾选后该步骤不在任务界面中展示，用于纯逻辑中转步骤。")]
        private bool hidden;

        [SerializeField] [Tooltip("该步骤的导航目标；HUD 追踪条与地图指引点据此定位。")]
        private QuestGuide guide;

        [SerializeField] [Tooltip("展示用目标行；不持有状态，进度全部来自变量。")]
        private List<QuestObjective> objectives = new List<QuestObjective>();

        [SerializeField] [Tooltip("按声明顺序求值，取第一个条件成立者。顺序即优先级。")]
        private List<QuestTransition> transitions = new List<QuestTransition>();

        [SerializeField] [Tooltip("仅该步骤是当前步骤时生效的触发器。")]
        private List<QuestTrigger> triggers = new List<QuestTrigger>();

        [SerializeField] [Tooltip("仅该步骤是当前步骤时参与解析的对话绑定。")]
        private List<DialogueBinding> dialogueBindings = new List<DialogueBinding>();

        [SerializeReference] [Tooltip("进入该步骤时执行的一次性动作。")]
        private List<QuestAction> enterActions = new List<QuestAction>();

        /// <summary>创建一个空步骤，供 Unity 序列化使用。</summary>
        public QuestStep()
        {
        }

        /// <summary>创建一个带标识与描述的步骤。</summary>
        /// <param name="stepId">在所属任务内唯一的步骤标识。</param>
        /// <param name="descTextKey">步骤描述的文案键。</param>
        public QuestStep(string stepId, string descTextKey = null)
        {
            this.stepId = stepId;
            this.descTextKey = descTextKey;
        }

        /// <summary>获取步骤稳定标识。</summary>
        public string StepId => stepId;

        /// <summary>获取步骤描述的文案键。</summary>
        public string DescTextKey => descTextKey;

        /// <summary>获取该步骤是否对玩家隐藏。</summary>
        public bool Hidden => hidden;

        /// <summary>获取该步骤的导航目标。</summary>
        public QuestGuide Guide => guide;

        /// <summary>获取展示用目标行。</summary>
        public IReadOnlyList<QuestObjective> Objectives => objectives;

        /// <summary>获取转移列表。</summary>
        public IReadOnlyList<QuestTransition> Transitions => transitions;

        /// <summary>获取步骤级触发器。</summary>
        public IReadOnlyList<QuestTrigger> Triggers => triggers;

        /// <summary>获取步骤级对话绑定。</summary>
        public IReadOnlyList<DialogueBinding> DialogueBindings => dialogueBindings;

        /// <summary>获取进入该步骤时执行的一次性动作。</summary>
        public IReadOnlyList<QuestAction> EnterActions => enterActions;

        /// <summary>追加一个目标行；供编辑器工具与测试使用。</summary>
        public QuestStep WithObjective(QuestObjective objective)
        {
            objectives.Add(objective ?? throw new ArgumentNullException(nameof(objective)));
            return this;
        }

        /// <summary>追加一条转移；供编辑器工具与测试使用。</summary>
        public QuestStep WithTransition(QuestTransition transition)
        {
            transitions.Add(transition ?? throw new ArgumentNullException(nameof(transition)));
            return this;
        }

        /// <summary>追加一条触发器；供编辑器工具与测试使用。</summary>
        public QuestStep WithTrigger(QuestTrigger trigger)
        {
            triggers.Add(trigger ?? throw new ArgumentNullException(nameof(trigger)));
            return this;
        }

        /// <summary>追加一条对话绑定；供编辑器工具与测试使用。</summary>
        public QuestStep WithDialogueBinding(DialogueBinding binding)
        {
            dialogueBindings.Add(binding ?? throw new ArgumentNullException(nameof(binding)));
            return this;
        }

        /// <summary>设置该步骤的导航目标；供编辑器工具与测试使用。</summary>
        public QuestStep WithGuide(QuestGuide value)
        {
            guide = value;
            return this;
        }

        /// <summary>追加一个进入动作；供编辑器工具与测试使用。</summary>
        public QuestStep WithEnterAction(QuestAction action)
        {
            enterActions.Add(action ?? throw new ArgumentNullException(nameof(action)));
            return this;
        }
    }

    /// <summary>
    /// 展示用目标行。
    ///
    /// 目标行**不持有状态**——进度全部来自变量，因此改配置不会让存档失效，
    /// 它只负责「怎么显示」与「算不算完成」两件纯函数的事。
    /// </summary>
    [Serializable]
    public sealed class QuestObjective
    {
        [SerializeField] [Tooltip("在所属步骤内唯一的目标标识。")]
        private string objectiveId;

        [SerializeField] [Tooltip("目标描述的文案键。")]
        private string descTextKey;

        [SerializeField] [Tooltip("当前进度表达式；留空表示该目标不显示进度。例：quest.q_hunt.kills")]
        private string progressExpr;

        [SerializeField] [Tooltip("目标数量表达式；留空表示该目标不显示进度。例：5")]
        private string targetExpr;

        [SerializeField] [Tooltip("完成判定表达式；留空时默认为 progress >= target。")]
        private string doneExpr;

        [SerializeField] [Tooltip("勾选后该目标不在任务界面中展示，但仍参与完成判定。")]
        private bool hidden;

        /// <summary>创建一个空目标行，供 Unity 序列化使用。</summary>
        public QuestObjective()
        {
        }

        /// <summary>创建一个带进度表达式的目标行。</summary>
        /// <param name="objectiveId">在所属步骤内唯一的目标标识。</param>
        /// <param name="progressExpr">当前进度表达式。</param>
        /// <param name="targetExpr">目标数量表达式。</param>
        /// <param name="descTextKey">目标描述的文案键。</param>
        public QuestObjective(string objectiveId, string progressExpr = null, string targetExpr = null, string descTextKey = null)
        {
            this.objectiveId = objectiveId;
            this.progressExpr = progressExpr;
            this.targetExpr = targetExpr;
            this.descTextKey = descTextKey;
        }

        /// <summary>获取目标稳定标识。</summary>
        public string ObjectiveId => objectiveId;

        /// <summary>获取目标描述的文案键。</summary>
        public string DescTextKey => descTextKey;

        /// <summary>获取当前进度表达式。</summary>
        public string ProgressExpr => progressExpr;

        /// <summary>获取目标数量表达式。</summary>
        public string TargetExpr => targetExpr;

        /// <summary>获取完成判定表达式；留空表示按进度与数量比较。</summary>
        public string DoneExpr => doneExpr;

        /// <summary>获取该目标是否对玩家隐藏。</summary>
        public bool Hidden => hidden;

        /// <summary>设置显式的完成判定表达式；供编辑器工具与测试使用。</summary>
        public QuestObjective WithDoneExpression(string expression)
        {
            doneExpr = expression;
            return this;
        }
    }

    /// <summary>
    /// 一条步骤转移。
    ///
    /// 单一有序转移列表取代了原神那套 accept/finish/fail 三元组：线性推进、分支、失败、
    /// 超时与**任务回滚**（目标指向更早的步骤）全部是同一种机制，少三个概念。
    /// </summary>
    [Serializable]
    public sealed class QuestTransition
    {
        [SerializeField] [Tooltip("转移条件表达式；留空表示恒真。")]
        private string condition;

        [SerializeField] [Tooltip("转移落点类型。")]
        private QuestTransitionKind kind = QuestTransitionKind.ToStep;

        [SerializeField] [Tooltip("落点步骤标识；仅 ToStep 有效，可以指向更早的步骤以实现回滚。")]
        private string targetStepId;

        [SerializeField] [Tooltip("失败原因；仅 FailQuest 有效。")]
        private string failReason;

        [SerializeReference] [Tooltip("发生该转移时执行的一次性动作。")]
        private List<QuestAction> actions = new List<QuestAction>();

        /// <summary>创建一条空转移，供 Unity 序列化使用。</summary>
        public QuestTransition()
        {
        }

        /// <summary>创建一条转移。</summary>
        /// <param name="condition">转移条件表达式；留空表示恒真。</param>
        /// <param name="kind">落点类型。</param>
        /// <param name="targetStepId">落点步骤标识。</param>
        /// <param name="failReason">失败原因。</param>
        public QuestTransition(string condition, QuestTransitionKind kind, string targetStepId = null, string failReason = null)
        {
            this.condition = condition;
            this.kind = kind;
            this.targetStepId = targetStepId;
            this.failReason = failReason;
        }

        /// <summary>获取转移条件表达式。</summary>
        public string Condition => condition;

        /// <summary>获取落点类型。</summary>
        public QuestTransitionKind Kind => kind;

        /// <summary>获取落点步骤标识。</summary>
        public string TargetStepId => targetStepId;

        /// <summary>获取失败原因。</summary>
        public string FailReason => failReason;

        /// <summary>获取本次转移执行的一次性动作。</summary>
        public IReadOnlyList<QuestAction> Actions => actions;

        /// <summary>追加一个转移动作；供编辑器工具与测试使用。</summary>
        public QuestTransition WithAction(QuestAction action)
        {
            actions.Add(action ?? throw new ArgumentNullException(nameof(action)));
            return this;
        }
    }

    /// <summary>
    /// 把一条领域事件翻译成一次任务变量写入。
    ///
    /// 「击败 5 只丘丘人」不是特殊的目标类型，而是「触发器累加变量 + 目标行显示变量 + 转移条件比较变量」。
    /// 由此事件去重表整个消失：计数是变量，变量进存档，重复事件的处理责任回到发布方——本来就该在那里。
    /// </summary>
    [Serializable]
    public sealed class QuestTrigger
    {
        [SerializeField] [Tooltip("精确匹配的事件名；取自 QuestEventNames。")]
        private string eventName;

        [SerializeField] [Tooltip("载荷过滤表达式；留空表示不过滤。载荷以 e. 前缀可见，例：e.id == \"hilichurl\"")]
        private string filter;

        [SerializeField] [Tooltip("要写入的任务变量完整路径，例：quest.q_hunt.kills")]
        private string variablePath;

        [SerializeField] [Tooltip("写入方式。")]
        private QuestTriggerOp op = QuestTriggerOp.Increment;

        [SerializeField] [Tooltip("写入值表达式；留空时 Increment 取 e.count（缺省 1），Set 取 1。")]
        private string valueExpr;

        /// <summary>创建一条空触发器，供 Unity 序列化使用。</summary>
        public QuestTrigger()
        {
        }

        /// <summary>创建一条触发器。</summary>
        /// <param name="eventName">精确匹配的事件名。</param>
        /// <param name="variablePath">要写入的任务变量完整路径。</param>
        /// <param name="op">写入方式。</param>
        /// <param name="filter">载荷过滤表达式。</param>
        /// <param name="valueExpr">写入值表达式。</param>
        public QuestTrigger(string eventName, string variablePath, QuestTriggerOp op = QuestTriggerOp.Increment, string filter = null, string valueExpr = null)
        {
            this.eventName = eventName;
            this.variablePath = variablePath;
            this.op = op;
            this.filter = filter;
            this.valueExpr = valueExpr;
        }

        /// <summary>获取精确匹配的事件名。</summary>
        public string EventName => eventName;

        /// <summary>获取载荷过滤表达式。</summary>
        public string Filter => filter;

        /// <summary>获取要写入的任务变量完整路径。</summary>
        public string VariablePath => variablePath;

        /// <summary>获取写入方式。</summary>
        public QuestTriggerOp Op => op;

        /// <summary>获取写入值表达式。</summary>
        public string ValueExpr => valueExpr;
    }
}
