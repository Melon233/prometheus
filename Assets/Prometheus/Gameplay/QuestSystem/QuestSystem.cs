using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 纯逻辑任务系统：配置注册、状态推导、步骤推进、对话解析与存档。
    ///
    /// 三条贯穿全类的规则：
    /// <list type="number">
    /// <item><b>状态可推导</b>：从未碰过的任务没有记录，状态由解锁条件现算（Q5）。</item>
    /// <item><b>不轮询</b>：条件重算只由显式失效通知驱动，且统一在帧末 flush（Q4）。</item>
    /// <item><b>零向外依赖</b>：不解析任何其他 System，外部数据一律经投影读入（Q6）。</item>
    /// </list>
    /// </summary>
    internal sealed class QuestSystem : XSystem, IQuestSystem
    {
        /// <summary>全局任务目录的 YooAsset 地址；由本系统自己在 AfterNewAsync 加载，是私有实现细节。</summary>
        private const string DefaultCatalogAddress = "QuestCatalog";

        /// <summary>单次 flush 允许的最大轮次；超过即判定配置写出了死循环。</summary>
        private const int MaxFlushRounds = 16;

        /// <summary>异步相位读入、等待同步相位注册的任务目录。</summary>
        private QuestCatalog pendingCatalog;

        /// <summary>保存已注册的任务配置。</summary>
        private readonly Dictionary<string, QuestDefinition> definitions = new Dictionary<string, QuestDefinition>(StringComparer.Ordinal);

        /// <summary>保存偏离默认状态的任务记录；从未碰过的任务不在其中。</summary>
        private readonly Dictionary<string, QuestRecord> records = new Dictionary<string, QuestRecord>(StringComparer.Ordinal);

        /// <summary>路径到任务的倒排索引，使一次变量变化只弄脏真正引用它的任务。</summary>
        private readonly Dictionary<string, HashSet<string>> pathIndex = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>本帧待重算的任务集合。</summary>
        private readonly HashSet<string> dirty = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>本帧对话绑定受影响的 NPC 集合。</summary>
        private readonly HashSet<string> invalidatedNpcs = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>复用的候选绑定缓冲，避免每次解析都产生临时列表。</summary>
        private readonly List<DialogueCandidate> candidateBuffer = new List<DialogueCandidate>();

        /// <summary>复用的活动任务缓冲。</summary>
        private readonly List<string> activeBuffer = new List<string>();

        /// <summary>倒排索引只关心标识符路径，函数名收进这个缓冲后丢弃。</summary>
        private readonly List<string> discardedFunctionNames = new List<string>();

        private QuestVariables variables;
        private bool isFlushing;

        /// <inheritdoc />
        public event Action<QuestChanged> QuestChanged;

        /// <inheritdoc />
        public event Action<QuestRewardGranted> RewardGranted;

        /// <inheritdoc />
        public event Action<IReadOnlyCollection<string>> DialogueBindingsInvalidated;

        /// <summary>创建任务系统并建立自己的变量命名空间。</summary>
        /// <param name="sharedVariables">
        /// 会话共享的变量存储；为空表示自建一个只含任务命名空间的独立存储。
        /// 订阅的是**整个存储**的变化通知，因此剧情旗标的改动同样能弄脏引用它的任务——
        /// 组合根不再需要「把剧情的 Changed 转发给任务」那行接线。
        /// </param>
        public QuestSystem(VariableStore sharedVariables = null)
        {
            variables = new QuestVariables(ResolveStatusForVariable, sharedVariables);
            variables.Changed += OnVariableChanged;
        }

        /// <inheritdoc />
        public QuestVariables Variables => variables;

        /// <inheritdoc />
        public string TrackedQuestId { get; set; }

        /// <inheritdoc />
        public void RegisterCatalog(QuestCatalog catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            for (int index = 0; index < catalog.Definitions.Count; index++) RegisterDefinition(catalog.Definitions[index]);
        }

        /// <inheritdoc />
        public void RegisterDefinition(QuestDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            QuestValidator.ValidateOrThrow(definition, variables);
            if (definitions.ContainsKey(definition.QuestId)) throw new InvalidOperationException($"Quest '{definition.QuestId}' is already registered.");
            definitions.Add(definition.QuestId, definition);
            IndexUnlockCondition(definition);
            // 新注册的任务立即参与一次推导：解锁条件可能已经成立。
            dirty.Add(definition.QuestId);
            // 新任务带来的对话绑定同样会改变 NPC 标记，注册本身就是一次失效来源。
            InvalidateAllNpcs();
        }

        /// <inheritdoc />
        public QuestStatus GetStatus(string questId)
        {
            QuestDefinition definition = RequireDefinition(questId);
            if (records.TryGetValue(questId, out QuestRecord record)) return record.Status;
            return IsUnlocked(definition) ? QuestStatus.Available : QuestStatus.Locked;
        }

        /// <inheritdoc />
        public string GetCurrentStepId(string questId)
        {
            RequireDefinition(questId);
            return records.TryGetValue(questId, out QuestRecord record) ? record.StepId : null;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetActiveQuestIds()
        {
            activeBuffer.Clear();
            foreach (KeyValuePair<string, QuestRecord> pair in records)
            {
                if (pair.Value.Status == QuestStatus.Active) activeBuffer.Add(pair.Key);
            }
            return activeBuffer;
        }

        /// <inheritdoc />
        public IReadOnlyList<QuestObjectiveProgress> GetObjectiveProgress(string questId)
        {
            QuestDefinition definition = RequireDefinition(questId);
            if (!records.TryGetValue(questId, out QuestRecord record) || record.Status != QuestStatus.Active) return Array.Empty<QuestObjectiveProgress>();
            QuestStep step = definition.FindStep(record.StepId);
            if (step == null) return Array.Empty<QuestObjectiveProgress>();
            QuestObjectiveProgress[] result = new QuestObjectiveProgress[step.Objectives.Count];
            for (int index = 0; index < step.Objectives.Count; index++)
            {
                QuestObjective objective = step.Objectives[index];
                double current = EvaluateNumber(objective.ProgressExpr);
                double target = EvaluateNumber(objective.TargetExpr);
                result[index] = new QuestObjectiveProgress(objective.ObjectiveId, objective.DescTextKey, current, target, IsObjectiveDone(objective, current, target), objective.Hidden);
            }
            return result;
        }

        /// <inheritdoc />
        public bool TryGetTrackedSummary(out QuestTrackSummary summary)
        {
            summary = default;
            if (string.IsNullOrEmpty(TrackedQuestId)) return false;
            if (!definitions.TryGetValue(TrackedQuestId, out QuestDefinition definition)) return false;
            if (!records.TryGetValue(TrackedQuestId, out QuestRecord record) || record.Status != QuestStatus.Active) return false;
            QuestStep step = definition.FindStep(record.StepId);
            if (step == null) return false;
            summary = new QuestTrackSummary(definition.QuestId, definition.TitleTextKey, step.StepId, step.DescTextKey, step.Guide);
            return true;
        }

        /// <inheritdoc />
        public bool Accept(string questId)
        {
            QuestDefinition definition = RequireDefinition(questId);
            if (GetStatus(questId) != QuestStatus.Available) return false;
            if (!IsUnlocked(definition)) return false;
            Activate(definition);
            return true;
        }

        /// <inheritdoc />
        public bool Abandon(string questId)
        {
            QuestDefinition definition = RequireDefinition(questId);
            if (!records.TryGetValue(questId, out QuestRecord record) || record.Status != QuestStatus.Active) return false;
            QuestStatus previous = record.Status;
            string previousStep = record.StepId;
            // 放弃不是终态，而是「回到 Available」的操作；是否清进度由配置决定。
            if (definition.ResetOnAbandon)
            {
                record.Reset();
                variables.RemoveQuest(questId);
                records.Remove(questId);
            }
            else
            {
                record.SetStatus(QuestStatus.Available);
                record.SetStep(null);
            }
            ReindexQuest(definition, null);
            PublishChanged(questId, previous, QuestStatus.Available, previousStep, null);
            dirty.Add(questId);
            return true;
        }

        /// <inheritdoc />
        public void Emit(QuestEvent questEvent)
        {
            if (string.IsNullOrEmpty(questEvent.Name)) throw new ArgumentException("Quest event name cannot be empty.", nameof(questEvent));
            QuestEventScope scope = new QuestEventScope(questEvent, variables);
            foreach (KeyValuePair<string, QuestRecord> pair in records)
            {
                if (pair.Value.Status != QuestStatus.Active) continue;
                QuestDefinition definition = definitions[pair.Key];
                ApplyTriggers(definition.Triggers, questEvent, scope);
                QuestStep step = definition.FindStep(pair.Value.StepId);
                if (step != null) ApplyTriggers(step.Triggers, questEvent, scope);
            }
        }

        /// <inheritdoc />
        public void Invalidate(string path)
        {
            OnVariableChanged(path);
        }

        /// <inheritdoc />
        public void FlushNow()
        {
            Flush();
        }

        /// <summary>按地址载入任务目录，但**不**在这里注册。</summary>
        /// <remarks>
        /// 注册即校验，而校验要用到 flag.* 等由剧情系统提供的投影命名空间。
        /// 异步相位是并行执行的，此刻别的系统可能还没就位，因此这里只把资产读进来，
        /// 真正的注册留到同步的 AfterNew——那时全部系统与接线都已完成。
        /// </remarks>
        public override async UniTask AfterNewAsync()
        {
            QuestCatalog loadedCatalog = null;
            await Core.Asset.LoadAssetAsync<QuestCatalog>(DefaultCatalogAddress, asset => loadedCatalog = asset, error => throw new InvalidOperationException(error)).ToUniTask();
            pendingCatalog = loadedCatalog;
        }

        /// <summary>在全部系统与接线就位后注册任务目录，此时校验能读到完整的命名空间集合。</summary>
        public override void AfterNew()
        {
            RegisterCatalog(pendingCatalog);
            pendingCatalog = null;
        }

        /// <summary>帧末统一推进；动作永远在这个明确的阶段执行，不会嵌套在某个变量的写入里。</summary>
        /// <param name="dt">当前帧增量时间。</param>
        public override void OnUpdate(float dt)
        {
            Flush();
        }

        /// <inheritdoc />
        public bool ResolveDialogue(string npcId, out QuestDialogueResolution resolution)
        {
            resolution = default;
            if (string.IsNullOrEmpty(npcId)) return false;
            candidateBuffer.Clear();
            foreach (KeyValuePair<string, QuestDefinition> pair in definitions)
            {
                QuestStatus status = GetStatus(pair.Key);
                // 进行中的任务贡献任务级与当前步骤级绑定；可接取的任务只贡献任务级绑定，
                // 那正是「接取入口」所在——此时还没有当前步骤。
                if (status == QuestStatus.Active)
                {
                    CollectCandidates(pair.Value, pair.Value.DialogueBindings, npcId);
                    QuestStep step = pair.Value.FindStep(records[pair.Key].StepId);
                    if (step != null) CollectCandidates(pair.Value, step.DialogueBindings, npcId);
                }
                else if (status == QuestStatus.Available)
                {
                    CollectCandidates(pair.Value, pair.Value.DialogueBindings, npcId);
                }
            }
            if (candidateBuffer.Count == 0) return false;
            DialogueCandidate best = candidateBuffer[0];
            for (int index = 1; index < candidateBuffer.Count; index++)
            {
                if (Compare(candidateBuffer[index], best) > 0) best = candidateBuffer[index];
            }
            resolution = new QuestDialogueResolution(best.QuestId, best.Binding.BindingId, best.Binding.StoryLocation, best.Binding.Marker, best.Binding.OffersQuestId);
            return true;
        }

        /// <inheritdoc />
        public string CaptureSnapshot()
        {
            return JsonUtility.ToJson(new QuestSnapshot(records.Values, variables.Capture(), TrackedQuestId));
        }

        /// <inheritdoc />
        public void RestoreSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Quest snapshot JSON cannot be empty.", nameof(json));
            QuestSnapshot snapshot = JsonUtility.FromJson<QuestSnapshot>(json);
            if (snapshot == null) throw new InvalidOperationException("Quest snapshot JSON is invalid.");
            records.Clear();
            pathIndex.Clear();
            for (int index = 0; index < snapshot.Quests.Count; index++)
            {
                QuestRecord record = snapshot.Quests[index];
                // 配置被删除的任务只记警告并丢弃：一条陈旧记录不该污染整个读档流程。
                if (!definitions.ContainsKey(record.QuestId))
                {
                    Debug.LogWarning($"[Quest] 存档包含未知任务 '{record.QuestId}'，已忽略。");
                    continue;
                }
                records[record.QuestId] = record;
            }
            variables.Restore(snapshot.RestoreVariables());
            TrackedQuestId = snapshot.TrackedQuestId;
            // 重建全部索引与脏集合：读档后每个任务都要重新推导一次。
            foreach (KeyValuePair<string, QuestDefinition> pair in definitions)
            {
                IndexUnlockCondition(pair.Value);
                records.TryGetValue(pair.Key, out QuestRecord record);
                ReindexQuest(pair.Value, record != null && record.Status == QuestStatus.Active ? pair.Value.FindStep(record.StepId) : null);
                dirty.Add(pair.Key);
            }
            InvalidateAllNpcs();
        }

        /// <summary>释放全部配置、运行时状态与外部订阅。</summary>
        public override void Dispose()
        {
            variables.Changed -= OnVariableChanged;
            definitions.Clear();
            records.Clear();
            pathIndex.Clear();
            dirty.Clear();
            invalidatedNpcs.Clear();
            variables.Clear();
            QuestChanged = null;
            RewardGranted = null;
            DialogueBindingsInvalidated = null;
            TrackedQuestId = null;
        }

        /// <summary>
        /// 把脏集合推进到不动点。
        /// 动作可能再次弄脏任务，因此循环执行；超过上限判定为配置死循环并挂起，避免卡死主线程。
        /// </summary>
        private void Flush()
        {
            if (isFlushing || dirty.Count == 0 && invalidatedNpcs.Count == 0) return;
            isFlushing = true;
            try
            {
                int round = 0;
                while (dirty.Count > 0)
                {
                    if (round >= MaxFlushRounds)
                    {
                        Debug.LogError($"[Quest] 推进超过 {MaxFlushRounds} 轮仍未收敛，剩余脏任务：{string.Join(", ", dirty)}。已挂起本次推进，请检查任务配置是否存在恒真自环。");
                        dirty.Clear();
                        break;
                    }
                    string[] batch = new string[dirty.Count];
                    dirty.CopyTo(batch);
                    dirty.Clear();
                    for (int index = 0; index < batch.Length; index++) Advance(batch[index]);
                    round++;
                }
            }
            finally
            {
                isFlushing = false;
            }
            if (invalidatedNpcs.Count == 0) return;
            string[] npcs = new string[invalidatedNpcs.Count];
            invalidatedNpcs.CopyTo(npcs);
            invalidatedNpcs.Clear();
            DialogueBindingsInvalidated?.Invoke(npcs);
        }

        /// <summary>推进单个任务：未接取的看解锁条件，进行中的按顺序取第一条成立的转移。</summary>
        private void Advance(string questId)
        {
            if (!definitions.TryGetValue(questId, out QuestDefinition definition)) return;
            if (!records.TryGetValue(questId, out QuestRecord record))
            {
                // 尚无记录：解锁且自动接取时进入进行中，否则保持推导状态不落记录。
                if (definition.AcceptMode == QuestAcceptMode.Auto && IsUnlocked(definition)) Activate(definition);
                return;
            }
            if (record.Status == QuestStatus.Available)
            {
                if (definition.AcceptMode == QuestAcceptMode.Auto && IsUnlocked(definition)) Activate(definition);
                return;
            }
            if (record.Status != QuestStatus.Active) return;
            QuestStep step = definition.FindStep(record.StepId);
            if (step == null) return;
            for (int index = 0; index < step.Transitions.Count; index++)
            {
                QuestTransition transition = step.Transitions[index];
                if (!EvaluateCondition(transition.Condition)) continue;
                ApplyTransition(definition, record, transition);
                return;
            }
        }

        /// <summary>把一个任务置为进行中并进入起始步骤。</summary>
        private void Activate(QuestDefinition definition)
        {
            if (!records.TryGetValue(definition.QuestId, out QuestRecord record))
            {
                record = new QuestRecord(definition.QuestId);
                records[definition.QuestId] = record;
            }
            QuestStatus previous = record.Status;
            string previousStep = record.StepId;
            record.SetStatus(QuestStatus.Active);
            record.SetFailReason(null);
            EnterStep(definition, record, definition.FirstStepId);
            // 与原神一致：接下一个任务时若当前没有在追的线，就自动追它，玩家不必手动去任务界面点一下。
            if (!IsTrackingActiveQuest()) TrackedQuestId = definition.QuestId;
            PublishChanged(definition.QuestId, previous, QuestStatus.Active, previousStep, record.StepId);
        }

        /// <summary>执行一条转移：先跑一次性动作，再落到新的步骤或终态。</summary>
        private void ApplyTransition(QuestDefinition definition, QuestRecord record, QuestTransition transition)
        {
            QuestStatus previousStatus = record.Status;
            string previousStep = record.StepId;
            RunActions(definition, record, transition.Actions);
            switch (transition.Kind)
            {
                case QuestTransitionKind.ToStep:
                    ClearRerunnableActions(definition, record, transition.TargetStepId, previousStep);
                    EnterStep(definition, record, transition.TargetStepId);
                    break;
                case QuestTransitionKind.CompleteQuest:
                    record.SetStatus(QuestStatus.Completed);
                    record.SetStep(null);
                    RunActions(definition, record, definition.CompleteActions);
                    ReindexQuest(definition, null);
                    break;
                case QuestTransitionKind.FailQuest:
                    record.SetStatus(QuestStatus.Failed);
                    record.SetFailReason(transition.FailReason);
                    record.SetStep(null);
                    ReindexQuest(definition, null);
                    break;
            }
            PublishChanged(definition.QuestId, previousStatus, record.Status, previousStep, record.StepId);
            // 新步骤的转移条件可能当场就成立（例如中转步骤），因此再排一轮。
            if (record.Status == QuestStatus.Active) dirty.Add(definition.QuestId);
        }

        /// <summary>进入一个步骤：重建倒排索引、执行进入动作。</summary>
        private void EnterStep(QuestDefinition definition, QuestRecord record, string stepId)
        {
            record.SetStep(stepId);
            QuestStep step = definition.FindStep(stepId);
            ReindexQuest(definition, step);
            if (step != null) RunActions(definition, record, step.EnterActions);
        }

        /// <summary>按「恰好一次」语义执行一组动作。</summary>
        private void RunActions(QuestDefinition definition, QuestRecord record, IReadOnlyList<QuestAction> actions)
        {
            if (actions == null) return;
            for (int index = 0; index < actions.Count; index++)
            {
                QuestAction action = actions[index];
                if (action == null || record.HasExecuted(action.ActionId)) continue;
                record.MarkExecuted(action.ActionId);
                action.Execute(new QuestActionContext(definition.QuestId, variables, PublishReward, StartQuest, FinishQuest));
            }
        }

        /// <summary>任务回滚到更早步骤时，清除声明了可重跑的动作的执行记录。</summary>
        private void ClearRerunnableActions(QuestDefinition definition, QuestRecord record, string targetStepId, string currentStepId)
        {
            int targetIndex = IndexOfStep(definition, targetStepId);
            int currentIndex = IndexOfStep(definition, currentStepId);
            if (targetIndex < 0 || currentIndex < 0 || targetIndex >= currentIndex) return;
            for (int index = targetIndex; index <= currentIndex && index < definition.Steps.Count; index++)
            {
                IReadOnlyList<QuestAction> actions = definition.Steps[index].EnterActions;
                for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                {
                    QuestAction action = actions[actionIndex];
                    if (action != null && action.RerunOnRollback) record.ClearExecuted(action.ActionId);
                }
            }
        }

        /// <summary>查找步骤在有序列表中的下标；不存在时返回负一。</summary>
        private static int IndexOfStep(QuestDefinition definition, string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return -1;
            for (int index = 0; index < definition.Steps.Count; index++)
            {
                if (string.Equals(definition.Steps[index].StepId, stepId, StringComparison.Ordinal)) return index;
            }
            return -1;
        }

        /// <summary>把匹配的触发器写入任务变量。</summary>
        private void ApplyTriggers(IReadOnlyList<QuestTrigger> triggers, QuestEvent questEvent, QuestEventScope scope)
        {
            for (int index = 0; index < triggers.Count; index++)
            {
                QuestTrigger trigger = triggers[index];
                if (trigger == null || !string.Equals(trigger.EventName, questEvent.Name, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(trigger.Filter) && !StoryExpression.Compile(trigger.Filter).EvaluateBool(scope)) continue;
                double value = ResolveTriggerValue(trigger, questEvent, scope);
                double current = variables.Get(trigger.VariablePath).Kind == StoryValueKind.Number ? variables.Get(trigger.VariablePath).AsNumber() : 0d;
                double next = trigger.Op switch
                {
                    QuestTriggerOp.Set => value,
                    QuestTriggerOp.Increment => current + value,
                    QuestTriggerOp.Max => Math.Max(current, value),
                    _ => value
                };
                variables.Set(trigger.VariablePath, new StoryValue(next));
            }
        }

        /// <summary>解析触发器要写入的数值；缺省时累加取载荷 count，赋值取一。</summary>
        private static double ResolveTriggerValue(QuestTrigger trigger, QuestEvent questEvent, QuestEventScope scope)
        {
            if (!string.IsNullOrWhiteSpace(trigger.ValueExpr)) return StoryExpression.Compile(trigger.ValueExpr).Evaluate(scope).AsNumber();
            if (questEvent.HasPayload("count")) return questEvent.GetPayload("count").AsNumber();
            return 1d;
        }

        /// <summary>求值一个条件表达式；留空视为恒真。</summary>
        private bool EvaluateCondition(string expression)
        {
            return string.IsNullOrWhiteSpace(expression) || StoryExpression.Compile(expression).EvaluateBool(variables);
        }

        /// <summary>求值一个数值表达式；留空返回零。</summary>
        private double EvaluateNumber(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return 0d;
            StoryValue value = StoryExpression.Compile(expression).Evaluate(variables);
            return value.Kind == StoryValueKind.Number ? value.AsNumber() : 0d;
        }

        /// <summary>判断一个目标行是否完成；未配置显式判定时按进度与数量比较。</summary>
        private bool IsObjectiveDone(QuestObjective objective, double current, double target)
        {
            if (!string.IsNullOrWhiteSpace(objective.DoneExpr)) return EvaluateCondition(objective.DoneExpr);
            return target > 0d && current >= target;
        }

        /// <summary>判断一个任务的解锁条件是否成立。</summary>
        private bool IsUnlocked(QuestDefinition definition)
        {
            return EvaluateCondition(definition.UnlockCondition);
        }

        /// <summary>供变量存储解析 <c>quest.&lt;id&gt;.status</c> 的推导值。</summary>
        private QuestStatus? ResolveStatusForVariable(string questId)
        {
            if (!definitions.TryGetValue(questId, out QuestDefinition definition)) return null;
            if (records.TryGetValue(questId, out QuestRecord record)) return record.Status;
            return IsUnlocked(definition) ? QuestStatus.Available : QuestStatus.Locked;
        }

        /// <summary>把任务的解锁条件引用的路径登记进倒排索引。</summary>
        private void IndexUnlockCondition(QuestDefinition definition)
        {
            IndexExpression(definition.UnlockCondition, definition.QuestId);
        }

        /// <summary>
        /// 重建一个任务在倒排索引中的登记。
        /// 只登记「当前真正会被求值」的表达式——当前步骤的转移条件与目标行，
        /// 因此一次变量变化只弄脏真正相关的任务。
        /// </summary>
        private void ReindexQuest(QuestDefinition definition, QuestStep step)
        {
            RemoveFromIndex(definition.QuestId);
            IndexUnlockCondition(definition);
            IndexBindingConditions(definition, definition.DialogueBindings);
            if (step == null) return;
            for (int index = 0; index < step.Transitions.Count; index++) IndexExpression(step.Transitions[index].Condition, definition.QuestId);
            for (int index = 0; index < step.Objectives.Count; index++)
            {
                QuestObjective objective = step.Objectives[index];
                IndexExpression(objective.ProgressExpr, definition.QuestId);
                IndexExpression(objective.TargetExpr, definition.QuestId);
                IndexExpression(objective.DoneExpr, definition.QuestId);
            }
            IndexBindingConditions(definition, step.DialogueBindings);
        }

        /// <summary>把一组对话绑定的条件登记进倒排索引，使标记失效同样由通知驱动。</summary>
        private void IndexBindingConditions(QuestDefinition definition, IReadOnlyList<DialogueBinding> bindings)
        {
            for (int index = 0; index < bindings.Count; index++) IndexExpression(bindings[index].Condition, definition.QuestId);
        }

        /// <summary>把一条表达式引用的全部路径登记到指定任务名下。</summary>
        private void IndexExpression(string expression, string questId)
        {
            if (string.IsNullOrWhiteSpace(expression)) return;
            List<string> identifiers = new List<string>();
            // CollectIdentifiers 的函数集合不接受 null；本索引只关心标识符路径，函数名收进一个丢弃缓冲。
            StoryExpression.Compile(expression).CollectIdentifiers(identifiers, discardedFunctionNames);
            discardedFunctionNames.Clear();
            for (int index = 0; index < identifiers.Count; index++)
            {
                if (!pathIndex.TryGetValue(identifiers[index], out HashSet<string> owners))
                {
                    owners = new HashSet<string>(StringComparer.Ordinal);
                    pathIndex[identifiers[index]] = owners;
                }
                owners.Add(questId);
            }
        }

        /// <summary>把一个任务从倒排索引中整体摘除。</summary>
        private void RemoveFromIndex(string questId)
        {
            List<string> empties = null;
            foreach (KeyValuePair<string, HashSet<string>> pair in pathIndex)
            {
                if (!pair.Value.Remove(questId) || pair.Value.Count > 0) continue;
                empties ??= new List<string>();
                empties.Add(pair.Key);
            }
            if (empties == null) return;
            for (int index = 0; index < empties.Count; index++) pathIndex.Remove(empties[index]);
        }

        /// <summary>变量变化时只弄脏真正引用该路径的任务。</summary>
        private void OnVariableChanged(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (pathIndex.TryGetValue(path, out HashSet<string> owners))
            {
                foreach (string questId in owners) dirty.Add(questId);
            }
            InvalidateAllNpcs();
        }

        /// <summary>把当前全部对话绑定涉及的 NPC 标记为待刷新。</summary>
        private void InvalidateAllNpcs()
        {
            foreach (KeyValuePair<string, QuestDefinition> pair in definitions)
            {
                CollectNpcIds(pair.Value.DialogueBindings);
                for (int index = 0; index < pair.Value.Steps.Count; index++) CollectNpcIds(pair.Value.Steps[index].DialogueBindings);
            }
        }

        /// <summary>把一组绑定涉及的 NPC 标识收进待刷新集合。</summary>
        private void CollectNpcIds(IReadOnlyList<DialogueBinding> bindings)
        {
            for (int index = 0; index < bindings.Count; index++)
            {
                if (!string.IsNullOrEmpty(bindings[index].NpcId)) invalidatedNpcs.Add(bindings[index].NpcId);
            }
        }

        /// <summary>把匹配指定 NPC 且条件成立的绑定收进候选缓冲。</summary>
        private void CollectCandidates(QuestDefinition definition, IReadOnlyList<DialogueBinding> bindings, string npcId)
        {
            for (int index = 0; index < bindings.Count; index++)
            {
                DialogueBinding binding = bindings[index];
                if (binding == null || !string.Equals(binding.NpcId, npcId, StringComparison.Ordinal)) continue;
                if (string.IsNullOrWhiteSpace(binding.StoryLocation)) continue;
                if (!EvaluateCondition(binding.Condition)) continue;
                candidateBuffer.Add(new DialogueCandidate(definition.QuestId, binding));
            }
        }

        /// <summary>
        /// 比较两条候选绑定。
        /// 优先级高者胜出；同优先级时当前追踪的任务优先——这是原神的实际行为：
        /// 玩家追踪哪条线，NPC 就优先说哪条线的话。任务标识字典序只作兜底，保证结果确定。
        /// </summary>
        private int Compare(DialogueCandidate left, DialogueCandidate right)
        {
            if (left.Binding.Priority != right.Binding.Priority) return left.Binding.Priority.CompareTo(right.Binding.Priority);
            bool leftTracked = string.Equals(left.QuestId, TrackedQuestId, StringComparison.Ordinal);
            bool rightTracked = string.Equals(right.QuestId, TrackedQuestId, StringComparison.Ordinal);
            if (leftTracked != rightTracked) return leftTracked ? 1 : -1;
            return string.CompareOrdinal(right.QuestId, left.QuestId);
        }

        /// <summary>判断当前追踪的任务是否仍在进行中。</summary>
        private bool IsTrackingActiveQuest()
        {
            return !string.IsNullOrEmpty(TrackedQuestId) && records.TryGetValue(TrackedQuestId, out QuestRecord record) && record.Status == QuestStatus.Active;
        }

        /// <summary>被追踪的任务离开进行中时，自动改追另一条仍在进行的线；没有则清空。</summary>
        private void RetargetTrackingIfNeeded()
        {
            if (IsTrackingActiveQuest()) return;
            IReadOnlyList<string> active = GetActiveQuestIds();
            TrackedQuestId = active.Count > 0 ? active[0] : null;
        }

        /// <summary>发布一条任务变化通知；状态与步骤都没变时不发。</summary>
        private void PublishChanged(string questId, QuestStatus previousStatus, QuestStatus currentStatus, string previousStep, string currentStep)
        {
            QuestChanged changed = new QuestChanged(questId, previousStatus, currentStatus, previousStep, currentStep);
            if (!changed.StatusChanged && !changed.StepChanged) return;
            RetargetTrackingIfNeeded();
            InvalidateAllNpcs();
            QuestChanged?.Invoke(changed);
        }

        /// <summary>转发一条奖励声明。</summary>
        private void PublishReward(QuestRewardGranted reward)
        {
            RewardGranted?.Invoke(reward);
        }

        /// <summary>由动作启动另一个任务。</summary>
        private void StartQuest(string questId)
        {
            if (!definitions.TryGetValue(questId, out QuestDefinition definition)) throw new KeyNotFoundException($"StartQuestAction references unknown quest '{questId}'.");
            if (GetStatus(questId) == QuestStatus.Active) return;
            Activate(definition);
        }

        /// <summary>由动作直接完成另一个任务。</summary>
        private void FinishQuest(string questId)
        {
            if (!definitions.TryGetValue(questId, out QuestDefinition definition)) throw new KeyNotFoundException($"FinishQuestAction references unknown quest '{questId}'.");
            if (!records.TryGetValue(questId, out QuestRecord record))
            {
                record = new QuestRecord(questId);
                records[questId] = record;
            }
            QuestStatus previous = record.Status;
            string previousStep = record.StepId;
            record.SetStatus(QuestStatus.Completed);
            record.SetStep(null);
            RunActions(definition, record, definition.CompleteActions);
            ReindexQuest(definition, null);
            PublishChanged(questId, previous, QuestStatus.Completed, previousStep, null);
        }

        /// <summary>取用任务配置；未注册的任务是调用方错误，直接抛出。</summary>
        private QuestDefinition RequireDefinition(string questId)
        {
            if (!definitions.TryGetValue(questId, out QuestDefinition definition)) throw new KeyNotFoundException($"Quest '{questId}' is not registered.");
            return definition;
        }

        /// <summary>一条参与解析的候选对话绑定。</summary>
        private readonly struct DialogueCandidate
        {
            /// <summary>创建一条候选。</summary>
            internal DialogueCandidate(string questId, DialogueBinding binding)
            {
                QuestId = questId;
                Binding = binding;
            }

            /// <summary>获取绑定所属的任务标识。</summary>
            internal string QuestId { get; }

            /// <summary>获取绑定本身。</summary>
            internal DialogueBinding Binding { get; }
        }
    }

    /// <summary>
    /// 触发器过滤表达式的求值作用域：在任务变量之上叠一层只在此处可见的 <c>e.*</c> 事件载荷。
    /// 载荷只在触发器 filter 内可见，任何其他表达式都读不到它。
    /// </summary>
    internal sealed class QuestEventScope : IStoryVariableResolver
    {
        private readonly QuestEvent questEvent;
        private readonly IStoryVariableResolver inner;

        /// <summary>创建一次事件求值作用域。</summary>
        internal QuestEventScope(QuestEvent questEvent, IStoryVariableResolver inner)
        {
            this.questEvent = questEvent;
            this.inner = inner;
        }

        /// <inheritdoc />
        public bool TryResolve(string path, out StoryValue value)
        {
            string prefix = QuestEventNames.PayloadRoot + ".";
            if (path != null && path.StartsWith(prefix, StringComparison.Ordinal))
            {
                string key = path.Substring(prefix.Length);
                if (questEvent.HasPayload(key))
                {
                    value = questEvent.GetPayload(key);
                    return true;
                }
                value = StoryValue.None;
                return false;
            }
            return inner.TryResolve(path, out value);
        }

        /// <inheritdoc />
        public bool IsKnownRoot(string root)
        {
            return string.Equals(root, QuestEventNames.PayloadRoot, StringComparison.Ordinal) || inner.IsKnownRoot(root);
        }
    }
}
