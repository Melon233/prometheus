using System;
using System.Collections.Generic;
using Xuan.Prometheus.Expression;

namespace Xuan.Prometheus.Quest
{
    /// <summary>
    /// 任务配置的静态校验器。
    ///
    /// 目标是**在跑起来之前**把配置错误全部暴露。其中可达性、无恒真自环、
    /// 事件名已登记这三条是本设计独有的静态保障——原神那套「枚举 + Lua」的结构做不到这类检查，
    /// 只能靠跑到那一步才发现。
    ///
    /// 校验在 <c>RegisterDefinition</c> 时执行并抛出，因此一份坏配置进不了运行时。
    /// </summary>
    public static class QuestValidator
    {
        /// <summary>校验一份任务配置；有任何错误时抛出，消息里逐条列出。</summary>
        /// <param name="definition">待校验的任务配置。</param>
        /// <param name="resolver">用于承认命名空间的解析器。</param>
        public static void ValidateOrThrow(QuestDefinition definition, IStoryVariableResolver resolver)
        {
            IReadOnlyList<string> errors = Validate(definition, resolver);
            if (errors.Count == 0) return;
            throw new InvalidOperationException($"任务配置 '{(definition == null ? "<null>" : definition.QuestId)}' 校验失败：\n- {string.Join("\n- ", errors)}");
        }

        /// <summary>校验一份任务配置并返回全部错误；无错误时返回空列表。</summary>
        /// <param name="definition">待校验的任务配置。</param>
        /// <param name="resolver">用于承认命名空间的解析器；为空时跳过命名空间检查。</param>
        public static IReadOnlyList<string> Validate(QuestDefinition definition, IStoryVariableResolver resolver)
        {
            List<string> errors = new List<string>();
            if (definition == null)
            {
                errors.Add("任务配置为空。");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(definition.QuestId)) errors.Add("缺少 QuestId。");
            // 任务标识不能含点：quest.<id>.<name> 的解析靠点分段，含点会让「任务名」与「变量名」无法区分。
            else if (definition.QuestId.IndexOf('.') >= 0) errors.Add($"QuestId '{definition.QuestId}' 不允许包含点号。");
            if (definition.Steps.Count == 0) errors.Add("至少需要一个步骤。");

            ValidateExpression(definition.UnlockCondition, "解锁条件", resolver, errors);
            ValidateActions(definition.CompleteActions, "任务完成动作", errors, new HashSet<string>(StringComparer.Ordinal));
            ValidateTriggers(definition.Triggers, "任务级触发器", resolver, errors);
            ValidateBindings(definition.DialogueBindings, "任务级对话绑定", resolver, errors);

            HashSet<string> stepIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> actionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < definition.CompleteActions.Count; index++)
            {
                QuestAction action = definition.CompleteActions[index];
                if (action != null && !string.IsNullOrWhiteSpace(action.ActionId)) actionIds.Add(action.ActionId);
            }

            for (int index = 0; index < definition.Steps.Count; index++)
            {
                QuestStep step = definition.Steps[index];
                if (step == null)
                {
                    errors.Add($"步骤 {index} 为空。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(step.StepId)) errors.Add($"步骤 {index} 缺少 StepId。");
                else if (!stepIds.Add(step.StepId)) errors.Add($"步骤标识 '{step.StepId}' 重复。");

                HashSet<string> objectiveIds = new HashSet<string>(StringComparer.Ordinal);
                for (int objectiveIndex = 0; objectiveIndex < step.Objectives.Count; objectiveIndex++)
                {
                    QuestObjective objective = step.Objectives[objectiveIndex];
                    if (objective == null)
                    {
                        errors.Add($"步骤 '{step.StepId}' 的目标 {objectiveIndex} 为空。");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(objective.ObjectiveId)) errors.Add($"步骤 '{step.StepId}' 的目标 {objectiveIndex} 缺少 ObjectiveId。");
                    else if (!objectiveIds.Add(objective.ObjectiveId)) errors.Add($"步骤 '{step.StepId}' 内目标标识 '{objective.ObjectiveId}' 重复。");
                    ValidateExpression(objective.ProgressExpr, $"步骤 '{step.StepId}' 目标 '{objective.ObjectiveId}' 的进度表达式", resolver, errors);
                    ValidateExpression(objective.TargetExpr, $"步骤 '{step.StepId}' 目标 '{objective.ObjectiveId}' 的数量表达式", resolver, errors);
                    ValidateExpression(objective.DoneExpr, $"步骤 '{step.StepId}' 目标 '{objective.ObjectiveId}' 的完成表达式", resolver, errors);
                }

                for (int transitionIndex = 0; transitionIndex < step.Transitions.Count; transitionIndex++)
                {
                    QuestTransition transition = step.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        errors.Add($"步骤 '{step.StepId}' 的转移 {transitionIndex} 为空。");
                        continue;
                    }
                    ValidateExpression(transition.Condition, $"步骤 '{step.StepId}' 转移 {transitionIndex} 的条件", resolver, errors);
                    if (transition.Kind == QuestTransitionKind.ToStep)
                    {
                        if (string.IsNullOrWhiteSpace(transition.TargetStepId)) errors.Add($"步骤 '{step.StepId}' 转移 {transitionIndex} 缺少 TargetStepId。");
                        // 恒真自环会让帧末推进永远无法收敛，必须在配置期拦住。
                        else if (string.Equals(transition.TargetStepId, step.StepId, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(transition.Condition)) errors.Add($"步骤 '{step.StepId}' 转移 {transitionIndex} 是条件恒真的自环。");
                    }
                    ValidateActions(transition.Actions, $"步骤 '{step.StepId}' 转移 {transitionIndex} 的动作", errors, actionIds);
                }

                ValidateActions(step.EnterActions, $"步骤 '{step.StepId}' 的进入动作", errors, actionIds);
                ValidateTriggers(step.Triggers, $"步骤 '{step.StepId}' 的触发器", resolver, errors);
                ValidateBindings(step.DialogueBindings, $"步骤 '{step.StepId}' 的对话绑定", resolver, errors);
            }

            if (errors.Count == 0) ValidateReachability(definition, stepIds, errors);
            return errors;
        }

        /// <summary>校验每个步骤都可从起始步骤到达，且存在至少一条通向完成的路径。</summary>
        private static void ValidateReachability(QuestDefinition definition, HashSet<string> stepIds, List<string> errors)
        {
            for (int index = 0; index < definition.Steps.Count; index++)
            {
                QuestStep step = definition.Steps[index];
                for (int transitionIndex = 0; transitionIndex < step.Transitions.Count; transitionIndex++)
                {
                    QuestTransition transition = step.Transitions[transitionIndex];
                    if (transition.Kind != QuestTransitionKind.ToStep) continue;
                    if (!stepIds.Contains(transition.TargetStepId)) errors.Add($"步骤 '{step.StepId}' 转移 {transitionIndex} 指向不存在的步骤 '{transition.TargetStepId}'。");
                }
            }
            if (errors.Count > 0) return;

            HashSet<string> reached = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> pending = new Queue<string>();
            bool hasCompletePath = false;
            pending.Enqueue(definition.FirstStepId);
            reached.Add(definition.FirstStepId);
            while (pending.Count > 0)
            {
                QuestStep step = definition.FindStep(pending.Dequeue());
                if (step == null) continue;
                for (int index = 0; index < step.Transitions.Count; index++)
                {
                    QuestTransition transition = step.Transitions[index];
                    if (transition.Kind == QuestTransitionKind.CompleteQuest)
                    {
                        hasCompletePath = true;
                        continue;
                    }
                    if (transition.Kind != QuestTransitionKind.ToStep) continue;
                    if (reached.Add(transition.TargetStepId)) pending.Enqueue(transition.TargetStepId);
                }
            }
            for (int index = 0; index < definition.Steps.Count; index++)
            {
                if (!reached.Contains(definition.Steps[index].StepId)) errors.Add($"步骤 '{definition.Steps[index].StepId}' 从起始步骤不可达。");
            }
            if (!hasCompletePath) errors.Add("不存在任何通向 CompleteQuest 的路径，该任务永远无法完成。");
        }

        /// <summary>校验一组一次性动作的标识唯一且非空。</summary>
        private static void ValidateActions(IReadOnlyList<QuestAction> actions, string scope, List<string> errors, HashSet<string> actionIds)
        {
            if (actions == null) return;
            for (int index = 0; index < actions.Count; index++)
            {
                QuestAction action = actions[index];
                if (action == null)
                {
                    errors.Add($"{scope} 的第 {index} 项为空。");
                    continue;
                }
                // 动作标识就是执行记录的键；缺失或重复都会让「恰好一次」失效。
                if (string.IsNullOrWhiteSpace(action.ActionId)) errors.Add($"{scope} 的第 {index} 项缺少 ActionId。");
                else if (!actionIds.Add(action.ActionId)) errors.Add($"动作标识 '{action.ActionId}' 在任务内重复。");
            }
        }

        /// <summary>校验一组触发器的事件名已登记、写入路径合法、过滤表达式可编译。</summary>
        private static void ValidateTriggers(IReadOnlyList<QuestTrigger> triggers, string scope, IStoryVariableResolver resolver, List<string> errors)
        {
            if (triggers == null) return;
            for (int index = 0; index < triggers.Count; index++)
            {
                QuestTrigger trigger = triggers[index];
                if (trigger == null)
                {
                    errors.Add($"{scope} 的第 {index} 项为空。");
                    continue;
                }
                if (!QuestEventNames.IsKnown(trigger.EventName)) errors.Add($"{scope} 的第 {index} 项引用了未登记的事件名 '{trigger.EventName}'；请先在 QuestEventNames 中登记。");
                if (string.IsNullOrWhiteSpace(trigger.VariablePath)) errors.Add($"{scope} 的第 {index} 项缺少 VariablePath。");
                // 触发器只能写自己的命名空间；写投影会让「谁拥有这个值」变得不可判断。
                else if (!trigger.VariablePath.StartsWith(QuestVariables.QuestRoot + ".", StringComparison.Ordinal)) errors.Add($"{scope} 的第 {index} 项写入路径 '{trigger.VariablePath}' 必须位于 '{QuestVariables.QuestRoot}.' 之下。");
                ValidateExpression(trigger.ValueExpr, $"{scope} 第 {index} 项的取值表达式", resolver, errors);
                // 过滤表达式可以引用 e.*，因此用带载荷作用域的解析器校验。
                ValidateExpression(trigger.Filter, $"{scope} 第 {index} 项的过滤表达式", resolver == null ? null : new QuestEventScope(new QuestEvent(trigger.EventName), resolver), errors);
            }
        }

        /// <summary>校验一组对话绑定的标识、NPC、剧情地址与条件表达式。</summary>
        private static void ValidateBindings(IReadOnlyList<DialogueBinding> bindings, string scope, IStoryVariableResolver resolver, List<string> errors)
        {
            if (bindings == null) return;
            HashSet<string> bindingIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < bindings.Count; index++)
            {
                DialogueBinding binding = bindings[index];
                if (binding == null)
                {
                    errors.Add($"{scope} 的第 {index} 项为空。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(binding.BindingId)) errors.Add($"{scope} 的第 {index} 项缺少 BindingId。");
                else if (!bindingIds.Add(binding.BindingId)) errors.Add($"{scope} 内绑定标识 '{binding.BindingId}' 重复。");
                if (string.IsNullOrWhiteSpace(binding.NpcId)) errors.Add($"{scope} 的第 {index} 项缺少 NpcId。");
                if (string.IsNullOrWhiteSpace(binding.StoryLocation)) errors.Add($"{scope} 的第 {index} 项缺少 StoryLocation。");
                ValidateExpression(binding.Condition, $"{scope} 第 {index} 项的条件", resolver, errors);
            }
        }

        /// <summary>校验一条表达式可编译且引用的命名空间被承认。</summary>
        private static void ValidateExpression(string expression, string scope, IStoryVariableResolver resolver, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(expression)) return;
            if (resolver == null)
            {
                try
                {
                    StoryExpression.Compile(expression);
                }
                catch (Exception exception)
                {
                    errors.Add($"{scope} 无法编译：{exception.Message}");
                }
                return;
            }
            if (!StoryExpression.Validate(expression, resolver, out string error)) errors.Add($"{scope} 校验失败：{error}");
        }
    }
}
